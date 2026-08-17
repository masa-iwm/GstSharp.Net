using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Gst;

/// <content>
/// The parts of <c>gst-launch-1.0</c> that only exist on one operating system.
/// </content>
/// <remarks>
/// <para>
/// The C tool compiles three of these in or out with the preprocessor; one
/// managed binary carries all of them and lights each up where it belongs, so
/// the guards below are <see cref="OperatingSystem"/> tests rather than
/// <c>#if</c>. What the C source actually has, and what became of it:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>SIGINT</b> (<c>G_OS_UNIX</c> through <c>g_unix_signal_add</c>,
/// <c>G_OS_WIN32</c> through <c>SetConsoleCtrlHandler</c>) — portable here,
/// because <see cref="Console.CancelKeyPress"/> is both. It posts the same
/// <c>GstLaunchInterrupt</c> application message the C handler posts; see
/// <see cref="RequestInterrupt"/>.
/// </description></item>
/// <item><description>
/// <b>SIGHUP</b> (<c>G_OS_UNIX</c>) — a dot file snapshot, kept here on every
/// POSIX system.
/// </description></item>
/// <item><description>
/// <b>SIGSEGV and SIGQUIT</b> (<c>G_OS_UNIX &amp;&amp; !__APPLE__</c>, off with
/// <c>-f</c>) — only the SIGQUIT half carries over; see
/// <see cref="InstallFaultHandler"/>.
/// </description></item>
/// <item><description>
/// <b>The multimedia timer</b> (<c>HAVE_WINMM</c>) — kept here on Windows,
/// including the line it prints.
/// </description></item>
/// </list>
/// <para>
/// There is no SIGUSR1 or SIGUSR2 handling in <c>gst-launch.c</c>, and
/// <c>-p</c> is <c>--prog-name</c> rather than a signal switch, so no raw
/// platform signal number is needed: every signal the tool does handle has a
/// member of <see cref="PosixSignal"/>.
/// </para>
/// </remarks>
internal sealed partial class Launcher
{
    /// <summary>What the multimedia timer calls return when they worked.</summary>
    private const uint TimerNoError = 0;

    /// <summary>
    /// Raises the resolution of the Windows timers, and says so, the way the C
    /// tool does right after it has built the pipeline.
    /// </summary>
    /// <returns>
    /// The resolution that was set, which <see cref="ClearTimerResolution"/>
    /// gives back, or <c>0</c> when nothing was set.
    /// </returns>
    /// <remarks>
    /// It improves the accuracy of <c>Sleep</c> and with it of the system clock
    /// of GStreamer. Every <c>timeBeginPeriod</c> has to be undone by a
    /// <c>timeEndPeriod</c>; before Windows 10 version 2004 it was a global
    /// setting that affected other processes as well.
    /// </remarks>
    private uint EnableTimerResolution() =>
        OperatingSystem.IsWindows() ? EnableWindowsTimerResolution() : 0;

    /// <summary>
    /// Gives the timer resolution back.
    /// </summary>
    /// <param name="resolution">What <see cref="EnableTimerResolution"/> returned.</param>
    private static void ClearTimerResolution(uint resolution)
    {
        if (resolution != 0 && OperatingSystem.IsWindows())
        {
            TimeEndPeriod(resolution);
        }
    }

    /// <summary>
    /// Installs the handler for the signal a developer sends to look at a stuck
    /// pipeline, unless <c>-f</c> asked for none.
    /// </summary>
    /// <param name="options">The command line of the process.</param>
    /// <returns>
    /// The registration, which has to be disposed, or <see langword="null"/>
    /// where there is nothing to install.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The C tool installs <c>sigaction</c> handlers for SIGSEGV and SIGQUIT on
    /// POSIX except Apple, and both of them run <c>fault_spin</c>: restore the
    /// default handlers, print a stack trace, and then loop forever so that a
    /// debugger can attach to a process that is standing still.
    /// </para>
    /// <para>
    /// <b>Only the SIGQUIT half is ported, and it does not spin.</b> A SIGSEGV
    /// handler that runs managed code is not memory safe — the runtime is the
    /// one thing that knows whether the fault was its own write barrier — and
    /// .NET already owns that path, from <c>createdump</c> to the crash report,
    /// so this never installs one. Spinning is not portable either: a POSIX
    /// handler stops the thread that was signalled, while a managed handler
    /// runs on a thread of its own and stopping it would leave the pipeline
    /// running. What is left is the useful half — saying that the signal
    /// arrived, and where to attach.
    /// </para>
    /// </remarks>
    private static IDisposable? InstallFaultHandler(Options options)
    {
        // The C guard is G_OS_UNIX && !__APPLE__.
        if (options.NoFault || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return null;
        }

        return PosixSignalRegistration.Create(PosixSignal.SIGQUIT, context => OnQuitSignal(context, options));
    }

