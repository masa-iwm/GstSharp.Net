using Gst;
using Gst.Base;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The identity rule of <c>prepare_output_buffer</c> inside a running
/// pipeline: the base implementation answers the very buffer it was given
/// whenever the input is writable and the filter works in place, and that
/// pointer carries no reference of its own.
/// </summary>
/// <remarks>
/// A chain-up that adopted it would claim the only reference the caller holds
/// and release it at the end of the call, leaving
/// <c>gst_base_transform_default_generate_output</c> with a freed buffer. What
/// this drives is the shape a filter is written in: override, chain up, hand
/// the answer of the parent back.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SubclassIdentityBufferTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassIdentityBufferTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// An override chains up to <c>prepare_output_buffer</c> and answers what
    /// it got. The parent hands the input buffer back, so the chain-up has to
    /// hand the input <em>wrapper</em> back rather than adopt the pointer a
    /// second time, and the pipeline runs to the end of the stream with every
    /// buffer intact.
    /// </summary>
    [Fact]
    public void AnOverrideChainsUpPrepareOutputBufferAndReturnsItsResult()
    {
        const int Buffers = 5;

        using Pipeline pipeline = Pipeline.New("identity-prepare-output-buffer");
        ProbeSrc source = new() { BufferCount = Buffers };
        ProbeIdentityTransform filter = new();
        ProbeSink sink = new();

        Assert.True(pipeline.AddMany(source, filter, sink));
        Assert.True(source.Link(filter));
        Assert.True(filter.Link(sink));

        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);

            Assert.NotNull(message);
            _output.WriteLine(FormattableString.Invariant(
                $"bus: {message.Type}, prepared={filter.Prepared}, identity={filter.IdentityAnswers}, rendered={sink.Rendered.Count}"));

            Assert.Equal(MessageType.Eos, message.Type);
        }
        finally
        {
            _ = pipeline.SetState(State.Null);
            bus.Dispose();
        }

        // The parent answered the buffer it was given every time, and the
        // chain-up handed the wrapper of that buffer back rather than a second
        // one claiming the same reference.
        Assert.Equal(Buffers, filter.Prepared);
        Assert.Equal(filter.Prepared, filter.IdentityAnswers);
        Assert.Equal(Buffers, sink.Rendered.Count);

        source.Dispose();
        filter.Dispose();
        sink.Dispose();
    }
}

/// <summary>
/// A managed in place filter that takes over <c>prepare_output_buffer</c>,
/// chains up and answers what the parent answered.
/// </summary>
internal sealed class ProbeIdentityTransform : BaseTransform
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeIdentityTransform";

    private static readonly PadTemplate SinkTemplate = NewTemplate("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = NewTemplate("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        PrepareOutputBufferOverride,
        TransformIpOverride,
        SetCapsOverride);

    private int _prepared;

    private int _identityAnswers;

    /// <summary>Creates a managed filter.</summary>
    internal ProbeIdentityTransform()
        : base(Definition.NewInstance())
    {
        // The in place path is the one whose default prepare_output_buffer
        // answers the input buffer; without it the base class allocates a new
        // one and the identity rule never fires.
        SetInPlace(true);
        SetPassthrough(false);
    }

    /// <summary>Gets how many times the override ran.</summary>
    internal int Prepared => Volatile.Read(ref _prepared);

    /// <summary>Gets how often the parent answered the buffer it was given.</summary>
    internal int IdentityAnswers => Volatile.Read(ref _identityAnswers);

    /// <inheritdoc/>
    protected override FlowReturn OnPrepareOutputBuffer(Gst.Buffer input, out Gst.Buffer? outbuf)
    {
        ArgumentNullException.ThrowIfNull(input);

        FlowReturn flow = ChainUpPrepareOutputBuffer(input, out outbuf);
        _ = Interlocked.Increment(ref _prepared);

        if (ReferenceEquals(outbuf, input))
        {
            _ = Interlocked.Increment(ref _identityAnswers);
        }

        return flow;
    }

    /// <inheritdoc/>
    protected override FlowReturn OnTransformIp(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return FlowReturn.Ok;
    }

    /// <inheritdoc/>
    protected override bool OnSetCaps(Caps inCaps, Caps outCaps) => ChainUpSetCaps(inCaps, outCaps);

    /// <summary>Describes the class, and gives it the two pads it needs.</summary>
    /// <param name="config">The class being initialised.</param>
    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe identity filter",
            "Filter/Testing",
            "Chains up to prepare_output_buffer and answers what it got",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }

    /// <summary>Builds one pad template of the class.</summary>
    /// <param name="name">The name of the template.</param>
    /// <param name="direction">Which way the pad points.</param>
    /// <returns>The template, which lives for the process.</returns>
    private static PadTemplate NewTemplate(string name, PadDirection direction)
    {
        using Caps caps = Caps.NewEmptySimple(ProbeSrc.MediaType);

        return PadTemplate.New(name, direction, PadPresence.Always, caps)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}
