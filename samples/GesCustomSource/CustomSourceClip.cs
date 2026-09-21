// A managed GESSourceClip that builds its own video child, which is what the
// timeline of this sample is made of.
using Gst.GObject;

/// <summary>
/// A managed <c>GESSourceClip</c> that builds a child per track type the way the
/// editing services demand: an asset for the <c>GType</c> of the managed source
/// and <see cref="GES.Asset.Extract{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// A child built with <c>new</c> instead would have no asset: it never gets an
/// <c>nleobject</c>, the layer removes it from the clip again, and splitting or
/// pasting the clip aborts the process. The asset is the contract.
/// </para>
/// <para>
/// It also takes over <c>ungroup</c>, which <c>GES.Container</c> declares and no
/// allowlisted class of its own: the override is declared against this clip
/// base, because that is where a managed type may stand.
/// </para>
/// </remarks>
internal sealed class CustomSourceClip : GES.SourceClip, IManagedSubclass<CustomSourceClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpSampleGesSourceClip";

    private static readonly SubclassType Definition = DefineSubclass<CustomSourceClip>(
        GTypeName,
        null,
        CreateTrackElementOverride,
        UngroupOverride);

    private CustomSourceClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets the video child the <c>create_track_element</c> override answered.</summary>
    internal CustomVideoSource? AnsweredChild { get; private set; }

    /// <summary>Gets the audio child the <c>create_track_element</c> override answered.</summary>
    internal CustomAudioSource? AnsweredAudioChild { get; private set; }

    /// <summary>Gets what the <c>ungroup</c> override was asked, or none.</summary>
    /// <remarks>
    /// Only what the override saw and answered is kept, as text: the containers
    /// themselves belong to whoever asked for the split and are disposed there,
    /// so a stash of the wrappers would be a stash of disposed ones.
    /// </remarks>
    internal string? UngroupStory { get; private set; }

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
        // not an error. The timeline below carries one track of each type, so
        // this is asked twice and answers a managed source both times.
        GType childType = type switch
        {
            GES.TrackType.Video => CustomVideoSource.Registration.GType,
            GES.TrackType.Audio => CustomAudioSource.Registration.GType,
            _ => default,
        };

        if (childType.Value == 0)
        {
            return null;
        }

        GES.Asset asset = GES.Asset.Request(childType, null)
            ?? throw new InvalidOperationException("The source asset could not be requested.");

        // Extract owns the only reference to the child until ges_container_add
        // takes one of its own, so it is not disposed here.
        if (type == GES.TrackType.Video)
        {
            CustomVideoSource video = asset.Extract<CustomVideoSource>();
            AnsweredChild = video;
            return video;
        }

        CustomAudioSource audio = asset.Extract<CustomAudioSource>();
        AnsweredAudioChild = audio;
        return audio;
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<GES.Container> OnUngroup(bool recursive)
    {
        int before = GetChildren(false).Count;

        // The implementation below a managed clip is a native one by
        // construction, and for a clip that is GESClip::ungroup: it answers
        // this clip together with one new clip per further track type of its
        // children. The list it hands back is owned by whoever asked for the
        // split, so nothing of it is kept here.
        IReadOnlyList<GES.Container> parts = ChainUpUngroup(recursive);

        UngroupStory = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"recursive={recursive}, {before} children before, {parts.Count} containers answered");

        return parts;
    }
}
