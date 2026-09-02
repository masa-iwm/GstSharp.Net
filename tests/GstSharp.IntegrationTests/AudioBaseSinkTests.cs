using System;
using Gst;
using Gst.Audio;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written custom slaving surface of
/// <see cref="Gst.Audio.AudioBaseSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// The installed callback cannot be read back: C keeps it in the private block
/// of the sink and offers no getter. The trampoline is therefore exercised
/// directly — the tests allocate a <see cref="CallbackHandle"/> of their own
/// with the very call the setter uses and invoke the entry point through its
/// address — and the two public members are exercised for the call they make,
/// which is all that is observable from managed code.
/// </para>
/// <para>
/// Nothing here sets a sink to <c>PLAYING</c>: constructing an audio sink needs
/// no device, opening one does, and the C code path this covers is the
/// marshalling and not the slaving algorithm.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class AudioBaseSinkTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises the fixture.</summary>
    /// <param name="output">Where the name of the sink that was found is written.</param>
    public AudioBaseSinkTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The entry point of the hand written trampoline, typed the way the C
    /// declaration spells it.
    /// </summary>
    private static delegate* unmanaged[Cdecl]<nint, ulong, ulong, long*, int, nint, void> Trampoline =>
        (delegate* unmanaged[Cdecl]<nint, ulong, ulong, long*, int, nint, void>)
            AudioBaseSink.CustomSlavingTrampoline;

    /// <summary>
    /// The handler is handed the two clock times and its return is written
    /// through the pointer the library passes.
    /// </summary>
    [RequiresAnyElementFact("wasapisink", "directsoundsink", "alsasink", "pulsesink", "osxaudiosink")]
    public void TheSlavingTrampolineWritesTheSkewTheHandlerAnswers()
    {
        using AudioBaseSink sink = CreateSink();

        AudioBaseSink? seenSink = null;
        ClockTime seenEtime = default;
        ClockTime seenItime = default;
        AudioBaseSinkDiscontReason seenReason = AudioBaseSinkDiscontReason.NewCaps;

        AudioBaseSinkCustomSlavingHandler handler = (s, etime, itime, reason) =>
        {
            seenSink = s;
            seenEtime = etime;
            seenItime = itime;
            seenReason = reason;
            return 4242;
        };

        CallbackHandle state = CallbackHandle.Alloc(handler);
        try
        {
            long skew = -1;
            Trampoline(sink.Handle, 1000, 900, &skew, 0, state.UserData);

            Assert.Same(sink, seenSink);
            Assert.Equal(1000ul, seenEtime.Nanoseconds);
            Assert.Equal(900ul, seenItime.Nanoseconds);
            Assert.Equal(AudioBaseSinkDiscontReason.NoDiscont, seenReason);
            Assert.Equal(4242, skew);
        }
        finally
        {
            state.Free();
        }
    }

    /// <summary>
    /// The discontinuity call passes no storage for the skew, which the
    /// trampoline has to survive, and both clock times arrive invalid.
    /// </summary>
    [RequiresAnyElementFact("wasapisink", "directsoundsink", "alsasink", "pulsesink", "osxaudiosink")]
    public void TheSlavingTrampolineSurvivesADiscontinuityWithNoSkewStorage()
    {
        using AudioBaseSink sink = CreateSink();

        ClockTime seenEtime = default;
        ClockTime seenItime = default;
        AudioBaseSinkDiscontReason seenReason = AudioBaseSinkDiscontReason.NoDiscont;
        int calls = 0;

        AudioBaseSinkCustomSlavingHandler handler = (s, etime, itime, reason) =>
        {
            calls++;
            seenEtime = etime;
            seenItime = itime;
            seenReason = reason;
            return 7;
        };

        CallbackHandle state = CallbackHandle.Alloc(handler);
        try
        {
            Trampoline(
                sink.Handle,
                ClockTime.None.Nanoseconds,
                ClockTime.None.Nanoseconds,
                null,
                (int)AudioBaseSinkDiscontReason.Alignment,
                state.UserData);

            Assert.Equal(1, calls);
            Assert.Equal(ClockTime.None, seenEtime);
            Assert.Equal(ClockTime.None, seenItime);
            Assert.Equal(AudioBaseSinkDiscontReason.Alignment, seenReason);
        }
        finally
        {
            state.Free();
        }
    }

    /// <summary>
    /// The bridge of the delegate that shipped in 1.28.5 invokes it with a skew
    /// of <c>0</c> and requests none.
    /// </summary>
    [RequiresAnyElementFact("wasapisink", "directsoundsink", "alsasink", "pulsesink", "osxaudiosink")]
    public void TheObsoleteSlavingBridgeRequestsNoSkew()
    {
        using AudioBaseSink sink = CreateSink();

        long seenSkew = -1;
        int calls = 0;

        // The delegate and the adapter are deliberately obsolete: they are the
        // shape that shipped in 1.28.5, which could never request a skew, and
        // item #51 keeps them only so that code compiled against 1.28.5 still
        // builds.
#pragma warning disable CS0618 // Type or member is obsolete
        AudioBaseSinkCustomSlavingCallback callback = (s, etime, itime, requestedSkew, reason) =>
        {
            calls++;
            seenSkew = requestedSkew;
        };

        AudioBaseSinkCustomSlavingHandler handler = AudioBaseSink.Adapt(callback);
#pragma warning restore CS0618

        CallbackHandle state = CallbackHandle.Alloc(handler);
        try
        {
            long skew = -1;
            Trampoline(sink.Handle, 1000, 900, &skew, 0, state.UserData);

            Assert.Equal(1, calls);
            Assert.Equal(0, seenSkew);
            Assert.Equal(0, skew);
        }
        finally
        {
            state.Free();
        }
    }

    /// <summary>
    /// Installing a handler and clearing it again are calls the library
    /// accepts; nothing about them is readable back through the C API.
    /// </summary>
    [RequiresAnyElementFact("wasapisink", "directsoundsink", "alsasink", "pulsesink", "osxaudiosink")]
    public void TheSlavingCallbackIsInstalledAndCleared()
    {
        using AudioBaseSink sink = CreateSink();

        Assert.Throws<ArgumentNullException>(() => sink.SetCustomSlavingCallback(
            (AudioBaseSinkCustomSlavingHandler)null!));

        sink.SetCustomSlavingCallback(static (s, etime, itime, reason) => 0);

        // The handle the install left behind is leaked on purpose: the C setter
        // overwrites the notification along with the callback, so the clear
        // leaves nothing for the disposal below to run.
        sink.ClearCustomSlavingCallback();
    }

    /// <summary>
    /// Builds the first audio sink the installation provides.
    /// </summary>
    /// <returns>The sink.</returns>
    /// <remarks>
    /// <c>GstAudio.Initialize()</c> has to run before the element is built, or
    /// the wrapper is interned against the closest registered type the binding
    /// knows and the cast to <see cref="AudioBaseSink"/> fails.
    /// </remarks>
    private AudioBaseSink CreateSink()
    {
        GstAudio.Initialize();

        foreach (string name in new[]
                 {
                     "wasapisink", "directsoundsink", "alsasink", "pulsesink", "osxaudiosink",
                 })
        {
            if (ElementFactory.Make(name, null) is { } element)
            {
                _output.WriteLine($"sink = {name}");
                return Assert.IsAssignableFrom<AudioBaseSink>(element);
            }
        }

        throw new InvalidOperationException("The gate of this test promised an audio sink and there is none.");
    }
}
