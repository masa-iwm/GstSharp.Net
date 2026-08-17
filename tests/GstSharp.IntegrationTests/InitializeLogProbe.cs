extern alias gstsharp;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Gst.Interop;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Watches the GLib log while GStreamer is initialised for the first time in
/// this process, and remembers what was logged.
/// </summary>
/// <remarks>
/// <para>
/// <c>GstSharp.Initialize</c> resolves the <c>get_type</c> function of every
/// registered type when it freezes the type registry, and a <c>get_type</c> of
/// a GStreamer library is entitled to assume that <c>gst_init</c> has run.
/// <c>gst_encoding_audio_profile_get_type</c> is the one that says so out loud:
/// asked before initialisation it logs three GLib criticals — a failed
/// <c>g_array_append_vals</c> assertion and two <c>hash_table != NULL</c> — and
/// registers a type that is missing entries. Freezing before initialising was
/// exactly that bug, and the criticals are the only outward sign of it.
/// </para>
/// <para>
/// Catching them needs three things that only a module initialiser can arrange.
/// The writer has to be installed before anything logs, and
/// <c>g_log_set_writer_func</c> may be called once per process. The module of
/// <c>GstSharp.Net.Pbutils</c> has to be loaded before the registry is frozen,
/// or the encoding profiles are not in it and the bug does not reproduce —
/// which is why the assembly is forced in here rather than left to whichever
/// test happens to run first. And the initialisation this measures has to be the
/// <em>first</em> one, because the call is idempotent: whoever gets there first
/// is the only one that freezes a cold registry. A module initialiser of the
/// test assembly runs before xunit builds so much as a fact attribute, so it is
/// the only place all three hold.
/// </para>
/// <para>
/// The writer forwards everything to <c>g_log_writer_default</c>, so the log of
/// the suite is what it always was; what it adds is a copy of the messages that
/// came out while <see cref="Failure"/> was being decided.
/// </para>
/// </remarks>
internal static unsafe partial class InitializeLogProbe
{
    /// <summary><c>G_LOG_LEVEL_ERROR</c> and <c>G_LOG_LEVEL_CRITICAL</c>.</summary>
    private const int Serious = 4 | 8;

    private static readonly object Sync = new();
    private static readonly List<string> Captured = [];

    private static bool _capturing;

    /// <summary>
    /// Gets the criticals GLib logged while GStreamer was initialised.
    /// </summary>
    internal static IReadOnlyList<string> InitializeCriticals { get; private set; } = [];

    /// <summary>
    /// Gets the exception the first initialisation threw, if it threw one.
    /// </summary>
    /// <remarks>
    /// A module initialiser that throws poisons every type of its assembly with
    /// a <see cref="TypeInitializationException"/> that says nothing useful, so
    /// the failure is remembered rather than raised. <see cref="GstFixture"/>
    /// calls <c>GstSharp.Initialize</c> again and reports it properly.
    /// </remarks>
    internal static Exception? Failure { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the probe installed its writer, which is
    /// what makes an empty <see cref="InitializeCriticals"/> mean anything.
    /// </summary>
    internal static bool IsInstalled { get; private set; }

    /// <summary>
    /// Installs the log writer, makes sure the Pbutils module is registered and
    /// initialises GStreamer, all before any test of this assembly runs.
    /// </summary>
    [ModuleInitializer]
    internal static void Install()
    {
        // The imports below resolve through the same installation as the
        // binding, which is what EnsureRegistered arranges; it is idempotent,
        // and GstFixture calls it again for the rest of the tests.
        NativeLoader.EnsureRegistered(typeof(InitializeLogProbe).Assembly);

        // Loading the assembly is what the sweep of GstSharp.Initialize needs
        // to find: it runs the module initialiser of every loaded binding
        // assembly, and that initialiser is what registers the encoding
        // profile types whose get_type functions the freeze then resolves.
        Pbutils = typeof(Gst.Pbutils.Discoverer).Assembly;

        try
        {
            TestNatives.LogSetWriterFunc(&Write, nint.Zero, nint.Zero);
            IsInstalled = true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Without a writer the probe measures nothing, and the test says so
            // rather than passing on an empty list it never filled.
            Failure = exception;
        }

        lock (Sync)
        {
            Captured.Clear();
            _capturing = true;
        }

        try
        {
            gstsharp::GstSharp.Initialize();
        }
        catch (Exception exception)
        {
            Failure = exception;
        }
        finally
        {
            lock (Sync)
            {
                _capturing = false;
                InitializeCriticals = Captured.ToArray();
                Captured.Clear();
            }
        }
    }

    /// <summary>The assembly whose types have to be in the registry before the freeze.</summary>
    private static System.Reflection.Assembly? Pbutils { get; set; }

    /// <summary>
    /// The <c>GLogWriterFunc</c> GLib calls for every message.
    /// </summary>
    /// <param name="logLevel">The <c>GLogLevelFlags</c> of the message.</param>
    /// <param name="fields">The structured fields of the message.</param>
    /// <param name="fieldCount">The number of fields.</param>
    /// <param name="userData">Unused.</param>
    /// <returns>What <c>g_log_writer_default</c> made of the message.</returns>
    /// <remarks>
    /// This runs on whichever thread logged, and an exception must not unwind
    /// into GLib, so everything the capture does is inside a guard. The message
    /// is formatted by GLib itself rather than by mirroring <c>GLogField</c>,
    /// which keeps a structure layout out of the test assembly.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int Write(int logLevel, nint fields, nuint fieldCount, nint userData)
    {
        try
        {
            if ((logLevel & Serious) != 0)
            {
                Capture(logLevel, fields, fieldCount);
            }
        }
        catch (Exception)
        {
            // A diagnostic must not break the logging it observes.
        }

        return TestNatives.LogWriterDefault(logLevel, fields, fieldCount, userData);
    }

    private static void Capture(int logLevel, nint fields, nuint fieldCount)
    {
        lock (Sync)
        {
            if (!_capturing)
            {
                return;
            }
        }

        nint formatted = TestNatives.LogWriterFormatFields(logLevel, fields, fieldCount, useColor: 0);
        string? message = ReadAndFree(formatted);

        lock (Sync)
        {
            if (_capturing && message is not null)
            {
                Captured.Add(message);
            }
        }
    }

    private static string? ReadAndFree(nint text)
    {
        if (text == nint.Zero)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)text));
        }
        finally
        {
            GMarshal.Free(text);
        }
    }
}
