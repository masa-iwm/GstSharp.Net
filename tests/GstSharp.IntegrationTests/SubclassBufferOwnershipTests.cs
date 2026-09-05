using Gst;
using Gst.Base;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// What a virtual method does with the buffers it is lent and the buffers it
/// hands over: the inout handle of <c>GstBaseSrc.create</c>, the identity
/// preserving <c>GstBaseTransform.prepare_output_buffer</c>, and the slot that
/// is declared without an override behind it.
/// </summary>
/// <remarks>
/// <para>
/// The slots are called through the class struct rather than through a
/// pipeline. That is the caller contract of these three: <c>create</c> is
/// called with a buffer already in <c>*buf</c> only by a pull mode peer, and
/// the managed <see cref="Pad.GetRange(ulong, uint, out Gst.Buffer)"/> has an
/// <c>out</c> parameter, so managed code cannot lend a buffer to it. Calling
/// the slot the way GStreamer calls it is what puts the reference counts under
/// a microscope, and the class struct offsets it reads are the ones the ABI
/// probes validate.
/// </para>
/// <para>
/// Nothing here is version dependent.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class SubclassBufferOwnershipTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassBufferOwnershipTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A <c>create</c> override that answers the very buffer it was lent costs
    /// no reference: the caller gets its own buffer back, still owning exactly
    /// the one reference it came in with.
    /// </summary>
    [Fact]
    public void TheCreateOverrideThatAnswersItsOwnBufferMintsNoReference()
    {
        using ProbeCreateSrc source = new();

        nint mine = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
        Assert.Equal(1, Refcount(mine));

        nint buf = mine;
        FlowReturn flow = CallCreate(source, 0, 4, &buf);

        _output.WriteLine(FormattableString.Invariant(
            $"identity: flow={flow}, lent=0x{mine:x}, answered=0x{buf:x}, refcount={Refcount(mine)}"));

        Assert.Equal(FlowReturn.Ok, flow);
        Assert.True(source.WasLentABuffer);
        Assert.Equal(mine, buf);

        // The caller still owns the one reference it created the buffer with:
        // the identity rule adds none, and the borrow took none.
        Assert.Equal(1, Refcount(mine));
        TestNatives.MiniObjectUnref(mine);
    }

    /// <summary>
    /// A <c>create</c> override that produces a buffer hands over exactly one
    /// reference: the wrapper of the override releases its own when the call
    /// ends, so the caller is the only owner left.
    /// </summary>
    [Fact]
    public void TheCreateOverrideThatProducesABufferHandsOverOneReference()
    {
        using ProbeCreateSrc source = new();

        nint buf = nint.Zero;
        FlowReturn flow = CallCreate(source, 0, 4, &buf);

        Assert.Equal(FlowReturn.Ok, flow);
        Assert.False(source.WasLentABuffer);
        Assert.NotEqual(nint.Zero, buf);

        _output.WriteLine(FormattableString.Invariant(
            $"produced: 0x{buf:x}, refcount={Refcount(buf)}"));

        Assert.Equal(1, Refcount(buf));
        TestNatives.MiniObjectUnref(buf);
    }

    /// <summary>
    /// A <c>prepare_output_buffer</c> override that answers its input works in
    /// place: the caller is handed the buffer it lent, with the reference count
    /// it had before the call.
    /// </summary>
    [Fact]
    public void PrepareOutputBufferAnsweringItsInputCostsNoReference()
    {
        using ProbePrepareTransform transform = new();

        nint input = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
        Assert.Equal(1, Refcount(input));

        nint output = nint.Zero;
        FlowReturn flow = CallPrepareOutputBuffer(transform, input, &output);

        _output.WriteLine(FormattableString.Invariant(
            $"identity: flow={flow}, input=0x{input:x}, output=0x{output:x}, refcount={Refcount(input)}"));

        Assert.Equal(FlowReturn.Ok, flow);
        Assert.Equal(input, output);
        Assert.Equal(1, Refcount(input));

        TestNatives.MiniObjectUnref(input);
    }

    /// <summary>
    /// A <c>prepare_output_buffer</c> override that answers a different buffer
    /// hands it over: the caller owns it once and the buffer it lent is
    /// untouched.
    /// </summary>
    [Fact]
    public void PrepareOutputBufferAnsweringAnotherBufferHandsItOverOnce()
    {
        using ProbePrepareTransform transform = new() { AnswerTheInput = false };

        nint input = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
        nint output = nint.Zero;

        FlowReturn flow = CallPrepareOutputBuffer(transform, input, &output);

        _output.WriteLine(FormattableString.Invariant(
            $"fresh: flow={flow}, output=0x{output:x}, refcount={Refcount(output)}, input={Refcount(input)}"));

        Assert.Equal(FlowReturn.Ok, flow);
        Assert.NotEqual(nint.Zero, output);
        Assert.NotEqual(input, output);

        // One reference for the caller, none left over from the wrapper the
        // override built, and the lent buffer is as it was.
        Assert.Equal(1, Refcount(output));
        Assert.Equal(1, Refcount(input));

        TestNatives.MiniObjectUnref(output);
        TestNatives.MiniObjectUnref(input);
    }

    /// <summary>
    /// A slot the base class calls unguarded has no default a chain-up could
    /// invent, so declaring it without overriding it fails loudly.
    /// <c>get_unit_size</c> is one: <c>gst_base_transform_transform_size</c>
    /// reads it with no NULL check. The default <c>On</c> chains up, the parent
    /// class left the slot empty, and the
    /// <see cref="InvalidOperationException"/> that says so is reported through
    /// <see cref="ExceptionTrap"/> and answered with the failure value — never
    /// with a silent success.
    /// </summary>
    [Fact]
    public void ADeclaredSlotWithNoOverrideBehindItFailsThroughTheTrap()
    {
        using ProbeUnimplementedTransform transform = new();
        using Caps caps = Caps.NewAny();

        List<Exception> reported = [];
        void Collect(Exception exception) => reported.Add(exception);

        int answered;
        nuint size = 12345;
        ExceptionTrap.UnhandledException += Collect;
        try
        {
            Gst.Base.BaseTransformClassRaw* klass = (Gst.Base.BaseTransformClassRaw*)ClassOf(transform);
            Assert.NotEqual(nint.Zero, klass->GetUnitSize);

            answered = ((delegate* unmanaged[Cdecl]<nint, nint, nuint*, int>)klass->GetUnitSize)(
                transform.Handle, caps.Handle, &size);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= Collect;
        }

        _output.WriteLine($"answered={answered}, reported={reported.Count}");

        Assert.Equal(0, answered);
        Exception failure = Assert.Single(reported);
        _output.WriteLine(failure.Message);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("OnGetUnitSize", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A slot the base class guards answers what the base class answers in its
    /// place. <c>transform</c> is one: <c>gst_base_transform_default_generate_output</c>
    /// answers <see cref="FlowReturn.NotSupported"/> when the slot is NULL, so
    /// a chain-up that reaches no implementation answers the same rather than
    /// throwing, and the buffers it was lent are untouched.
    /// </summary>
    [Fact]
    public void AGuardedSlotWithNoOverrideBehindItAnswersTheDocumentedDefault()
    {
        using ProbeUnimplementedTransform transform = new();

        nint input = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
        nint output = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);

        List<Exception> reported = [];
        void Collect(Exception exception) => reported.Add(exception);

        FlowReturn flow;
        ExceptionTrap.UnhandledException += Collect;
        try
        {
            Gst.Base.BaseTransformClassRaw* klass = (Gst.Base.BaseTransformClassRaw*)ClassOf(transform);
            Assert.NotEqual(nint.Zero, klass->Transform);

            flow = (FlowReturn)((delegate* unmanaged[Cdecl]<nint, nint, nint, int>)klass->Transform)(
                transform.Handle, input, output);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= Collect;
        }

        _output.WriteLine($"flow={flow}, reported={reported.Count}");

        Assert.Equal(FlowReturn.NotSupported, flow);
        Assert.Empty(reported);

        // The borrowed wrappers released nothing on the way out.
        Assert.Equal(1, Refcount(input));
        Assert.Equal(1, Refcount(output));

        TestNatives.MiniObjectUnref(input);
        TestNatives.MiniObjectUnref(output);
    }

    private static FlowReturn CallCreate(BaseSrc source, ulong offset, uint size, nint* buf)
    {
        Gst.Base.BaseSrcClassRaw* klass = (Gst.Base.BaseSrcClassRaw*)ClassOf(source);

        Assert.NotEqual(nint.Zero, klass->Create);
        return (FlowReturn)((delegate* unmanaged[Cdecl]<nint, ulong, uint, nint*, int>)klass->Create)(
            source.Handle, offset, size, buf);
    }

    private static FlowReturn CallPrepareOutputBuffer(BaseTransform transform, nint input, nint* output)
    {
        Gst.Base.BaseTransformClassRaw* klass = (Gst.Base.BaseTransformClassRaw*)ClassOf(transform);

        Assert.NotEqual(nint.Zero, klass->PrepareOutputBuffer);
        return (FlowReturn)((delegate* unmanaged[Cdecl]<nint, nint, nint*, int>)klass->PrepareOutputBuffer)(
            transform.Handle, input, output);
    }

    /// <summary>Reads the class of an instance, which is its first field.</summary>
    /// <param name="instance">The object whose class is read.</param>
    /// <returns>The class struct of the instance.</returns>
    private static nint ClassOf(Gst.GObject.Object instance) => *(nint*)instance.Handle;

    private static int Refcount(nint handle) => ((MiniObjectRaw*)handle)->Refcount;
}

