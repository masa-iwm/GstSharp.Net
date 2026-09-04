using Gst;
using Gst.Base;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// What happens to the wrapper an override answers when the slot hands a mini
/// object over.
/// </summary>
/// <remarks>
/// The element takes the reference the wrapper held rather than a second one,
/// which is what keeps a produced object writable for whoever gets it and a
/// pooled one out of the finalizer queue. The slot is called through the class
/// struct so that the counts around the call are observable.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class SubclassReturnedMiniObjectTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassReturnedMiniObjectTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The caps the override built arrive with exactly one reference — the one
    /// its wrapper held — and the wrapper is detached by the return.
    /// </summary>
    [Fact]
    public void TheCapsAnOverrideProducesArriveWithTheReferenceOfItsWrapper()
    {
        using ProbeCapsTransform transform = new() { AnswerTheInput = false };
        using Caps given = Caps.NewAny();

        nint answered = CallTransformCaps(transform, given.Handle);

        _output.WriteLine(FormattableString.Invariant(
            $"produced caps 0x{answered:x} refcount={Refcount(answered)}"));

        try
        {
            // One reference, held by nobody but this caller: a second one would
            // make the caps non writable and would only be released when the
            // wrapper the override built was collected.
            Assert.Equal(1, Refcount(answered));

            Caps kept = Assert.IsType<Caps>(transform.Kept);
            Assert.True(kept.IsDisposed);
            _ = Assert.Throws<ObjectDisposedException>(() => kept.IsAny());
        }
        finally
        {
            GstNative.MiniObjectUnref(answered);
        }
    }

    /// <summary>
    /// The other half: an override that answers the very object it was lent has
    /// no reference to give away, so one is minted and the borrow survives.
    /// </summary>
    [Fact]
    public void TheCapsAnOverrideWasLentAreReferencedForTheCaller()
    {
        using ProbeCapsTransform transform = new() { AnswerTheInput = true };
        using Caps given = Caps.NewAny();

        Assert.Equal(1, Refcount(given.Handle));

        nint answered = CallTransformCaps(transform, given.Handle);

        _output.WriteLine(FormattableString.Invariant(
            $"lent caps 0x{answered:x} refcount={Refcount(answered)}"));

        Assert.Equal(given.Handle, answered);
        Assert.Equal(2, Refcount(answered));

        GstNative.MiniObjectUnref(answered);
        Assert.Equal(1, Refcount(given.Handle));
    }

    private static nint CallTransformCaps(BaseTransform transform, nint caps)
    {
        Gst.Base.BaseTransformClassRaw* klass = (Gst.Base.BaseTransformClassRaw*)ClassOf(transform);

        Assert.NotEqual(nint.Zero, klass->TransformCaps);
        return ((delegate* unmanaged[Cdecl]<nint, int, nint, nint, nint>)klass->TransformCaps)(
            transform.Handle, (int)PadDirection.Sink, caps, nint.Zero);
    }

    private static nint ClassOf(Gst.GObject.Object instance) => *(nint*)instance.Handle;

    private static int Refcount(nint handle) => ((MiniObjectRaw*)handle)->Refcount;
}

/// <summary>
/// A managed filter whose <c>transform_caps</c> override either answers fresh
/// caps or the ones it was lent.
/// </summary>
internal sealed class ProbeCapsTransform : BaseTransform
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeCapsTransform";

    private static readonly PadTemplate SinkTemplate = ProbeTemplates.Any("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = ProbeTemplates.Any("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        config =>
        {
            config.SetMetadata(
                "GstSharp probe caps filter",
                "Filter/Testing",
                "Answers caps of its own or the ones it was lent",
                "GstSharp.Net integration tests");
            config.AddPadTemplate(SinkTemplate);
            config.AddPadTemplate(SrcTemplate);
        },
        TransformCapsOverride);

    /// <summary>Creates a managed filter.</summary>
    internal ProbeCapsTransform()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets whether the override answers the caps it was lent.</summary>
    internal bool AnswerTheInput { get; init; }

    /// <summary>Gets the wrapper the last call answered.</summary>
    internal Caps? Kept { get; private set; }

    /// <inheritdoc/>
    protected override Caps? OnTransformCaps(PadDirection direction, Caps caps, Caps? filter)
    {
        ArgumentNullException.ThrowIfNull(caps);

        Kept = AnswerTheInput ? caps : Caps.NewEmptySimple("audio/x-raw");
        return Kept;
    }
}
