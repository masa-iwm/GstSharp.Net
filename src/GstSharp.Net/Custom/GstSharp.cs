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
    /// The <c>GError</c> of a failed <c>gst_init_check</c> is raised as a
    /// <see cref="GException"/>.
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

            // 2. Resolve the get_type function of every type that the binding
            //    assemblies registered from their module initialisers. This is
            //    what loads the native libraries. The order relative to step 3
            //    is worth revisiting once the generated modules bring real
            //    entries: today every module is still empty.
            TypeRegistry.Freeze();

            // 3. Initialise GStreamer itself.
            if (!applied.SkipNativeInit)
            {
                NativeInit(applied.InitArgs);
            }

            // 4. Remember what the library reports about itself.
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

    private static void EnsureCompatible(GstSharpOptions? options)
    {
        if (options is null)
        {
            return;
        }

        // The loader knows which installation is pinned and rejects a path or a
        // flavor that does not match it.
        NativeLoader.Configure(options.NativeSearchPath, options.WindowsFlavor);

        if (!options.SkipNativeInit && _appliedSkipNativeInit)
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
