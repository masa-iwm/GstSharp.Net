using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESSourceClip</c> that answers a child it built itself, which
/// is the mistake the child contract exists to rule out.
/// </summary>
/// <remarks>
/// An element that no asset extracted never had <c>ges_extractable_set_asset</c>
/// called on it, so it has no <c>nleobject</c> and no track will take it
/// (<c>ges-track-element.c:293-295</c>, <c>ges-track.c:1233-1238</c>). What the
/// library then does is observable and is what the test asserts: adding the
/// clip fails, the child that was just created is removed again, and the clip
/// leaves the layer (<c>ges-layer.c:781-808</c>).
/// </remarks>
internal sealed class ProbeNewChildSourceClip : GES.SourceClip, IManagedSubclass<ProbeNewChildSourceClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesNewChildSourceClip";

    private static readonly SubclassType Definition = DefineSubclass<ProbeNewChildSourceClip>(
        GTypeName,
        null,
        CreateTrackElementOverride);

    private ProbeNewChildSourceClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets the child the <c>create_track_element</c> override answered last.</summary>
    internal ProbeVideoSource? AnsweredChild { get; private set; }

    /// <summary>Builds a clip out of an asset for its own type.</summary>
    /// <returns>The new clip, which has an asset of its own.</returns>
    internal static ProbeNewChildSourceClip New()
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<ProbeNewChildSourceClip>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeNewChildSourceClip CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override GES.TrackElement? OnCreateTrackElement(GES.TrackType type)
    {
        if (type != GES.TrackType.Video)
        {
            return null;
        }

        ProbeVideoSource child = ProbeVideoSource.New();
        AnsweredChild = child;
        return child;
    }
}
