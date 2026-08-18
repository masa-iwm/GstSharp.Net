using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Gst.Interop;

namespace Gst;

/// <summary>
/// The entry point of a program that <see cref="Global.MacosMain"/> runs.
/// </summary>
/// <param name="args">
/// The command line, decoded from the native <c>argv</c> the callback was
/// handed. It is the array passed to <see cref="Global.MacosMain"/>, element for
/// element.
/// </param>
/// <returns>The exit code of the program.</returns>
/// <remarks>
/// This is <c>GstMainFunc</c>. It runs on a thread GStreamer creates for it and
/// never on the thread that called <see cref="Global.MacosMain"/>, which is
/// busy running the Cocoa event loop for as long as this call lasts.
/// </remarks>
public delegate int MainFunc(string[] args);

/// <summary>
/// The entry point of a program that <see cref="Global.MacosMainSimple"/> runs.
/// </summary>
/// <returns>The exit code of the program.</returns>
/// <remarks>
/// This is <c>GstMainFuncSimple</c>, the variant upstream added for bindings: a
/// language that already carries its command line around as an object of its own
/// gains nothing from a C <c>argv</c> being built for it and taken apart again.
/// The threading contract is the one of <see cref="MainFunc"/>.
/// </remarks>
public delegate int MainFuncSimple();

/// <summary>The native entry point of <see cref="Gst.MainFunc"/>.</summary>
internal static unsafe class MainFuncTrampoline
{
    /// <summary>Gets the address that is handed to native code.</summary>
    internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<int, byte**, nint, int>)&Invoke;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Invoke(int argc, byte** argv, nint userData)
    {
        try
        {
            if (Gst.Interop.CallbackHandle.GetState<Gst.MainFunc>(userData) is not { } callback)
            {
                return Gst.Global.MainFuncFailureExitCode;
            }

            // The strings are read back out of the vector that native code
            // passed, not out of the array the caller handed in: whatever the
            // library did to argv on the way is what the program has to see.
            string[] args = argc <= 0 || argv is null ? [] : new string[argc];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = Gst.Interop.GMarshal.PtrToStringUtf8((nint)argv[i]) ?? string.Empty;
            }

            return callback(args);
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return Gst.Global.MainFuncFailureExitCode;
        }
    }
}

/// <summary>The native entry point of <see cref="Gst.MainFuncSimple"/>.</summary>
internal static unsafe class MainFuncSimpleTrampoline
{
    /// <summary>Gets the address that is handed to native code.</summary>
    internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<nint, int>)&Invoke;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Invoke(nint userData)
    {
        try
        {
            if (Gst.Interop.CallbackHandle.GetState<Gst.MainFuncSimple>(userData) is not { } callback)
            {
                return Gst.Global.MainFuncFailureExitCode;
            }

            return callback();
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return Gst.Global.MainFuncFailureExitCode;
        }
    }
}

/// <content>
/// The macOS Cocoa entry points of <c>libgstreamer-1.0</c>, in the class the
/// generator would have emitted them into.
/// </content>
/// <remarks>
/// <para>
/// <c>gstmacos.m</c> is added to the sources of <c>libgstreamer-1.0</c> only
/// where the host system is Darwin and the subsystem is macOS, so a macOS build
/// exports <c>gst_macos_main</c> and <c>gst_macos_main_simple</c> and no other
/// build does — a Windows or Linux library has no stub, no pass-through and no
/// symbol at all. The family is likewise in none of the reference girs, which
/// were produced on a platform that is not macOS, so there is nothing for the
/// generator to emit from and the two functions are written by hand here. See
/// <c>girs/README.md</c>.
/// </para>
/// <para>
/// Both are marked <see cref="SupportedOSPlatformAttribute"/> for
/// <c>macos</c>, which is what makes a caller guard the call with
/// <see cref="OperatingSystem.IsMacOS"/> rather than discover the missing symbol
/// at run time. The guarded shape is the port of the <c>#ifdef</c> the C tools
/// carry around their own <c>main</c>.
/// </para>
/// </remarks>
public static unsafe partial class Global
{
    /// <summary>
    /// The exit code a program is reported to have ended with when its callback
    /// threw instead of returning one.
    /// </summary>
    /// <remarks>
    /// The generated trampolines answer a failed callback with
    /// <c>default</c>, because the answer they carry is a "keep going" flag
    /// whose safe value is <see langword="false"/>. Here the value is the exit
    /// code of a program, whose safe value is not zero: a <c>main</c> that threw
    /// did not succeed, and reporting success would hide the failure from
    /// everything that reads an exit code.
    /// </remarks>
    internal const int MainFuncFailureExitCode = 1;

