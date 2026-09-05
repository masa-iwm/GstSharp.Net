using Gst;
using Gst.Audio;
using Gst.Base;
using Gst.GObject;
using Gst.Interop;
using Gst.Video;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated subclassing surface of the parser and the four codec base
/// classes: a parser that cuts its input into fixed size frames, an encoder
/// and a decoder per medium, what <c>pre_push</c> does with the buffer it is
/// lent, and the borrow of a boxed value that a trampoline scopes to the call.
/// </summary>
[Collection(GstCollection.Name)]
public sealed unsafe class SubclassCodecTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassCodecTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A managed parser frames what a bounded test source produces: it asks
    /// for more data while it has less than a frame, then writes the buffer it
    /// built and the clip flag through the borrowed frame and finishes it, and
    /// what it finished reaches the sink.
    /// </summary>
    [Fact]
    public void AManagedParserFramesWhatItIsGiven()
    {
        using Pipeline pipeline = Pipeline.New("managed-parser");
        using ProbeParse parser = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("audiotestsrc", "parser-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 40);
        Assert.True(pipeline.AddMany(source, parser, sink));
        Assert.True(source.Link(parser));
        Assert.True(parser.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        _output.WriteLine(
            FormattableString.Invariant($"managed parser: framed={parser.Framed}, fed back={parser.FedBack}, ")
            + FormattableString.Invariant($"rendered={sink.Rendered}, bytes={sink.Bytes}"));

        Assert.True(parser.EveryFrameCarriedABuffer);

        // A frame is larger than one buffer of the source, so the override was
        // called at least once with too little data and wrote a skip size of
        // zero rather than finishing anything.
        Assert.True(parser.FedBack > 0);
        Assert.True(parser.Framed > 0);

        // Everything it finished is what the sink saw, and it saw the buffers
        // the override built rather than the ones the source produced.
        Assert.Equal(parser.Framed, sink.Rendered);
        Assert.Equal((long)parser.Framed * ProbeParse.FrameSize, sink.Bytes);

        // The sink events of the parser were adopted and chained up, so the
        // base class still got the ones it needs.
        Assert.Contains(EventType.StreamStart, parser.Events);
        Assert.Contains(EventType.Caps, parser.Events);
        Assert.Contains(EventType.Segment, parser.Events);
    }

    /// <summary>
    /// A managed audio encoder is given a valid audio info to negotiate with,
    /// answers one buffer per block of samples, and is asked for a drain with
    /// no buffer when the stream ends.
    /// </summary>
    [Fact]
    public void AManagedAudioEncoderAnswersOneBufferPerBlock()
    {
        using Pipeline pipeline = Pipeline.New("managed-audio-encoder");
        using ProbeAudioEncoder encoder = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("audiotestsrc", "encoder-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 10);
        Assert.True(pipeline.AddMany(source, encoder, sink));
        Assert.True(source.Link(encoder));
        Assert.True(encoder.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        _output.WriteLine(
            FormattableString.Invariant($"managed audio encoder: encoded={encoder.Encoded}, drains={encoder.Drains}, ")
            + FormattableString.Invariant($"rate={encoder.Rate}, channels={encoder.Channels}, rendered={sink.Rendered}"));

        // The info the set_format slot was lent was a real one.
        Assert.True(encoder.Rate > 0);
        Assert.True(encoder.Channels > 0);

        Assert.True(encoder.Encoded > 0);
        Assert.Equal(encoder.Encoded, sink.Rendered);

        // The end of the stream reaches handle_frame as a null buffer.
        Assert.True(encoder.Drains > 0);

        Assert.Contains(EventType.Caps, encoder.Events);
        Assert.Contains(EventType.Eos, encoder.Events);
    }

    /// <summary>
    /// A managed audio decoder treats the raw audio it is fed as the coded
    /// stream: every buffer reaches <c>handle_frame</c>, and the drain at the
    /// end of the stream reaches it as a null buffer.
    /// </summary>
    [Fact]
    public void AManagedAudioDecoderIsDrainedWithANullBuffer()
    {
        using Pipeline pipeline = Pipeline.New("managed-audio-decoder");
        using ProbeAudioDecoder decoder = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("audiotestsrc", "decoder-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 10);
        Assert.True(pipeline.AddMany(source, decoder, sink));
        Assert.True(source.Link(decoder));
        Assert.True(decoder.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        _output.WriteLine(
            FormattableString.Invariant($"managed audio decoder: decoded={decoder.Decoded}, drains={decoder.Drains}, ")
            + FormattableString.Invariant($"rendered={sink.Rendered}"));

        Assert.True(decoder.Decoded > 0);
        Assert.True(sink.Rendered > 0);
        Assert.True(decoder.Drains > 0);
    }

    /// <summary>
    /// A managed video encoder adopts every frame it is handed and consumes it
    /// with <c>FinishFrame</c>: the library frees each of them again, which the
    /// notify of the frame's user data reports.
    /// </summary>
    [Fact]
    public void AManagedVideoEncoderConsumesEveryFrameItAdopts()
    {
        const int Frames = 5;

        using Pipeline pipeline = Pipeline.New("managed-video-encoder");
        using ProbeVideoEncoder encoder = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("videotestsrc", "video-encoder-source")
            ?? throw new InvalidOperationException("videotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", Frames);
        Assert.True(pipeline.AddMany(source, encoder, sink));
        Assert.True(source.Link(encoder));
        Assert.True(encoder.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        _output.WriteLine(
            FormattableString.Invariant($"managed video encoder: encoded={encoder.Encoded}, released={encoder.Released}, ")
            + FormattableString.Invariant($"width={encoder.Width}, rendered={sink.Rendered}"));

        Assert.Equal(Frames, encoder.Encoded);
        Assert.Equal(Frames, sink.Rendered);
        Assert.True(encoder.Width > 0);

        // Nothing is left holding a frame: every one of them was freed.
        Assert.Equal(Frames, encoder.Released);
    }

    /// <summary>
    /// The same for a managed video decoder, which allocates the output buffer
    /// of the frame it adopted before finishing it.
    /// </summary>
    [Fact]
    public void AManagedVideoDecoderConsumesEveryFrameItAdopts()
    {
        const int Frames = 5;

        using Pipeline pipeline = Pipeline.New("managed-video-decoder");
        using ProbeVideoDecoder decoder = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("videotestsrc", "video-decoder-source")
            ?? throw new InvalidOperationException("videotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", Frames);
        Assert.True(pipeline.AddMany(source, decoder, sink));
        Assert.True(source.Link(decoder));
        Assert.True(decoder.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        _output.WriteLine(
            FormattableString.Invariant($"managed video decoder: decoded={decoder.Decoded}, released={decoder.Released}, ")
            + FormattableString.Invariant($"rendered={sink.Rendered}"));

        Assert.Equal(Frames, decoder.Decoded);
        Assert.Equal(Frames, sink.Rendered);
        Assert.Equal(Frames, decoder.Released);

        Assert.Contains(EventType.Caps, decoder.Events);
        Assert.Contains(EventType.Eos, decoder.Events);
    }

    /// <summary>
    /// A <c>pre_push</c> override that hands the very buffer back costs no
    /// reference: the slot answers the buffer its caller lent it, and the one
    /// reference that came in is the one that goes out.
    /// </summary>
    [Fact]
    public void ThePrePushOverrideThatAnswersItsOwnBufferMintsNoReference()
    {
        using ProbeAudioEncoder encoder = new() { PrePush = PrePushBehaviour.Unchanged };

        nint mine = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
        _ = TestNatives.MiniObjectRef(mine);
        Assert.Equal(2, Refcount(mine));

        nint buf = mine;
        FlowReturn flow = CallPrePush(encoder, &buf);

        _output.WriteLine(FormattableString.Invariant(
            $"pre_push identity: flow={flow}, lent=0x{mine:x}, answered=0x{buf:x}, refcount={Refcount(mine)}"));

        Assert.Equal(FlowReturn.Ok, flow);
        Assert.Equal(mine, buf);

        // The reference the slot was handed is the one it hands back.
        Assert.Equal(2, Refcount(mine));

        TestNatives.MiniObjectUnref(mine);
        TestNatives.MiniObjectUnref(mine);
    }

    /// <summary>
    /// A <c>pre_push</c> override that chains up hands the buffer on unchanged:
    /// the parent slot of a direct subclass is NULL, so the chain-up answers
    /// <see cref="FlowReturn.Ok"/> before it hands anything over, the wrapper
    /// stays attached, and the reference the slot was lent is the one it
    /// answers with.
    /// </summary>
    [Fact]
    public void ThePrePushOverrideThatChainsUpHandsTheBufferOn()
    {
        using ProbeAudioEncoder encoder = new() { PrePush = PrePushBehaviour.ChainUp };

        nint mine = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
        _ = TestNatives.MiniObjectRef(mine);
        Assert.Equal(2, Refcount(mine));

        nint buf = mine;
        FlowReturn flow = CallPrePush(encoder, &buf);

        _output.WriteLine(FormattableString.Invariant(
            $"pre_push chain-up: flow={flow}, lent=0x{mine:x}, answered=0x{buf:x}, refcount={Refcount(mine)}"));

        // The NULL parent slot is answered for, and nothing was consumed.
        Assert.Equal(FlowReturn.Ok, flow);
        Assert.Equal(mine, buf);
        Assert.Equal(2, Refcount(mine));

        TestNatives.MiniObjectUnref(mine);
        TestNatives.MiniObjectUnref(mine);
    }

    /// <summary>
    /// The same chain-up inside a running pipeline: every buffer the encoder
    /// hands to <c>pre_push</c> and chains up with still reaches the sink.
    /// </summary>
    [Fact]
    public void AManagedAudioEncoderThatChainsUpPrePushStillPushesEveryBuffer()
    {
        using Pipeline pipeline = Pipeline.New("managed-audio-encoder-chain-up");
        using ProbeAudioEncoder encoder = new() { PrePush = PrePushBehaviour.ChainUp };
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("audiotestsrc", "chain-up-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 10);
        Assert.True(pipeline.AddMany(source, encoder, sink));
        Assert.True(source.Link(encoder));
        Assert.True(encoder.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        _output.WriteLine(FormattableString.Invariant(
            $"pre_push chain-up pipeline: encoded={encoder.Encoded}, rendered={sink.Rendered}"));

        Assert.True(encoder.Encoded > 0);
        Assert.Equal(encoder.Encoded, sink.Rendered);
    }

    /// <summary>
    /// A <c>pre_push</c> override that answers a buffer of its own releases the
    /// one it was lent: the third form takes the reference in and hands another
    /// one out.
    /// </summary>
    [Fact]
    public void ThePrePushOverrideThatReplacesTheBufferReleasesTheLentOne()
    {
        using ProbeAudioEncoder encoder = new() { PrePush = PrePushBehaviour.Replace };

        nint mine = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
        _ = TestNatives.MiniObjectRef(mine);

        nint buf = mine;
        FlowReturn flow = CallPrePush(encoder, &buf);

        _output.WriteLine(
            FormattableString.Invariant($"pre_push replace: flow={flow}, lent=0x{mine:x} refcount={Refcount(mine)}, ")
            + FormattableString.Invariant($"answered=0x{buf:x} refcount={Refcount(buf)}"));

        Assert.Equal(FlowReturn.Ok, flow);
        Assert.NotEqual(mine, buf);
        Assert.NotEqual(nint.Zero, buf);

        // The lent buffer lost the reference the slot was handed, and the
        // answer carries exactly one.
        Assert.Equal(1, Refcount(mine));
        Assert.Equal(1, Refcount(buf));

        TestNatives.MiniObjectUnref(buf);
        TestNatives.MiniObjectUnref(mine);
    }

    /// <summary>
    /// A <c>pre_push</c> override that answers no buffer drops it: the handle
    /// is cleared and the reference the slot was handed is released.
    /// </summary>
    [Fact]
    public void ThePrePushOverrideThatAnswersNothingReleasesTheBuffer()
    {
        using ProbeAudioEncoder encoder = new() { PrePush = PrePushBehaviour.NullOut };

        nint mine = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
        _ = TestNatives.MiniObjectRef(mine);

        nint buf = mine;
        FlowReturn flow = CallPrePush(encoder, &buf);

        _output.WriteLine(FormattableString.Invariant(
            $"pre_push null: flow={flow}, answered=0x{buf:x}, refcount={Refcount(mine)}"));

        Assert.Equal(FlowReturn.Ok, flow);
        Assert.Equal(nint.Zero, buf);
        Assert.Equal(1, Refcount(mine));

        TestNatives.MiniObjectUnref(mine);
    }

    /// <summary>
    /// A <c>pre_push</c> override that throws is answered for by the trap: the
    /// slot reports an error, the handle is cleared, and the buffer the
    /// override never handed on is released rather than leaked.
    /// </summary>
    [Fact]
    public void ThePrePushOverrideThatThrowsReleasesTheBuffer()
    {
        using ProbeAudioEncoder encoder = new() { PrePush = PrePushBehaviour.Throw };

        List<Exception> trapped = [];
        void OnTrapped(Exception exception) => trapped.Add(exception);

        nint mine = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
        _ = TestNatives.MiniObjectRef(mine);

        nint buf = mine;
        FlowReturn flow;

        ExceptionTrap.UnhandledException += OnTrapped;

        try
        {
            flow = CallPrePush(encoder, &buf);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnTrapped;
        }

        _output.WriteLine(
            FormattableString.Invariant($"pre_push throw: flow={flow}, answered=0x{buf:x}, refcount={Refcount(mine)}, ")
            + FormattableString.Invariant($"trapped={trapped.Count}"));

        Assert.Equal(FlowReturn.Error, flow);
        Assert.Equal(nint.Zero, buf);
        Assert.Equal(1, Refcount(mine));
        Assert.Contains(trapped, exception => exception is InvalidOperationException);

        TestNatives.MiniObjectUnref(mine);
    }

    /// <summary>
    /// <c>AudioFilter::setup</c> is bound: a managed audio filter is lent the
    /// negotiated audio info, and the wrapper of that info stops meaning
    /// anything the moment the call returns.
    /// </summary>
    [Fact]
    public void AManagedAudioFilterIsLentTheNegotiatedAudioInfo()
    {
        using Pipeline pipeline = Pipeline.New("managed-audio-filter-setup");
        using ProbeAudioFilter filter = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("audiotestsrc", "filter-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 5);
        Assert.True(pipeline.AddMany(source, filter, sink));
        Assert.True(source.Link(filter));
        Assert.True(filter.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        _output.WriteLine(
            FormattableString.Invariant($"managed audio filter: rate={filter.SetupRate}, channels={filter.SetupChannels}, ")
            + FormattableString.Invariant($"transformed={filter.Transformed}"));

        Assert.True(filter.SetupRate > 0);
        Assert.True(filter.SetupChannels > 0);
        Assert.True(filter.Transformed > 0);

        // The borrow is scoped to the call: the wrapper the override kept is
        // detached, so reading through it throws rather than reading memory
        // the library may have reused.
        AudioInfo escaped = Assert.IsType<AudioInfo>(filter.EscapedInfo);
        _ = Assert.Throws<ObjectDisposedException>(() => escaped.Rate);
    }

    /// <summary>
    /// Every one of the five classes calls <c>handle_frame</c> unguarded, so a
    /// registration that leaves it out is refused before the type name is
    /// taken.
    /// </summary>
    [Fact]
    public void ACodecWithoutTheHandleFrameSlotIsRefused()
    {
        AssertRefused(
            "GstSharpTestParseWithoutHandleFrame",
            name => BaseParse.DefineSubclass(name, _ => { }, BaseParse.StartOverride));

        AssertRefused(
            "GstSharpTestAudioDecoderWithoutHandleFrame",
            name => AudioDecoder.DefineSubclass(name, _ => { }, AudioDecoder.StartOverride));

        AssertRefused(
            "GstSharpTestAudioEncoderWithoutHandleFrame",
            name => AudioEncoder.DefineSubclass(name, _ => { }, AudioEncoder.StartOverride));

        AssertRefused(
            "GstSharpTestVideoDecoderWithoutHandleFrame",
            name => VideoDecoder.DefineSubclass(name, _ => { }, VideoDecoder.StartOverride));

        AssertRefused(
            "GstSharpTestVideoEncoderWithoutHandleFrame",
            name => VideoEncoder.DefineSubclass(name, _ => { }, VideoEncoder.StartOverride));
    }

    private static int Refcount(nint handle) => ((MiniObjectRaw*)handle)->Refcount;

    private static nint ClassOf(Gst.GObject.Object instance) => *(nint*)instance.Handle;

    private static FlowReturn CallPrePush(AudioEncoder encoder, nint* buffer)
    {
        Gst.Audio.AudioEncoderClassRaw* klass = (Gst.Audio.AudioEncoderClassRaw*)ClassOf(encoder);

        Assert.NotEqual(nint.Zero, klass->PrePush);

        return (FlowReturn)((delegate* unmanaged[Cdecl]<nint, nint*, int>)klass->PrePush)(
            encoder.Handle,
            buffer);
    }

    private void AssertRefused(string typeName, Func<string, SubclassType> define)
    {
        Assert.False(GType.FromName(typeName).IsValid);

        ArgumentException error = Assert.Throws<ArgumentException>(() => define(typeName));

        _output.WriteLine(error.Message);
        Assert.Contains("HandleFrameOverride", error.Message, StringComparison.Ordinal);
        Assert.False(GType.FromName(typeName).IsValid);
    }
}
