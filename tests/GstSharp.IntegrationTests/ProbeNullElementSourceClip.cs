using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESSourceClip</c> whose video child is a
/// <see cref="ProbeNullElementVideoSource"/>, so that the guard on a refused
/// <c>create_element</c> can be observed on a clip a layer really takes.
/// </summary>
internal sealed class ProbeNullElementSourceClip : GES.SourceClip, IManagedSubclass<ProbeNullElementSourceClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesNullElementSourceClip";

    private static readonly SubclassType Definition = DefineSubclass<ProbeNullElementSourceClip>(
        GTypeName,
        null,
        CreateTrackElementOverride);

    private ProbeNullElementSourceClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Builds a clip out of an asset for its own type.</summary>
    /// <returns>The new clip, which has an asset.</returns>
    internal static ProbeNullElementSourceClip New()
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<ProbeNullElementSourceClip>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeNullElementSourceClip CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override GES.TrackElement? OnCreateTrackElement(GES.TrackType type)
    {
        if (type != GES.TrackType.Video)
        {
            return null;
        }

        GES.Asset asset = GES.Asset.Request(ProbeNullElementVideoSource.Registration.GType, null)
            ?? throw new InvalidOperationException("The source asset could not be requested.");

        return asset.Extract<ProbeNullElementVideoSource>();
    }
}
