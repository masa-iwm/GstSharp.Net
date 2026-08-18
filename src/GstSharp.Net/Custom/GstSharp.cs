using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst;
using Gst.GLib;
using Gst.GObject;
using Gst.Interop;

/// <summary>
/// The entry point of GstSharp.Net: it loads the native libraries, initialises
/// GStreamer and connects the runtime to the application.
/// </summary>
/// <remarks>
/// The class deliberately lives in the global namespace, so that
/// <c>GstSharp.Initialize()</c> reads the same from every file, whether it has
/// <c>using Gst;</c> or not. Code that sits inside a namespace which itself
/// starts with <c>GstSharp</c> has to spell the call out as
/// <c>global::GstSharp.Initialize()</c>.
/// </remarks>
public static class GstSharp
{
    private static readonly object Sync = new();

    private static bool _initialized;
    private static bool _moduleSweepInstalled;
    private static bool _appliedSkipNativeInit;
    private static string[]? _appliedInitArgs;
    private static Gst.Version _version;

    /// <summary>
    /// Raised for every exception that was caught on a native callback
    /// boundary, where it must not be allowed to unwind into native code.
    /// Handlers must not throw.
    /// </summary>
    /// <remarks>
    /// This forwards <see cref="ExceptionTrap.UnhandledException"/>. Without a
    /// handler such an exception is written to the standard error stream; set
    /// <c>GSTSHARP_FAILFAST=1</c> in the environment to turn it into an
    /// immediate <see cref="Environment.FailFast(string, Exception)"/> instead.
    /// </remarks>
    public static event Action<Exception>? UnhandledCallbackException
    {
        add => ExceptionTrap.UnhandledException += value;
        remove => ExceptionTrap.UnhandledException -= value;
    }