    /// <summary>
    /// Installs the handler that dumps a snapshot of the graph, on the systems
    /// that have the signal.
    /// </summary>
    /// <returns>
    /// The registration, which has to be disposed, or <see langword="null"/> on
    /// Windows.
    /// </returns>
    /// <remarks>
    /// SIGHUP maps onto a console close event on Windows, which is a different
    /// thing entirely and would take the process down in the middle of a dump,
    /// so the guard follows the C one — <c>G_OS_UNIX</c> — rather than what the
    /// runtime would allow.
    /// </remarks>
    private IDisposable? InstallHangUpHandler() =>
        OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnHangUp);

    /// <summary>
    /// Writes a snapshot of the graph, and keeps the run going.
    /// </summary>
    /// <param name="context">The signal, and whether it ends the process.</param>
    private void OnHangUp(PosixSignalContext context)
    {
        // The C handler returns G_SOURCE_CONTINUE: the signal neither ends the
        // run nor takes the handler off.
        context.Cancel = true;

        Print(Environment.GetEnvironmentVariable("GST_DEBUG_DUMP_DOT_DIR") is null
            ? "SIGHUP: not dumping dot file snapshot, GST_DEBUG_DUMP_DOT_DIR environment variable not set."
            : "SIGHUP: dumping dot file snapshot ...");

        // Writing the graph is thread safe, which is what lets this run from
        // the thread the runtime dispatches the signal on.
        Global.DebugBinToDotFileWithTs(_pipeline, DebugGraphDetails.All, "gst-launch.snapshot");
    }

    /// <summary>
    /// Reports the signal and where to attach a debugger, and keeps the process
    /// alive.
    /// </summary>
    /// <param name="context">The signal, and whether it ends the process.</param>
    /// <param name="options">The command line of the process.</param>
    private static void OnQuitSignal(PosixSignalContext context, Options options)
    {
        // Without this the default action takes the process down with a core
        // dump, which is the one thing the C handler exists to prevent.
        context.Cancel = true;

        if (!options.Quiet)
        {
            Console.Out.WriteLine("Caught SIGQUIT");
        }

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Options.ProgramName}: the C tool spins here so that a debugger can attach. A managed " +
            $"runtime cannot stop its own threads from a signal handler, so the run continues; " +
            $"attach to process {Environment.ProcessId} instead."));
    }

    /// <summary>
    /// Asks Windows for the finest timer period it has and takes it.
    /// </summary>
    /// <returns>The period that was set, or <c>0</c>.</returns>
    [SupportedOSPlatform("windows")]
    private uint EnableWindowsTimerResolution()
    {
        if (TimeGetDevCaps(out TimeCaps capabilities, (uint)Unsafe.SizeOf<TimeCaps>()) != TimerNoError)
        {
            Console.Error.WriteLine($"{Options.ProgramName}: timeGetDevCaps() returned a non zero code");
            return 0;
        }

        uint resolution = Math.Min(Math.Max(capabilities.PeriodMinimum, 1), capabilities.PeriodMaximum);

        if (TimeBeginPeriod(resolution) != TimerNoError)
        {
            Console.Error.WriteLine($"{Options.ProgramName}: timeBeginPeriod() returned a non zero code");
            return 0;
        }

        Print(string.Create(
            CultureInfo.InvariantCulture,
            $"Use Windows high-resolution clock, precision: {resolution} ms"));

        return resolution;
    }

    /// <summary>The <c>timeGetDevCaps</c> entry point.</summary>
    /// <param name="capabilities">Receives the range of periods.</param>
    /// <param name="size">The size of <paramref name="capabilities"/>.</param>
    /// <returns><c>0</c> when it worked.</returns>
    [SupportedOSPlatform("windows")]
    [LibraryImport("winmm.dll", EntryPoint = "timeGetDevCaps")]
    private static partial uint TimeGetDevCaps(out TimeCaps capabilities, uint size);

    /// <summary>The <c>timeBeginPeriod</c> entry point.</summary>
    /// <param name="period">The period to ask for, in milliseconds.</param>
    /// <returns><c>0</c> when it worked.</returns>
    [SupportedOSPlatform("windows")]
    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint period);

    /// <summary>The <c>timeEndPeriod</c> entry point.</summary>
    /// <param name="period">The period that was asked for.</param>
    /// <returns><c>0</c> when it worked.</returns>
    [SupportedOSPlatform("windows")]
    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint period);

    /// <summary>The <c>TIMECAPS</c> structure of the Windows multimedia API.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TimeCaps
    {
        /// <summary>The smallest period the system supports, in milliseconds.</summary>
        internal uint PeriodMinimum;

        /// <summary>The largest period the system supports, in milliseconds.</summary>
        internal uint PeriodMaximum;
    }
}
