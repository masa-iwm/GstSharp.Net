using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESBaseEffectClip</c> that answers the effect behind it from its
/// <c>create_track_element</c> override.
/// </summary>
/// <remarks>
/// The child a clip extracts there is a <em>core</em> child, because the clip
/// stamps it with its own asset (<c>ges-clip.c:2785-2789</c>, <c>:1697-1700</c>) —
/// which is what distinguishes it from the same effect added with
/// <c>AddTopEffect</c>. <c>GESBaseEffectClip</c> refuses a time effect as a child
/// (<c>ges-base-effect-clip.c:57-79</c>), so the effect answered here is an
/// ordinary one.
/// </remarks>
internal sealed class ProbeEffectClip : GES.BaseEffectClip, IManagedSubclass<ProbeEffectClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesEffectClip";

    /// <summary>The asset id of the effect the override extracts.</summary>
    internal const string ChildDescription = "video videobalance";

    private static readonly SubclassType Definition = DefineSubclass<ProbeEffectClip>(
        GTypeName,
        null,
        CreateTrackElementOverride);

    private ProbeEffectClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets the child the <c>create_track_element</c> override answered last.</summary>
    internal ProbeEffect? AnsweredChild { get; private set; }

    /// <summary>Builds a clip out of an asset for its own type.</summary>
    /// <returns>The new clip, which has an asset.</returns>
    /// <remarks>
    /// A <see langword="null"/> id is legal here and nowhere below: this is not a
    /// <c>GESEffectClip</c>, whose asset hashes its id without checking it
    /// (<c>ges-asset.c:752-766</c>).
    /// </remarks>
    internal static ProbeEffectClip New()
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<ProbeEffectClip>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeEffectClip CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override GES.TrackElement? OnCreateTrackElement(GES.TrackType type)
    {
        if (type != GES.TrackType.Video)
        {
            return null;
        }

        ProbeEffect child = ProbeEffect.NewFromDescription(ChildDescription);
        AnsweredChild = child;
        return child;
    }
}
