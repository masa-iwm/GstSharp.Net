using Gst;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The setters of the fields a subclass owns: the output and input buffer of a
/// <c>GstVideoCodecFrame</c>, the caps and the allocation caps of a
/// <c>GstVideoCodecState</c>, and the buffer of a <c>GstBaseParseFrame</c>.
/// Each of them takes the reference of the wrapper it is given and releases
/// the value it replaced, which is what <c>gst_buffer_replace</c> and
/// <c>gst_caps_replace</c> do in C.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class SubclassCodecFieldSetterTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassCodecFieldSetterTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A decoder that writes the output buffer of every frame itself pushes
    /// what it wrote: the wrapper is detached by the setter, the field answers
    /// the very buffer, replacing it hands the reference of the previous one
    /// back and clearing it empties the field.
    /// </summary>
    [Fact]
    public void AVideoDecoderWritesTheOutputBufferOfEveryFrame()
    {
        const int Frames = 5;

        using Pipeline pipeline = Pipeline.New("field-setter-decoder");
        using ProbeSetterVideoDecoder decoder = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("videotestsrc", "field-setter-source")
            ?? throw new InvalidOperationException("videotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", Frames);
        Assert.True(pipeline.AddMany(source, decoder, sink));
        Assert.True(source.Link(decoder));
        Assert.True(decoder.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        FieldSetterObservations seen = decoder.Observations;

        _output.WriteLine(
            FormattableString.Invariant($"decoded={decoder.Decoded}, rendered={sink.Rendered}, ")
            + FormattableString.Invariant($"bytes={sink.Bytes}, size={decoder.OutputSize}"));

        Assert.Equal(Frames, decoder.Decoded);
        Assert.Equal(Frames, sink.Rendered);
        Assert.Equal(Frames * (long)decoder.OutputSize, sink.Bytes);

        Assert.True(seen.OutputWrapperDetached, "the setter did not detach the wrapper");
        Assert.True(seen.OutputHandleMatched, "the field did not answer the buffer that was set");
        Assert.True(seen.OutputSharedWhileHeld, "the frame did not hold a reference of the buffer");
        Assert.True(seen.OutputReleasedByReplacement, "replacing the buffer did not release the previous one");
        Assert.True(seen.OutputCleared, "clearing the field left something in it");
    }

    /// <summary>
    /// Replacing the input buffer of a frame writes the field the base class
    /// filled and releases the buffer that was there: the frame answers the
    /// new one, and the previous one is left with one reference less.
    /// </summary>
    [Fact]
    public void AVideoDecoderReplacesTheInputBufferOfAFrame()
    {
        const int Frames = 3;

        using Pipeline pipeline = Pipeline.New("field-setter-input");
        using ProbeSetterVideoDecoder decoder = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("videotestsrc", "field-setter-input-source")
            ?? throw new InvalidOperationException("videotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", Frames);
        Assert.True(pipeline.AddMany(source, decoder, sink));
        Assert.True(source.Link(decoder));
        Assert.True(decoder.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        FieldSetterObservations seen = decoder.Observations;

        _output.WriteLine(
            FormattableString.Invariant($"input matched={seen.InputHandleMatched}, ")
            + FormattableString.Invariant($"references dropped={seen.InputReferencesDropped}"));

        Assert.Equal(Frames, sink.Rendered);
        Assert.True(seen.InputHandleMatched, "the field did not answer the buffer that was set");
        Assert.Equal(1, seen.InputReferencesDropped);
    }

    /// <summary>
    /// The caps a decoder writes into its output state are the caps it
    /// negotiates, and the allocation caps it writes are the ones the
    /// allocation query is made with — two different values, so neither can
    /// stand in for the other.
    /// </summary>
    [Fact]
    public void ADecoderNegotiatesTheCapsItWroteIntoItsOutputState()
    {
        const int Frames = 2;

        using Pipeline pipeline = Pipeline.New("field-setter-caps");
        using ProbeSetterVideoDecoder decoder = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("videotestsrc", "field-setter-caps-source")
            ?? throw new InvalidOperationException("videotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", Frames);
        Assert.True(pipeline.AddMany(source, decoder, sink));
        Assert.True(source.Link(decoder));
        Assert.True(decoder.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        FieldSetterObservations seen = decoder.Observations;

        _output.WriteLine(FormattableString.Invariant($"caps={seen.CapsAfterSet}"));
        _output.WriteLine(FormattableString.Invariant($"allocation caps={seen.AllocationCapsAfterSet}"));
        _output.WriteLine(FormattableString.Invariant($"query caps={seen.AllocationQueryCaps}"));

        string negotiated = Framerate(ProbeSetterVideoDecoder.OutputFramerate);
        string allocation = Framerate(ProbeSetterVideoDecoder.AllocationFramerate);

        Assert.True(seen.CapsCleared, "clearing the caps left something in the field");
        Assert.NotNull(seen.CapsAfterSet);
        Assert.Contains(negotiated, seen.CapsAfterSet, StringComparison.Ordinal);

        Assert.NotNull(seen.AllocationCapsAfterSet);
        Assert.Contains(allocation, seen.AllocationCapsAfterSet, StringComparison.Ordinal);

        // The allocation query is made with the allocation caps, not with the
        // caps of the state.
        Assert.NotNull(seen.AllocationQueryCaps);
        Assert.Contains(allocation, seen.AllocationQueryCaps, StringComparison.Ordinal);

        _output.WriteLine(FormattableString.Invariant($"src caps={seen.NegotiatedSrcCaps}"));
        Assert.NotNull(seen.NegotiatedSrcCaps);
        Assert.Contains(negotiated, seen.NegotiatedSrcCaps, StringComparison.Ordinal);
    }

    /// <summary>
    /// A parser may move the buffer it was given into the output buffer and
    /// clear the field it came from, the way <c>gst_aac_parse_pre_push_frame</c>
    /// does: the frame answers no buffer from then on, and what it was handed
    /// still reaches the sink.
    /// </summary>
    [Fact]
    public void AParserMovesTheFrameBufferIntoTheOutputBuffer()
    {
        using Pipeline pipeline = Pipeline.New("field-setter-parser");
        using ProbeSetterParse parser = new();
        using ProbeAnySink sink = new();
        Element source = ElementFactory.Make("audiotestsrc", "field-setter-parser-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 20);
        Assert.True(pipeline.AddMany(source, parser, sink));
        Assert.True(source.Link(parser));
        Assert.True(parser.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        _output.WriteLine(
            FormattableString.Invariant($"framed={parser.Framed}, moved={parser.MovedOut}, ")
            + FormattableString.Invariant($"rendered={sink.Rendered}, bytes={sink.Bytes}"));

        Assert.True(parser.Framed > 0, "the parser framed nothing");
        Assert.Equal(parser.Framed, parser.MovedOut);
        Assert.Equal(parser.Framed, sink.Rendered);
        Assert.Equal((long)parser.Framed * ProbeSetterParse.FrameSize, sink.Bytes);
        Assert.True(parser.BufferFieldCleared, "the frame still carried a buffer after the field was cleared");
    }

    private static string Framerate(int value) =>
        FormattableString.Invariant($"framerate=(fraction){value}/1");
}
