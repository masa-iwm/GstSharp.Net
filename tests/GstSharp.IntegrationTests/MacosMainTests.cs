using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Gst;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Covers <see cref="Global.MacosMain"/> and
/// <see cref="Global.MacosMainSimple"/>: the wrapper that runs the Cocoa event
/// loop on the main thread while the program runs on a thread of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here runs a Cocoa event loop, on macOS or anywhere else.</b> The
/// call needs the process main thread and returns only when the program it was
/// given returns; xunit hands a test neither, so a test that really called
/// <c>gst_macos_main</c> would run the rest of the assembly inside an
/// <c>NSApplication</c> started on a worker thread. What is left is everything
/// that can be decided without the loop: the argument checks, which run before
/// the entry point is ever looked up, the decoding half of the command line,
/// which is reachable through the trampoline on its own, and the platform the
/// two symbols exist on at all.
/// </para>
/// <para>
/// That last one is the fact the whole design rests on, which is why it is
/// asserted rather than assumed: <c>gstmacos.m</c> is compiled only where the
/// host system is Darwin and the subsystem is macOS, so there is no
/// pass-through to fall back on anywhere else.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
[SuppressMessage(
    "Interoperability",
    "CA1416:Validate platform compatibility",
    Justification = "The calls made here never reach the macOS-only entry point. The argument " +
        "checks throw before the interop stub is entered, and the one call that does enter it is " +
        "made only where the export is absent, which is the behaviour being asserted.")]
