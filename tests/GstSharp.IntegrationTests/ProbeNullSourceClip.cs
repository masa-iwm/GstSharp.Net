using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESSourceClip</c> whose video child is a
/// <see cref="ProbeNullVideoSource"/>, so that the guard on a refused
/// <c>create_source</c> can be observed on a clip a layer really takes.
/// </summary>
internal sealed class ProbeNullSourceClip : GES.SourceClip, IManagedSubclass<ProbeNullSourceClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesNullSourceClip";

    private static readonly SubclassType Definition = DefineSubclass<ProbeNullSourceClip>(
        GTypeName,
        null,
        CreateTrackElementOverride);

    private ProbeNullSourceClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Builds a clip out of an asset for its own type.</summary>
    /// <returns>The new clip, which has an asset.</returns>
    internal static ProbeNullSourceClip New()
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<ProbeNullSourceClip>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeNullSourceClip CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override GES.TrackElement? OnCreateTrackElement(GES.TrackType type)
    {
        if (type != GES.TrackType.Video)
        {
            return null;
        }

        GES.Asset asset = GES.Asset.Request(ProbeNullVideoSource.Registration.GType, null)
            ?? throw new InvalidOperationException("The source asset could not be requested.");

        return asset.Extract<ProbeNullVideoSource>();
    }
}
