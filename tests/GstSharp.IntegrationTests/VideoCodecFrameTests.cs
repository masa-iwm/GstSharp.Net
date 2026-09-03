using System.Runtime.ExceptionServices;
using Gst;
using Gst.Video;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The user data slot of a <c>GstVideoCodecFrame</c>, measured on a frame that
/// a real decoder produced.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_video_codec_frame_set_user_data</c> is the third member of the family
/// that pairs a pointer with a <c>GDestroyNotify</c>, and the only one whose
/// notification also runs <em>synchronously</em>: writing the slot again
/// releases what was in it before the call returns. That is what these tests
/// pin, together with the other half of the contract — the notification of the
/// value that is left in the slot runs when the frame is released, and not
/// before.
/// </para>
/// <para>
/// Getting hold of a frame is the whole difficulty. <see cref="VideoDecoder"/>
/// is abstract and managed subclassing of it is not shipped, so the frame has
/// to be borrowed from an element that is decoding, and it cannot be borrowed
/// from a buffer probe: both <c>gst_video_decoder_finish_frame</c> (1.28.6
/// gstvideodecoder.c:3582 releases, :3590 pushes; 1.24.0 :3530 and :3538) and
/// <c>gst_video_encoder_finish_frame</c> release the frame <em>before</em> they
/// push the buffer, so by the time a src pad probe runs the frame is gone from
/// <c>priv-&gt;frames</c> and <c>gst_video_decoder_get_oldest_frame</c> answers
/// nothing.
/// </para>
/// <para>
/// The CAPS event is the opening. <c>gst_video_decoder_decode_frame</c> pushes
/// the frame onto <c>priv-&gt;frames</c> (1.28.6 gstvideodecoder.c:4037, 1.24.0
/// :3981) <em>before</em> it calls the subclass <c>handle_frame</c>, and
/// <c>theoradec</c> negotiates from inside <c>handle_frame</c> while it parses
/// the Theora headers — through <c>theora_handle_type_packet</c> when the
/// headers arrive as buffers, and through <c>theoradec_handle_header_caps</c>
/// when they only arrive in the caps, but from inside <c>handle_frame</c>
/// either way. <c>gst_video_decoder_negotiate_default</c> ends in
/// <c>gst_pad_set_caps</c> (1.28.6 :4569, 1.24.0 :4495), which pushes the CAPS
/// event through the downstream event probes of the src pad synchronously, on
/// the streaming thread, while that frame is still in the list.
/// </para>
/// <para>
/// So the probe below reacts to CAPS rather than to a buffer, and what it takes
/// is a reference of its own (<c>gst_video_decoder_get_oldest_frame</c> is
/// <c>transfer full</c>). The decoder lets go of the frame right after — it
/// drops a header frame, it finishes a data one — and the reference the test
/// holds is what keeps it alive, which is exactly the state the notification
/// contract is about: nothing runs until the test disposes the wrapper.
/// </para>
/// <para>
/// Reading the frame after the run is therefore reading the structure the
/// decoder was working on, not a copy: the frame number the probe recorded is
/// still there, while the timestamp is not, because the decoder rewrites an
/// unset PTS as it lets the frame go. See the comment on that assertion.
/// </para>
/// <para>
/// Everything the probe asserts is captured and re-raised on the test thread.
/// The trampoline of a pad probe answers an exception with
/// <see cref="PadProbeReturn.Drop"/>, so an assertion left to it would swallow
/// the CAPS event and turn a readable failure into a pipeline error.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class VideoCodecFrameTests
{
    /// <summary>The pipeline every test of this class runs.</summary>
    private const string Description =
        "videotestsrc num-buffers=5 ! theoraenc ! theoradec name=dec ! fakesink sync=false";

    /// <summary>How long a run has to reach the end of its stream.</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>The frame the probe took a reference of, owned by the test.</summary>
    private VideoCodecFrame? _frame;

    /// <summary>What the probe saw fail, re-raised on the test thread.</summary>
    private Exception? _failure;

    /// <summary>Whether the probe reached the first CAPS event.</summary>
    private int _probeRan;

    /// <summary>How often the notification written first has run.</summary>
    private int _firstRuns;

    /// <summary>How often the notification written second has run.</summary>
    private int _secondRuns;

    /// <summary>The frame number the probe read, for the comparison after the run.</summary>
    private uint _systemFrameNumber;

    /// <summary>The timestamp the probe read, for the comparison after the run.</summary>
    private ClockTime _pts;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public VideoCodecFrameTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Writing the slot a second time releases the state of the first write
    /// before the call returns, and the state of the second write survives the
    /// decoder, the pipeline and the run — until the wrapper that holds the
    /// last reference of the frame is disposed.
    /// </summary>
    [RequiresElementFact("videotestsrc", "theoraenc", "theoradec", "fakesink")]
    public void ReplacingTheUserDataReleasesWhatItReplacedAndDisposingReleasesTheRest()
    {
        RunThroughTheDecoder(decoder =>
        {
            VideoCodecFrame frame = Assert.IsType<VideoCodecFrame>(decoder.GetOldestFrame());

            _systemFrameNumber = frame.SystemFrameNumber;
            _pts = frame.Pts;

            // Neither GstVideoDecoder nor theoradec ever writes the slot, so a
            // frame that reaches a probe carries nothing.
            Assert.Equal(nint.Zero, frame.GetUserData());

            frame.SetUserData(() => Interlocked.Increment(ref _firstRuns));

            // The token is the binding's own handle. Comparing it against zero
            // is all it is for; it is never dereferenced.
            Assert.NotEqual(nint.Zero, frame.GetUserData());
            Assert.Equal(0, Volatile.Read(ref _firstRuns));

            frame.SetUserData(() => Interlocked.Increment(ref _secondRuns));

            // Synchronous replacement: the first notification has already run
            // when the second write returns, on this thread, and the second one
            // has not run at all.
            Assert.Equal(1, Volatile.Read(ref _firstRuns));
            Assert.Equal(0, Volatile.Read(ref _secondRuns));
            Assert.NotEqual(nint.Zero, frame.GetUserData());

            _frame = frame;
        });

        Assert.NotNull(_frame);

        _output.WriteLine(FormattableString.Invariant(
            $"the CAPS probe borrowed frame {_systemFrameNumber} at {_pts.Nanoseconds} ns"));

        // The decoder released the frame long ago and the pipeline is gone, but
        // the wrapper holds the last reference, so nothing has run it yet.
        Assert.Equal(1, Volatile.Read(ref _firstRuns));
        Assert.Equal(0, Volatile.Read(ref _secondRuns));

        // The same reference is what still reads the live structure, and the
        // frame number is the field that identifies it. The timestamp is not
        // compared: it is one of the fields the decoder writes on its way out.
        // gst_video_decoder_drop_frame (1.28.6 gstvideodecoder.c:3284, 1.24.0
        // :3232) calls gst_video_decoder_prepare_finish_frame (:3294 / :3242),
        // which fills an unset PTS in with the start of the output segment
        // (:3170 / :3118) — which is exactly why the structure this reads has
        // to be the live one rather than a copy taken in the probe.
        Assert.Equal(_systemFrameNumber, _frame.SystemFrameNumber);

        _output.WriteLine(FormattableString.Invariant(
            $"the decoder left it at {_frame.Pts.Nanoseconds} ns"));

        _frame.Dispose();

        // Releasing the last reference runs it, once, on this thread.
        Assert.Equal(1, Volatile.Read(ref _secondRuns));
        Assert.Equal(1, Volatile.Read(ref _firstRuns));
    }

    /// <summary>
    /// Clearing the slot is the same release on the calling thread, and it
    /// leaves nothing behind: the token reads zero again and disposing the
    /// frame runs nothing further.
    /// </summary>
    [RequiresElementFact("videotestsrc", "theoraenc", "theoradec", "fakesink")]
    public void ClearingTheUserDataReleasesItAndEmptiesTheSlot()
    {
        RunThroughTheDecoder(decoder =>
        {
            VideoCodecFrame frame = Assert.IsType<VideoCodecFrame>(decoder.GetOldestFrame());

            frame.SetUserData(() => Interlocked.Increment(ref _firstRuns));
            Assert.NotEqual(nint.Zero, frame.GetUserData());
            Assert.Equal(0, Volatile.Read(ref _firstRuns));

            frame.SetUserData(null);

            Assert.Equal(1, Volatile.Read(ref _firstRuns));
            Assert.Equal(nint.Zero, frame.GetUserData());

            _frame = frame;
        });

        Assert.NotNull(_frame);
        Assert.Equal(1, Volatile.Read(ref _firstRuns));
        Assert.Equal(nint.Zero, _frame.GetUserData());

        _frame.Dispose();

        Assert.Equal(1, Volatile.Read(ref _firstRuns));
    }

    /// <summary>
    /// The buffers a codec frame names and the caps its output state carries,
    /// read where the C contract puts them: inside the call the decoder made,
    /// on its streaming thread, under the stream lock it holds.
    /// </summary>
    /// <remarks>
    /// The negotiation the probe rides on is the moment the output state exists
    /// and the frame is still on the decoder's list, so it is the one place a
    /// read of either is inside the window. The two HDR pointers are the
    /// nullable path of the copy: a Theora stream carries no HDR metadata, and
    /// <see langword="null"/> is what says so.
    /// </remarks>
    [RequiresElementFact("videotestsrc", "theoraenc", "theoradec", "fakesink")]
    public void AFrameNamesItsInputBufferAndTheOutputStateItsCaps()
    {
        RunThroughTheDecoder(decoder =>
        {
            using VideoCodecFrame frame = Assert.IsType<VideoCodecFrame>(decoder.GetOldestFrame());

            // The decoder assigned the input before it handed the frame to the
            // subclass, and the subclass has produced no output yet.
            using Gst.Buffer? input = frame.GetInputBuffer();
            Assert.NotNull(input);
            Assert.True(input.GetSize() > 0);

            Assert.Null(frame.GetOutputBuffer());

            using VideoCodecState state = Assert.IsType<VideoCodecState>(decoder.GetOutputState());

            using Caps? caps = state.GetCaps();
            Assert.NotNull(caps);
            using Structure structure = caps.GetStructure(0);
            Assert.Equal("video/x-raw", structure.GetName());

            // The negotiation this probe rides on fills the allocation caps in
            // before it sets the caps on the pad that raises the event
            // (gstvideodecoder.c:4533 precedes :4569), so by here they are set.
            using Caps? allocation = state.GetAllocationCaps();
            Assert.NotNull(allocation);

            // No HDR metadata on this stream: the copy answers null rather than
            // a zeroed structure.
            Assert.Null(state.ContentLightLevel);
            Assert.Null(state.MasteringDisplayInfo);
        });
    }

    /// <summary>
    /// Runs the pipeline to the end of its stream and calls
    /// <paramref name="onFirstCaps"/> from the downstream event probe of the
    /// decoder's src pad, on the first CAPS event.
    /// </summary>
    /// <param name="onFirstCaps">What to measure on the streaming thread.</param>
    /// <remarks>
    /// The probe removes itself after that one call, everything it raises is
    /// captured, and the capture is re-raised here — with its stack — once the
    /// pipeline has stopped.
    /// </remarks>
    private void RunThroughTheDecoder(Action<VideoDecoder> onFirstCaps)
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(Description));
        using Element? named = pipeline.GetByName("dec");
        VideoDecoder decoder = Assert.IsAssignableFrom<VideoDecoder>(named);
        using Pad source = Assert.IsAssignableFrom<Pad>(decoder.GetStaticPad("src"));
        using Bus bus = pipeline.GetBus();

        _ = source.AddProbe(PadProbeType.EventDownstream, (_, info) =>
        {
            using Event? probed = info.GetEvent();

            if (probed?.Type != EventType.Caps || Interlocked.Exchange(ref _probeRan, 1) != 0)
            {
                return PadProbeReturn.Ok;
            }

            try
            {
                onFirstCaps(decoder);
            }
            catch (Exception exception)
            {
                _failure = exception;
            }

            return PadProbeReturn.Remove;
        });

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, RunTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }

        if (Volatile.Read(ref _failure) is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        // A probe that never ran would leave every assertion below trivially
        // true, so the run has to prove it reached the frame.
        Assert.Equal(1, Volatile.Read(ref _probeRan));
    }
}
