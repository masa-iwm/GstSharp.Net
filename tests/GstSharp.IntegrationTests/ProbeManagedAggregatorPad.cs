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

    private readonly object _answers = new();

    private int _flushed;

    private int _skipped;

    private FlowReturn? _lastFlushAnswer;

    private bool? _lastSkipAnswer;

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

    /// <summary>
    /// Gets what the last chain-up of the <c>flush</c> slot answered, or
    /// <see langword="null"/> when none has run or the last one threw.
    /// </summary>
    internal FlowReturn? LastFlushAnswer
    {
        get
        {
            lock (_answers)
            {
                return _lastFlushAnswer;
            }
        }
    }

    /// <summary>
    /// Gets what the last chain-up of the <c>skip_buffer</c> slot answered, or
    /// <see langword="null"/> when none has run or the last one threw.
    /// </summary>
    internal bool? LastSkipAnswer
    {
        get
        {
            lock (_answers)
            {
                return _lastSkipAnswer;
            }
        }
    }

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

    /// <summary>Chains the <c>flush</c> slot up from outside a slot call.</summary>
    /// <param name="aggregator">The aggregator the pad belongs to.</param>
    /// <returns>What the class below the override answers.</returns>
    internal FlowReturn ChainUpFlushForTest(Aggregator aggregator) => ChainUpFlush(aggregator);

    /// <summary>Chains the <c>skip_buffer</c> slot up from outside a slot call.</summary>
    /// <param name="aggregator">The aggregator the pad belongs to.</param>
    /// <param name="buffer">The buffer the decision is about.</param>
    /// <returns>What the class below the override answers.</returns>
    internal bool ChainUpSkipBufferForTest(Aggregator aggregator, Gst.Buffer buffer) =>
        ChainUpSkipBuffer(aggregator, buffer);

    /// <inheritdoc/>
    protected override FlowReturn OnFlush(Aggregator aggregator)
    {
        _ = Interlocked.Increment(ref _flushed);

        FlowReturn answer = ChainUpFlush(aggregator);

        lock (_answers)
        {
            _lastFlushAnswer = answer;
        }

        return answer;
    }

    /// <inheritdoc/>
    protected override bool OnSkipBuffer(Aggregator aggregator, Gst.Buffer buffer)
    {
        _ = Interlocked.Increment(ref _skipped);

        bool answer = ChainUpSkipBuffer(aggregator, buffer);

        lock (_answers)
        {
            _lastSkipAnswer = answer;
        }

        return answer;
    }
}