    /// <summary>
    /// Raised the first time an object is wrapped as one of its base types
    /// because its exact type is not in the type registry, once per native
    /// type. Handlers must not throw.
    /// </summary>
    /// <remarks>
    /// This forwards <see cref="TypeRegistry.Fallback"/>. Wrapping an object as
    /// a base type is normal — no binding wraps the type of every element a
    /// plugin implements — so nothing is reported anywhere without a handler.
    /// It is a diagnostic for the one case where the fallback is a bug: a type
    /// that a binding assembly does wrap, seen as its base type because the
    /// module of that assembly had not registered yet, which is otherwise
    /// silent.
    /// </remarks>
    public static event Action<TypeFallback>? TypeFallback
    {
        add => TypeRegistry.Fallback += value;
        remove => TypeRegistry.Fallback -= value;
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Initialize"/> has run.
    /// </summary>
    public static bool IsInitialized
    {
        get
        {
            lock (Sync)
            {
                return _initialized;
            }
        }
    }

    /// <summary>
    /// Gets the version of the native GStreamer library that was loaded.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Initialize"/> has not run yet.
    /// </exception>
    public static Gst.Version NativeVersion
    {
        get
        {
            lock (Sync)
            {
                if (!_initialized)
                {
                    throw new InvalidOperationException(
                        "The native version is only known once GstSharp.Initialize has been called.");
                }

                return _version;
            }
        }
    }

    /// <summary>
    /// Loads the native libraries and initialises GStreamer.
    /// </summary>
    /// <param name="options">
    /// Where the native libraries are and how GStreamer should be initialised,
    /// or <see langword="null"/> for the defaults.
    /// </param>
    /// <remarks>
    /// <para>
    /// The call is idempotent and safe to make from several threads: the first
    /// one initialises, the others return once it is done. A second call with
    /// <see langword="null"/> options does nothing. A second call that asks for
    /// something the first one did not do fails, because none of it can be
    /// changed once the native libraries are loaded: a
    /// <see cref="GstSharpOptions.NativeSearchPath"/> or a
    /// <see cref="GstSharpOptions.WindowsFlavor"/> that does not match the
    /// installation in use, arguments in
    /// <see cref="GstSharpOptions.InitArgs"/> that differ from the ones that
    /// were passed, or a request to initialise GStreamer after
    /// <see cref="GstSharpOptions.SkipNativeInit"/> suppressed it. Options that
    /// are <see langword="null"/> mean "no preference" and never conflict.
    /// </para>
    /// <para>
    /// <see cref="GstSharpOptions.SkipNativeInit"/> is a <see cref="bool"/> and
    /// has no <see langword="null"/> to say that with, so what says it there is
    /// never having assigned the property: an options object that was left
    /// alone takes no part in the check, and one that was assigned — including
    /// one assigned <see langword="false"/> — does. Handing a second call a
    /// fresh <see cref="GstSharpOptions"/> for the sake of another option is
    /// therefore not a demand that <c>gst_init</c> run after all.
    /// </para>
    /// <para>
    /// The <c>GError</c> of a failed <c>gst_init_check</c> is raised as a
    /// <see cref="GException"/>.
    /// </para>
    /// <para>
    /// Every binding assembly puts its types into the type registry from a
    /// module initialiser, and the runtime runs a module initialiser before the
    /// first <em>call</em> into that assembly, not before one of its types is
    /// merely named in a cast. This call therefore runs the module initialiser
    /// of every <c>GstSharp.Net</c> assembly that is loaded, and of every one
    /// that is loaded later, so that <c>GetByName("sink") as AppSink</c> works
    /// without the application having called into <c>GstSharp.Net.App</c>
    /// first. Under ahead of time compilation the initialisers have all run at
    /// startup and the sweep finds nothing to do.
    /// </para>
    /// <para>
    /// A wrapper keeps the type it was created with. If an object is wrapped
    /// before the module that knows its type has registered, the wrapper stays
    /// the base type it was built as for as long as the application holds it —
    /// retyping a live object is not something a binding can do behind the
    /// application's back. Initialising before anything else touches GStreamer
    /// is what avoids that; <see cref="TypeFallback"/> makes it visible when it
    /// happens anyway.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The options conflict with the ones of the first call.
    /// </exception>
    /// <exception cref="GstNativeLoadException">
    /// The native libraries could not be found.
    /// </exception>
    /// <exception cref="GException">GStreamer refused to initialise.</exception>
    public static void Initialize(GstSharpOptions? options = null)
    {
        lock (Sync)
        {
            if (_initialized)
            {
                EnsureCompatible(options);
                return;
            }

            GstSharpOptions applied = options ?? new GstSharpOptions();

            // 1. Where to load the native libraries from. Nothing is loaded yet.
            NativeLoader.Configure(applied.NativeSearchPath, applied.WindowsFlavor);

            // 2. Make sure the module initialiser of every binding assembly has
            //    run, so that the registry knows the types of all of them and
            //    not only of the ones the application happened to call into.
            RunBindingModuleInitializers();

            // 3. Initialise GStreamer itself, before anything asks a type about
            //    itself. A get_type function of a GStreamer library is entitled
            //    to assume that gst_init has run — several of them build a
            //    static table or touch a registry that gst_init creates, and
            //    gst_encoding_audio_profile_get_type is the one that says so
            //    out loud, with three GLib criticals — so the freeze below has
            //    to come after this.
            if (!applied.SkipNativeInit)
            {
                NativeInit(applied.InitArgs);
            }

            // 4. Resolve the get_type function of every type that the binding
            //    assemblies registered from their module initialisers. A module
            //    that registers after this point unfreezes the registry again,
            //    and the next lookup rebuilds it.
            //
            //    The native libraries are already loaded by the time this runs,
            //    because the call above went through the loader. Under
            //    SkipNativeInit this is instead the first native call the
            //    binding makes, and initialising GStreamer before handing
            //    control to the binding is the contract that option carries.
            TypeRegistry.Freeze();

            // 5. Remember what the library reports about itself.
            _version = Gst.Version.FromNative();

            _appliedSkipNativeInit = applied.SkipNativeInit;
            _appliedInitArgs = applied.InitArgs is null ? null : (string[])applied.InitArgs.Clone();
            _initialized = true;
        }
    }

    /// <summary>
    /// Releases the native objects whose wrappers have been collected.
    /// </summary>
    /// <remarks>
    /// A finalizer must not call into native code, so it enqueues the release
    /// of the object instead. The queue is drained whenever a wrapper is looked
    /// up and from the idle callback of a running main loop; an application
    /// that does neither, for example a batch job without a main loop, can
    /// drain it here.
    /// </remarks>
    public static void DrainPendingReleases() => Gst.GObject.Object.DrainPendingReleases();

    /// <summary>
    /// Runs the module initialiser of every binding assembly that is loaded,
    /// and arranges for the ones that are loaded later to be run as well.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A module initialiser of a library is lazy on CoreCLR: it runs before the
    /// first call into the assembly, and a cast to one of its types is not a
    /// call. That is the whole of the silent failure described under
    /// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#the-gtype-registry">The GType registry</see>,
    /// because the initialiser is what registers the types. Running it here is
    /// the durable fix, and running it twice is harmless — the runtime runs a
    /// module initialiser at most once.
    /// </para>
    /// <para>
    /// The handler is subscribed before the sweep, so that an assembly that is
    /// loaded while the sweep runs is covered by one or the other. It does not
    /// freeze the registry: registering a module unfreezes it and the next
    /// lookup rebuilds it, which keeps native <c>get_type</c> calls out of an
    /// assembly load callback.
    /// </para>
    /// <para>
    /// Ahead of time compilation has no lazy module initialisers: they have all
    /// run by the time the entry point does, and no assembly is ever loaded
    /// afterwards. <see cref="RuntimeFeature.IsDynamicCodeSupported"/>
    /// distinguishes the two runtimes, and the sweep is skipped where it has
    /// nothing to do.
    /// </para>
    /// </remarks>
    private static void RunBindingModuleInitializers()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            return;
        }

