using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESSourceClip</c> that builds its video child the way the
/// editing services demand: an asset for the <c>GType</c> of the managed source
/// and <see cref="GES.Asset.Extract{T}"/>.
/// </summary>
/// <remarks>
/// It installs one property of its own so that the copy the library makes when
/// a clip is split can be observed. <c>ges_timeline_element_copy</c> copies
/// every readable and writable property of the class, the managed ones
/// included, after the copy has been extracted — so the write arrives at the
/// wrapper of the copy (<c>ges-timeline-element.c:1672-1690</c>).
/// </remarks>
internal sealed class ProbeSourceClip : GES.SourceClip, IManagedSubclass<ProbeSourceClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesSourceClip";

    /// <summary>The identifier of the <c>probe-tag</c> property.</summary>
    internal const uint TagId = 1;

    private static readonly ParamSpecString TagSpec = ParamSpecString.New(
        "probe-tag",
        "Probe tag",
        "A string the copy of the clip is expected to carry",
        null,
        ParamFlags.Readable | ParamFlags.Writable);

    private static readonly SubclassType Definition = DefineSubclass<ProbeSourceClip>(
        GTypeName,
        ConfigureClass,
        CreateTrackElementOverride,
        SetPropertyOverride,
        GetPropertyOverride);

    private static int _wrappersBuilt;

    private string? _tag;

    private ProbeSourceClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets how many wrappers were fabricated since the last reset.</summary>
    internal static int WrappersBuilt => Volatile.Read(ref _wrappersBuilt);

    /// <summary>Gets the child the <c>create_track_element</c> override answered last.</summary>
    internal ProbeVideoSource? AnsweredChild { get; private set; }

    /// <summary>Gets what the last write of <c>probe-tag</c> stored.</summary>
    internal string? Tag => _tag;

    /// <summary>Builds a clip out of an asset for its own type.</summary>
    /// <returns>The new clip, which has an asset.</returns>
    internal static ProbeSourceClip New()
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<ProbeSourceClip>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeSourceClip CreateWrapper(SubclassCtorArgs args)
    {
        ProbeSourceClip wrapper = new(args);
        _ = Interlocked.Increment(ref _wrappersBuilt);
        return wrapper;
    }

    /// <inheritdoc/>
    protected override GES.TrackElement? OnCreateTrackElement(GES.TrackType type)
    {
        if (type != GES.TrackType.Video)
        {
            return null;
        }

        GES.Asset asset = GES.Asset.Request(ProbeVideoSource.Registration.GType, null)
            ?? throw new InvalidOperationException("The source asset could not be requested.");

        ProbeVideoSource child = asset.Extract<ProbeVideoSource>();
        AnsweredChild = child;
        return child;
    }

    /// <inheritdoc/>
    protected override void OnSetProperty(uint propertyId, ValueView value, ParamSpec pspec)
    {
        if (propertyId == TagId)
        {
            _tag = value.GetString();
            return;
        }

        base.OnSetProperty(propertyId, value, pspec);
    }

    /// <inheritdoc/>
    protected override void OnGetProperty(uint propertyId, ValueRef value, ParamSpec pspec)
    {
        if (propertyId == TagId)
        {
            value.SetString(_tag);
            return;
        }

        base.OnGetProperty(propertyId, value, pspec);
    }

    private static void ConfigureClass(ObjectClassConfig config) =>
        config.InstallProperty(TagId, TagSpec);
}