/// <summary>
/// A managed source whose <c>create</c> override answers the buffer it was
/// lent when there is one, and a fresh buffer when there is not.
/// </summary>
internal sealed class ProbeCreateSrc : BaseSrc
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeCreateSrc";

    private static readonly PadTemplate SrcTemplate = ProbeTemplates.Any("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        config =>
        {
            config.SetMetadata(
                "GstSharp probe create source",
                "Source/Testing",
                "Answers the buffer it is lent",
                "GstSharp.Net integration tests");
            config.AddPadTemplate(SrcTemplate);
        },
        CreateOverride);

    /// <summary>Creates a managed source.</summary>
    internal ProbeCreateSrc()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets whether the last call was lent a buffer.</summary>
    internal bool WasLentABuffer { get; private set; }

    /// <inheritdoc/>
    protected override FlowReturn OnCreate(ulong offset, uint size, ref Gst.Buffer? buf)
    {
        WasLentABuffer = buf is not null;

        // The lent buffer is answered as it is; only the push path, which
        // lends nothing, has to produce one.
        buf ??= Gst.Buffer.NewAllocate(null, size, null);
        return buf is null ? FlowReturn.Error : FlowReturn.Ok;
    }
}

/// <summary>
/// A managed filter whose <c>prepare_output_buffer</c> override answers either
/// the buffer it was lent or a new one.
/// </summary>
internal sealed class ProbePrepareTransform : BaseTransform
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbePrepareTransform";

    private static readonly PadTemplate SinkTemplate = ProbeTemplates.Any("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = ProbeTemplates.Any("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        config =>
        {
            config.SetMetadata(
                "GstSharp probe prepare filter",
                "Filter/Testing",
                "Answers the buffer it is lent",
                "GstSharp.Net integration tests");
            config.AddPadTemplate(SinkTemplate);
            config.AddPadTemplate(SrcTemplate);
        },
        PrepareOutputBufferOverride);

    /// <summary>Creates a managed filter.</summary>
    internal ProbePrepareTransform()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets whether the override answers the buffer it was lent.</summary>
    internal bool AnswerTheInput { get; init; } = true;

    /// <inheritdoc/>
    protected override FlowReturn OnPrepareOutputBuffer(Gst.Buffer input, out Gst.Buffer? outbuf)
    {
        ArgumentNullException.ThrowIfNull(input);

        outbuf = AnswerTheInput ? input : Gst.Buffer.NewAllocate(null, 4, null);
        return outbuf is null ? FlowReturn.Error : FlowReturn.Ok;
    }
}