public sealed class MacosMainTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public MacosMainTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The two functions exist in a macOS build of GStreamer and in no other.
    /// </summary>
    [Fact]
    public void TheFamilyIsExportedByAMacOsBuildAndByNoOther()
    {
        nint library = NativeLoader.Load("Gst");

        bool main = NativeLibrary.TryGetExport(library, "gst_macos_main", out _);
        bool simple = NativeLibrary.TryGetExport(library, "gst_macos_main_simple", out _);

        _output.WriteLine(
            $"gst_macos_main: {main}, gst_macos_main_simple: {simple}, macOS: {OperatingSystem.IsMacOS()}");

        Assert.Equal(OperatingSystem.IsMacOS(), main);
        Assert.Equal(OperatingSystem.IsMacOS(), simple);
    }

    /// <summary>
    /// Where the library exports nothing of the family, the call says which
    /// entry point it could not find rather than failing some other way.
    /// </summary>
    /// <remarks>
    /// The command line is a real one, so the vector is built and released
    /// around a call that never happens — which is the half of
    /// <see cref="Global.MacosMain"/> a machine without the symbol can still
    /// exercise.
    /// </remarks>
    [Fact]
    public void TheCallNamesTheMissingEntryPointWhereTheFamilyIsNotExported()
    {
        if (NativeLibrary.TryGetExport(NativeLoader.Load("Gst"), "gst_macos_main", out _))
        {
            // A build that exports it is a macOS build, and calling it from
            // here is what the remarks of this class rule out.
            Assert.True(OperatingSystem.IsMacOS());
            return;
        }

        EntryPointNotFoundException failure = Assert.Throws<EntryPointNotFoundException>(
            () => Global.MacosMain(_ => 0, ["--gst-debug-level=1", "file.mp4"]));

        _output.WriteLine(failure.Message);
    }

    /// <summary>
    /// The command line reaches the program as the native vector carried it:
    /// the same strings, in the same order, and no more of them than
    /// <c>argc</c> promised.
    /// </summary>
    /// <remarks>
    /// The trampoline is entered directly, with a vector built the way
    /// <see cref="Global.MacosMain"/> builds one, because that is the only way
    /// to see the decoding half without a Cocoa event loop. The strings are
    /// chosen to be the ones a naive round trip loses: one holds a space, one is
    /// empty, and one leaves the ASCII range.
    /// </remarks>
    [Fact]
    public unsafe void TheCommandLineReachesTheProgramUnchanged()
    {
        string[] given = ["gstsharp", "--uri=file:///a b.mp4", string.Empty, "café"];

        string[]? seen = null;
        MainFunc program = args =>
        {
            seen = args;
            return 42;
        };

        CallbackHandle state = CallbackHandle.Alloc(program);
        nint[] owned = new nint[given.Length];
        byte** argv = (byte**)NativeMemory.AllocZeroed((nuint)given.Length + 1, (nuint)sizeof(byte*));

        try
        {
            for (int i = 0; i < given.Length; i++)
            {
                owned[i] = GMarshal.StringToUtf8Ptr(given[i]);
                argv[i] = (byte*)owned[i];
            }

            var entry = (delegate* unmanaged[Cdecl]<int, byte**, nint, int>)MainFuncTrampoline.Pointer;
            int result = entry(given.Length, argv, state.UserData);

            Assert.Equal(42, result);
            Assert.NotNull(seen);
            Assert.Equal(given, seen);
        }
        finally
        {
            foreach (nint argument in owned)
            {
                GMarshal.Free(argument);
            }

            NativeMemory.Free(argv);
            state.Free();
        }
    }

    /// <summary>
    /// An empty command line arrives as an empty array, not as a null one.
    /// </summary>
    /// <remarks>
    /// This is the vector <c>gst_macos_main_simple</c> passes on — it sets
    /// <c>argc</c> to zero and <c>argv</c> to <c>NULL</c> — so the same
    /// trampoline has to survive it.
    /// </remarks>
    [Fact]
    public unsafe void AnEmptyCommandLineArrivesAsAnEmptyArray()
    {
        string[]? seen = null;
        MainFunc program = args =>
        {
            seen = args;
            return 0;
        };

        CallbackHandle state = CallbackHandle.Alloc(program);

        try
        {
            var entry = (delegate* unmanaged[Cdecl]<int, byte**, nint, int>)MainFuncTrampoline.Pointer;

            Assert.Equal(0, entry(0, null, state.UserData));
            Assert.NotNull(seen);
            Assert.Empty(seen);
        }
        finally
        {
            state.Free();
        }
    }

    /// <summary>
    /// A program that throws is reported instead of unwinding into Cocoa, and
    /// the process is told that it failed rather than that it succeeded.
    /// </summary>
    [Fact]
    public unsafe void AThrowingProgramIsReportedAndCountsAsAFailure()
    {
        MainFuncSimple program = () => throw new InvalidTimeZoneException("the program gave up");

        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        CallbackHandle state = CallbackHandle.Alloc(program);
        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            var entry = (delegate* unmanaged[Cdecl]<nint, int>)MainFuncSimpleTrampoline.Pointer;

            // Zero would say the program succeeded, which is the one answer a
            // program that threw must not give.
            Assert.NotEqual(0, entry(state.UserData));
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
            state.Free();
        }

        lock (failures)
        {
            Exception reported = Assert.Single(failures);
            Assert.IsType<InvalidTimeZoneException>(reported);
        }
    }

    /// <summary>There is no program to run without a callback.</summary>
    [Fact]
    public void ANullProgramIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Global.MacosMain(null!, []));
        Assert.Throws<ArgumentNullException>(() => Global.MacosMainSimple(null!));
    }

    /// <summary>
    /// A missing command line is a mistake, not an empty one:
    /// <see cref="Global.MacosMainSimple"/> is the call that has none.
    /// </summary>
    [Fact]
    public void ANullCommandLineIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => Global.MacosMain(_ => 0, null!));

    /// <summary>
    /// A C <c>argv</c> holds no null entry before its <c>argc</c> one, so an
    /// array that does is refused before a program can be handed it.
    /// </summary>
    [Fact]
    public void ANullArgumentIsRejected()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => Global.MacosMain(_ => 0, ["one", null!, "three"]));

        Assert.Equal("args", failure.ParamName);
        _output.WriteLine(failure.Message);
    }
}
