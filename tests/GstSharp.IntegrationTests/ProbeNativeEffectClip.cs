using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESEffectClip</c> that declares nothing, so the child it is given
/// is the native <c>GESEffect</c> the inherited <c>create_track_element</c> builds
/// out of the description its asset carries.
/// </summary>
/// <remarks>
/// The id of such an asset is <c>"audio &lt;description&gt; ||video &lt;description&gt;"</c>,
/// either half of which may be absent (<c>ges-effect-clip.c:68-105</c>) and
/// neither of which may be preceded by a space: the half after the bars is split
/// on its first space (<c>ges-effect-asset.c:390-397</c>), so a space there costs
/// the half its type and it is dropped. An empty
/// id is a clip with no description and no child; a <see langword="null"/> id
/// takes the process down, because the asset hashes the id without checking it
/// (<c>ges-asset.c:752-766</c>), so no test here passes one.
/// </remarks>
internal sealed class ProbeNativeEffectClip : GES.EffectClip, IManagedSubclass<ProbeNativeEffectClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesNativeEffectClip";

    private static readonly SubclassType Definition = DefineSubclass<ProbeNativeEffectClip>(
        GTypeName,
        null);

    private ProbeNativeEffectClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Builds a clip out of an asset whose id carries the descriptions.</summary>
    /// <param name="id">The asset id, which is never <see langword="null"/>.</param>
    /// <returns>The new clip, which has an asset.</returns>
    internal static ProbeNativeEffectClip New(string id)
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, id)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<ProbeNativeEffectClip>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeNativeEffectClip CreateWrapper(SubclassCtorArgs args) => new(args);
}