/// <summary>
/// A managed filter that declares <c>transform</c> and overrides nothing: the
/// slot it installs finds no implementation below it.
/// </summary>
internal sealed class ProbeUnimplementedTransform : BaseTransform
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeUnimplementedTransform";

    private static readonly PadTemplate SinkTemplate = ProbeTemplates.Any("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = ProbeTemplates.Any("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        config =>
        {
            config.SetMetadata(
                "GstSharp probe unimplemented filter",
                "Filter/Testing",
                "Declares a slot it does not implement",
                "GstSharp.Net integration tests");
            config.AddPadTemplate(SinkTemplate);
            config.AddPadTemplate(SrcTemplate);
        },
        // The diagnostic is what this probe is built to earn: it declares the
        // two slots and implements neither, which is the shape the ownership
        // tests read the base class through. Removing the declarations would
        // remove the fixture, so the rule is suppressed here instead.
#pragma warning disable GST0004
        TransformOverride,
        GetUnitSizeOverride);
#pragma warning restore GST0004

    /// <summary>Creates a managed filter.</summary>
    internal ProbeUnimplementedTransform()
        : base(Definition.NewInstance())
    {
    }
}

/// <summary>The pad templates the ownership probes are described with.</summary>
internal static class ProbeTemplates
{
    /// <summary>Builds a template that accepts anything.</summary>
    /// <param name="name">The name of the template.</param>
    /// <param name="direction">Which way the pads point.</param>
    /// <returns>The template, which lives for the process.</returns>
    internal static PadTemplate Any(string name, PadDirection direction)
    {
        using Caps caps = Caps.NewAny();

        return PadTemplate.New(name, direction, PadPresence.Always, caps)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}
