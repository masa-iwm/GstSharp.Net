// A managed GESVideoSource that answers the element behind it, which is the
// child the clip beside it builds.
using Gst.GObject;

/// <summary>
/// A managed <c>GESVideoSource</c> whose <c>create_source</c> override answers
/// the element the source is made of.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in this sample constructs one of these. The editing services build
/// it, through <c>ges_asset_extract</c>, when the clip below is added to a
/// layer, so the wrapper is fabricated for an instance the sample never made
/// and <see cref="CreateWrapper"/> is what says how. See
/// <c>docs/subclassing.md</c> §11.
/// </para>
/// <para>
/// The override answers an element and never <see langword="null"/>. A null
/// answer is a documented C shape, but it leaves the source with no top bin at
/// all, and the process that holds such a source does not survive the teardown
/// of its timeline.
/// </para>
/// </remarks>
internal sealed class CustomVideoSource : GES.VideoSource, IManagedSubclass<CustomVideoSource>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpSampleGesVideoSource";

    private static readonly SubclassType Definition = DefineSubclass<CustomVideoSource>(
        GTypeName,
        null,
        CreateSourceOverride);

    private CustomVideoSource(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the source.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets the factory name of the element the override built.</summary>
    internal string? BuiltElement { get; private set; }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static CustomVideoSource CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override Gst.Element OnCreateSource()
    {
        // The element must have no parent: a failed gst_bin_add releases both
        // the answer and the nlesource (ges-track-element.c:1073-1078). The wrapper keeps the
        // reference it made and the top bin takes one of its own, so the
        // wrapper may be let go of right after the call.
        Gst.Element source = Gst.ElementFactory.Make("videotestsrc", null)
            ?? throw new InvalidOperationException(
                "videotestsrc is not installed. Install the base plugins of GStreamer.");

        BuiltElement = source.Name;
        return source;
    }
}
