using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstAggregatorPad</c> subclass, built by the
/// <c>create_new_pad</c> override of <see cref="ProbeCreateNewPadAggregator"/>
/// with the construction properties a pad needs.
/// </summary>
internal sealed class ProbeManagedAggregatorPad : AggregatorPad, IManagedSubclass<ProbeManagedAggregatorPad>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestManagedAggregatorPad";

    private static readonly SubclassType Definition = DefineSubclass<ProbeManagedAggregatorPad>(
        GTypeName,
        null,
        FlushOverride,
        SkipBufferOverride);

    private int _flushed;

    private int _skipped;

    private ProbeManagedAggregatorPad(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the type the pad is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets how often the <c>flush</c> override ran for this pad.</summary>
    internal int Flushed => Volatile.Read(ref _flushed);

    /// <summary>Gets how often the <c>skip_buffer</c> override ran for this pad.</summary>
    internal int Skipped => Volatile.Read(ref _skipped);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeManagedAggregatorPad CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <summary>Creates a pad of the managed type from C#.</summary>
    /// <param name="name">The name of the pad.</param>
    /// <param name="templ">The template the pad is created from.</param>
    /// <returns>The pad, which is floating until an element takes it.</returns>
    /// <remarks>
    /// <c>direction</c> is construct only on a <c>GstPad</c>, so it can only be
    /// given while the instance is being built, which is what the dictionary
    /// overload of <see cref="SubclassType.NewInstance()"/> exists for.
    /// </remarks>
    internal static ProbeManagedAggregatorPad New(string name, PadTemplate templ) =>
        new(Definition.NewInstance(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["direction"] = PadDirection.Sink,
            ["template"] = templ,
        }));

    /// <inheritdoc/>
    protected override FlowReturn OnFlush(Aggregator aggregator)
    {
        _ = Interlocked.Increment(ref _flushed);
        return ChainUpFlush(aggregator);
    }

    /// <inheritdoc/>
    protected override bool OnSkipBuffer(Aggregator aggregator, Gst.Buffer buffer)
    {
        _ = Interlocked.Increment(ref _skipped);
        return ChainUpSkipBuffer(aggregator, buffer);
    }
}