        if (!_moduleSweepInstalled)
        {
            _moduleSweepInstalled = true;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            RunModuleInitializer(assembly);
        }
    }

    /// <summary>
    /// Runs the module initialiser of a binding assembly that was loaded after
    /// <see cref="Initialize"/>.
    /// </summary>
    /// <param name="sender">The application domain.</param>
    /// <param name="args">The assembly that was loaded.</param>
    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        _ = sender;
        RunModuleInitializer(args.LoadedAssembly);
    }

    /// <summary>
    /// Runs the module initialiser of one assembly, if it is a binding
    /// assembly.
    /// </summary>
    /// <param name="assembly">The assembly to consider.</param>
    private static void RunModuleInitializer(Assembly assembly)
    {
        if (assembly.IsDynamic ||
            assembly.GetName().Name is not string name ||
            !name.StartsWith("GstSharp.Net", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        }
        catch (Exception exception)
        {
            // An assembly that only shares the prefix of the name, or one whose
            // initialiser throws, must not take the initialisation of the
            // binding down with it.
            ExceptionTrap.Report(exception);
        }
    }

    private static void EnsureCompatible(GstSharpOptions? options)
    {
        if (options is null)
        {
            return;
        }

        // The loader knows which installation is pinned and rejects a path or a
        // flavor that does not match it.
        NativeLoader.Configure(options.NativeSearchPath, options.WindowsFlavor);

        if (ConflictsWithAppliedSkipNativeInit(options, _appliedSkipNativeInit))
        {
            throw new InvalidOperationException(
                "GstSharp was initialised with SkipNativeInit, so gst_init has not run and cannot run now.");
        }

        if (options.InitArgs is not null && !SameArguments(options.InitArgs, _appliedInitArgs))
        {
            throw new InvalidOperationException(
                "GStreamer has already been initialised with different arguments. " +
                "Pass InitArgs on the first call to GstSharp.Initialize.");
        }
    }

    /// <summary>
    /// Tests whether a second call asks for the initialisation that the first
    /// one suppressed.
    /// </summary>
    /// <param name="options">The options of the second call.</param>
    /// <param name="applied">
    /// Whether the first call was made with
    /// <see cref="GstSharpOptions.SkipNativeInit"/>.
    /// </param>
    /// <returns><see langword="true"/> when the two cannot both be honoured.</returns>
    /// <remarks>
    /// Every other option says "no preference" with a <see langword="null"/>,
    /// which a <see cref="bool"/> has no room for, so the third state of
    /// <see cref="GstSharpOptions.SkipNativeInit"/> is whether it was ever
    /// assigned. Without that, the unavoidable <see langword="false"/> of a
    /// fresh options object read as a demand that <c>gst_init</c> run, and a
    /// second <see cref="Initialize"/> that only wanted to state a search path
    /// failed for a preference its caller never expressed.
    /// </remarks>
    internal static bool ConflictsWithAppliedSkipNativeInit(GstSharpOptions options, bool applied) =>
        applied && options.SkipNativeInitSpecified && !options.SkipNativeInit;

    private static bool SameArguments(string[] requested, string[]? applied)
    {
        if (applied is null)
        {
            return requested.Length == 0;
        }

        return requested.AsSpan().SequenceEqual(applied);
    }

    private static unsafe void NativeInit(string[]? initArgs)
    {
        nint error = nint.Zero;

        if (initArgs is null || initArgs.Length == 0)
        {
            // gst_init_check accepts a null command line.
            Check(GstNative.InitCheck(null, null, &error), ref error);
            return;
        }

        // GStreamer hands the vector to the option parser of GLib, which always
        // takes the first entry for the name of the program.
        string[] arguments = new string[initArgs.Length + 1];
        arguments[0] = Environment.ProcessPath ?? "gstsharp";
        initArgs.CopyTo(arguments, 1);

        nint[] owned = new nint[arguments.Length];
        byte** argv = null;

        try
        {
            argv = (byte**)NativeMemory.AllocZeroed((nuint)arguments.Length + 1, (nuint)sizeof(byte*));

            for (int i = 0; i < arguments.Length; i++)
            {
                owned[i] = GMarshal.StringToUtf8Ptr(arguments[i]);
                argv[i] = (byte*)owned[i];
            }

            int argc = arguments.Length;
            byte** vector = argv;
            Check(GstNative.InitCheck(&argc, &vector, &error), ref error);
        }
        finally
        {
            // The strings are freed from the copy that was made above, not from
            // the vector: GStreamer removes the arguments it understands from
            // it, in place, without freeing them.
            foreach (nint argument in owned)
            {
                GMarshal.Free(argument);
            }

            NativeMemory.Free(argv);
        }
    }

    private static void Check(int result, ref nint error)
    {
        GException.ThrowIfSet(ref error);

        if (result == 0)
        {
            throw new GException("GStreamer could not be initialised, and did not report why.");
        }
    }
}
