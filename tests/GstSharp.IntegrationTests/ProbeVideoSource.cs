using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESVideoSource</c> whose <c>create_source</c> override answers
/// a <c>videotestsrc</c>, and which records the two slots that say something
/// about how the editing services build and unbuild it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in C# constructs one of these in the paths that matter. The library
/// builds it, through <c>ges_asset_extract</c>, when a clip of a managed type
/// is added to a layer, split or pasted, so the wrapper is fabricated and every
/// override below runs for an instance the test never made. See
/// <c>docs/subclassing.md</c> §11.
/// </para>
/// <para>
/// <see cref="New"/> exists for the negative case only:
/// <see cref="ProbeNewChildSourceClip"/> answers such an instance, which has no
/// asset and therefore no <c>nleobject</c>.
/// </para>
/// </remarks>
internal sealed class ProbeVideoSource : GES.VideoSource, IManagedSubclass<ProbeVideoSource>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesVideoSource";

    private static readonly SubclassType Definition = DefineSubclass<ProbeVideoSource>(
        GTypeName,
        null,
        CreateSourceOverride,
        SetMaxDurationOverride,
        SetParentOverride);

    private static int _wrappersBuilt;

    private readonly List<string?> _maxDurationNames = [];

    private int _maxDurationCalls;
    private bool _sawUnnamedMaxDuration;
    private int _setParentCalls;
    private bool _lastParentWasNull;

    private ProbeVideoSource(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the source.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets how many wrappers were fabricated since the last reset.</summary>
    internal static int WrappersBuilt => Volatile.Read(ref _wrappersBuilt);

    /// <summary>Gets how often <c>set_max_duration</c> reached this wrapper.</summary>
    internal int MaxDurationCalls => Volatile.Read(ref _maxDurationCalls);

    /// <summary>
    /// Gets a value indicating whether <c>set_max_duration</c> ever reached
    /// this wrapper while the instance was still unnamed, which is what the
    /// construction-time call would look like.
    /// </summary>
    internal bool SawUnnamedMaxDuration => Volatile.Read(ref _sawUnnamedMaxDuration);

    /// <summary>Gets the name the instance had at each <c>set_max_duration</c>.</summary>
    internal IReadOnlyList<string?> MaxDurationNames
    {
        get
        {
            lock (_maxDurationNames)
            {
                return _maxDurationNames.ToArray();
            }
        }
    }

    /// <summary>Gets how often <c>set_parent</c> reached this wrapper.</summary>
    internal int SetParentCalls => Volatile.Read(ref _setParentCalls);

    /// <summary>Gets a value indicating whether the last parent handed over was none.</summary>
    internal bool LastParentWasNull => Volatile.Read(ref _lastParentWasNull);

    /// <summary>Builds an instance no asset describes, for the negative case.</summary>
    /// <returns>The new source, which has no asset.</returns>
    internal static ProbeVideoSource New() => new(Definition.NewInstance());

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset() => Volatile.Write(ref _wrappersBuilt, 0);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeVideoSource CreateWrapper(SubclassCtorArgs args)
    {
        ProbeVideoSource wrapper = new(args);
        _ = Interlocked.Increment(ref _wrappersBuilt);
        return wrapper;
    }

    /// <inheritdoc/>
    protected override Gst.Element OnCreateSource()
    {
        // The element must have no parent: a failed gst_bin_add releases both
        // the answer and the nlesource (ges-track-element.c:1073-1078). The
        // wrapper keeps the reference it made and the topbin takes one of its
        // own.
        return Gst.ElementFactory.Make("videotestsrc", null)
            ?? throw new InvalidOperationException("videotestsrc is not installed.");
    }

    /// <inheritdoc/>
    protected override bool OnSetMaxDuration(Gst.ClockTime maxduration)
    {
        _ = Interlocked.Increment(ref _maxDurationCalls);

        string? name = Name;

        lock (_maxDurationNames)
        {
            _maxDurationNames.Add(name);
        }

        if (string.IsNullOrEmpty(name))
        {
            Volatile.Write(ref _sawUnnamedMaxDuration, true);
        }

        return ChainUpSetMaxDuration(maxduration);
    }

    /// <inheritdoc/>
    protected override bool OnSetParent(GES.TimelineElement? newParent)
    {
        _ = Interlocked.Increment(ref _setParentCalls);
        Volatile.Write(ref _lastParentWasNull, newParent is null);

        return ChainUpSetParent(newParent);
    }
}