    /// <summary>
    /// Runs the Cocoa event loop on the calling thread and the program on a
    /// thread of its own.
    /// </summary>
    /// <param name="mainFunc">The program to run.</param>
    /// <param name="args">
    /// The command line to hand it. It becomes the native <c>argv</c> the
    /// callback is passed, and reaches the callback unchanged.
    /// </param>
    /// <returns>The value <paramref name="mainFunc"/> returned.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_macos_main</c>. It starts an <c>NSApplication</c>, hands
    /// <paramref name="mainFunc"/> to a thread GStreamer creates, and runs the
    /// Cocoa event loop on the calling thread until that thread finishes; the
    /// loop is then stopped, the thread joined, and the value the callback
    /// returned is returned from here. The call blocks for exactly as long as
    /// the program runs and never ends the process itself, so a <c>Main</c>
    /// that returns what this returns behaves the way it would without the
    /// wrapper.
    /// </para>
    /// <para>
    /// <b>Call it from the process main thread.</b> AppKit runs its event loop
    /// on that thread and on no other, and leaving that thread free for it is
    /// the entire point of the arrangement. A call from a thread pool thread —
    /// from a test, or from anything <c>async</c> resumed — is not the
    /// supported use and has no defined behaviour.
    /// </para>
    /// <para>
    /// <b>This is what a video window on macOS needs.</b> A sink that opens one,
    /// <c>autovideosink</c> and <c>glimagesink</c> among them, needs a Cocoa
    /// event loop to be running on the main thread before it can create its
    /// window; without one it does not come up. A console program that opens no
    /// window does not need the wrapper, which is why the C tools that have no
    /// window still use it — it costs them nothing — and why the ports of those
    /// tools in <c>samples/</c> do not.
    /// </para>
    /// <para>
    /// <b>Initialise inside the callback.</b> <c>gst_macos_main</c> does not
    /// call <c>gst_init</c>: it is a wrapper around a <c>main</c>, and the
    /// <c>main</c> is what initialises. Call
    /// <see cref="GstSharp.Initialize(GstSharpOptions?)"/> from
    /// <paramref name="mainFunc"/>, which is what the C tools do, and pass
    /// <paramref name="args"/> on through
    /// <see cref="GstSharpOptions.InitArgs"/> if the command line is to be
    /// parsed — that property prepends the program name a GLib option parser
    /// expects, so <paramref name="args"/> stays the plain command line of a
    /// .NET <c>Main</c> from end to end.
    /// </para>
    /// <para>
    /// <b>Once per process.</b> The upstream documentation is explicit that a
    /// second call, especially one made while the first is still running, is
    /// unpredictable and most likely fails outright. There is one main thread
    /// and one <c>NSApplication</c>, and this claims both.
    /// </para>
    /// <para>
    /// An exception that leaves <paramref name="mainFunc"/> would have to unwind
    /// through the Cocoa stack frame that called it, so it is caught on the
    /// boundary, reported to <see cref="ExceptionTrap"/>, and answered with the
    /// exit code 1. Catch what the program should report on itself.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mainFunc"/> or <paramref name="args"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="args"/> holds a <see langword="null"/> or a string with an
    /// embedded null character. A C <c>argv</c> has neither before its
    /// <c>argc</c> entry.
    /// </exception>
    /// <exception cref="EntryPointNotFoundException">
    /// The loaded GStreamer is not a macOS build and exports no
    /// <c>gst_macos_main</c>.
    /// </exception>
    [SupportedOSPlatform("macos")]
    public static int MacosMain(MainFunc mainFunc, string[] args)
    {
        ArgumentNullException.ThrowIfNull(mainFunc);
        ArgumentNullException.ThrowIfNull(args);

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is null)
            {
                throw new ArgumentException(
                    FormattableString.Invariant(
                        $"A command line has no null entries, and args[{i}] is null."),
                    nameof(args));
            }
        }

        Gst.Interop.CallbackHandle mainFuncState = Gst.Interop.CallbackHandle.Alloc(mainFunc);
        nint[] owned = new nint[args.Length];
        byte** argv = null;

        try
        {
            // One entry per argument plus the terminating null a C argv carries,
            // which nothing here reads back but a callee written in C may.
            argv = (byte**)NativeMemory.AllocZeroed((nuint)args.Length + 1, (nuint)sizeof(byte*));

            for (int i = 0; i < args.Length; i++)
            {
                owned[i] = Gst.Interop.GMarshal.StringToUtf8Ptr(args[i]);
                argv[i] = (byte*)owned[i];
            }

            return GstMacosMain(MainFuncTrampoline.Pointer, args.Length, argv, mainFuncState.UserData);
        }
        finally
        {
            // The strings are released from the copy that was made above rather
            // than from the vector, for the reason GstSharp.Initialize releases
            // them that way: a callee that parses the command line removes the
            // entries it understood from the vector in place, without freeing
            // them.
            foreach (nint argument in owned)
            {
                Gst.Interop.GMarshal.Free(argument);
            }

            NativeMemory.Free(argv);

            // The callback is scope-call: gst_macos_main has returned, so the
            // thread that could have invoked it has been joined.
            mainFuncState.Free();
        }
    }

    /// <summary>
    /// Runs the Cocoa event loop on the calling thread and the program on a
    /// thread of its own, without a command line.
    /// </summary>
    /// <param name="mainFunc">The program to run.</param>
    /// <returns>The value <paramref name="mainFunc"/> returned.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_macos_main_simple</c>, which upstream added for bindings.
    /// It is <see cref="MacosMain"/> with an empty <c>argv</c>, and everything
    /// documented there — the main thread, the second thread the program runs
    /// on, when the call returns, that it may be made once — holds here
    /// unchanged.
    /// </para>
    /// <para>
    /// Prefer this one. A .NET program has its command line in
    /// <see cref="Environment.GetCommandLineArgs"/> and in the argument of its
    /// own <c>Main</c>, both of which the callback closes over, so building a
    /// native <c>argv</c> for it to be decoded again buys nothing.
    /// <see cref="MacosMain"/> is there for the case where the command line has
    /// to reach a callee that reads the vector itself.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mainFunc"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="EntryPointNotFoundException">
    /// The loaded GStreamer is not a macOS build and exports no
    /// <c>gst_macos_main_simple</c>.
    /// </exception>
    [SupportedOSPlatform("macos")]
    public static int MacosMainSimple(MainFuncSimple mainFunc)
    {
        ArgumentNullException.ThrowIfNull(mainFunc);

        Gst.Interop.CallbackHandle mainFuncState = Gst.Interop.CallbackHandle.Alloc(mainFunc);

        try
        {
            return GstMacosMainSimple(MainFuncSimpleTrampoline.Pointer, mainFuncState.UserData);
        }
        finally
        {
            mainFuncState.Free();
        }
    }

    /// <summary>The <c>gst_macos_main</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_macos_main")]
    private static partial int GstMacosMain(nint mainFunc, int argc, byte** argv, nint userData);

    /// <summary>The <c>gst_macos_main_simple</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_macos_main_simple")]
    private static partial int GstMacosMainSimple(nint mainFunc, nint userData);
}
