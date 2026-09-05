// A managed GESSourceClip that builds its own video child, which is what the
// timeline of this sample is made of.
using Gst.GObject;

/// <summary>
/// A managed <c>GESSourceClip</c> that builds its video child the way the
/// editing services demand: an asset for the <c>GType</c> of the managed source
/// and <see cref="GES.Asset.Extract{T}"/>.
/// </summary>
/// <remarks>
/// A child built with <c>new</c> instead would have no asset: it never gets an
/// <c>nleobject</c>, the layer removes it from the clip again, and splitting or
/// pasting the clip aborts the process. The asset is the contract.
/// </remarks>
internal sealed class CustomSourceClip : GES.SourceClip, IManagedSubclass<CustomSourceClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpSampleGesSourceClip";

    private static readonly SubclassType Definition = DefineSubclass<CustomSourceClip>(
        GTypeName,
        null,
        CreateTrackElementOverride);

    private CustomSourceClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets the child the <c>create_track_element</c> override answered.</summary>
    internal CustomVideoSource? AnsweredChild { get; private set; }

    /// <summary>Builds a clip out of an asset for its own type.</summary>
    /// <returns>The new clip, which has an asset.</returns>
    internal static CustomSourceClip New()
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<CustomSourceClip>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static CustomSourceClip CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override GES.TrackElement? OnCreateTrackElement(GES.TrackType type)
    {
        // A null answer is a clip with no child of that track type, which is
        // not an error. The timeline below carries a video track only, so this
        // is asked for video alone; the guard is what would keep an audio
        // track from getting a video source.
        if (type != GES.TrackType.Video)
        {
            return null;
        }

        GES.Asset asset = GES.Asset.Request(CustomVideoSource.Registration.GType, null)
            ?? throw new InvalidOperationException("The source asset could not be requested.");

        // Extract owns the only reference to the child until ges_container_add
        // takes one of its own, so it is not disposed here.
        CustomVideoSource child = asset.Extract<CustomVideoSource>();
        AnsweredChild = child;
        return child;
    }
}
