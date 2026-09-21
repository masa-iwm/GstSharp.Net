using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESSourceClip</c> that takes over <c>ungroup</c>, which
/// <c>GES.Container</c> declares and no allowlisted class of its own: the
/// override is declared against a clip base, because that is where a managed
/// type may stand.
/// </summary>
/// <remarks>
/// It grows a child of each of the two track types, so that the split has
/// something to split, and answers either what a test handed it or what the
/// implementation below it answers.
/// </remarks>
internal sealed class ProbeUngroupSourceClip : GES.SourceClip, IManagedSubclass<ProbeUngroupSourceClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesUngroupSourceClip";

    private static readonly SubclassType Definition = DefineSubclass<ProbeUngroupSourceClip>(
        GTypeName,
        null,
        CreateTrackElementOverride,
        UngroupOverride);

    private int _ungrouped;

    private ProbeUngroupSourceClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets how often the <c>ungroup</c> override ran.</summary>
    internal int Ungrouped => Volatile.Read(ref _ungrouped);

    /// <summary>
    /// Gets or sets what the override answers, or <see langword="null"/> to
    /// chain up, which is what the default implementation does.
    /// </summary>
    internal IReadOnlyList<GES.Container>? Answer { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the override answers a list with
    /// an empty entry, which the hand-out refuses.
    /// </summary>
    internal bool AnswersANullEntry { get; set; }

    /// <summary>Builds a clip out of an asset for its own type.</summary>
    /// <returns>The new clip, which has an asset.</returns>
    internal static ProbeUngroupSourceClip New()
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<ProbeUngroupSourceClip>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeUngroupSourceClip CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override GES.TrackElement? OnCreateTrackElement(GES.TrackType type)
    {
        nuint childType = type switch
        {
            GES.TrackType.Video => ProbeVideoSource.Registration.GType.Value,
            GES.TrackType.Audio => GES.AudioTestSource.GetGType(),
            _ => 0,
        };

        if (childType == 0)
        {
            return null;
        }

        GES.Asset asset = GES.Asset.Request(new GType(childType), null)
            ?? throw new InvalidOperationException("The source asset could not be requested.");

        return asset.Extract<GES.TrackElement>();
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<GES.Container> OnUngroup(bool recursive)
    {
        _ = Interlocked.Increment(ref _ungrouped);

        if (AnswersANullEntry)
        {
            return [this, null!];
        }

        return Answer ?? ChainUpUngroup(recursive);
    }
}
