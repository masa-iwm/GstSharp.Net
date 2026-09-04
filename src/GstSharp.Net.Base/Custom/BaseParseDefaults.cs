using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Base;

/// <summary>
/// What <c>GstBaseParse</c> does for a slot its own class leaves NULL, reached
/// from the chain-up of a managed parser.
/// </summary>
/// <remarks>
/// Both members stand for behaviour that is not a function below the override
/// but code inside the caller of the slot, which a chain-up has nothing to
/// reach: <c>gst_base_parse_sink_query</c> answers a caps query itself when
/// <c>get_sink_caps</c> is NULL, and <c>gst_base_parse_push_frame</c> marks the
/// frame for clipping itself when <c>pre_push_frame</c> is. Reproducing them
/// here is what makes a chain-up of those two slots mean what C means.
/// </remarks>
internal static unsafe partial class BaseParseDefaults
{
    /// <summary>
    /// Answers the caps of the sink pad template, intersected with the filter
    /// when there is one, the way <c>gst_base_parse_sink_query</c> does for a
    /// NULL <c>get_sink_caps</c> slot.
    /// </summary>
    /// <param name="parse">The native <c>GstBaseParse</c>.</param>
    /// <param name="filter">The filter of the caps query, or <c>0</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    /// <remarks>
    /// gstbaseparse.c:1671-1688. The filter comes first in the intersection and
    /// the mode is <see cref="Gst.CapsIntersectMode.First"/>, which is what
    /// keeps the preference order of the filter.
    /// </remarks>
    internal static nint GetSinkCaps(nint parse, nint filter)
    {
        nint pad = GetStaticPad(parse, "sink");
        if (pad == nint.Zero)
        {
            // The pad is made in the instance initialiser of every parser, so
            // this is unreachable; answering empty caps rather than nothing
            // keeps the promise the slot makes to its caller all the same.
            return Gst.GstNative.CapsNewEmpty();
        }

        nint template = GetPadTemplateCaps(pad);
        GObjectNative.ObjectUnref(pad);

        if (filter == nint.Zero)
        {
            return template;
        }

        nint caps = IntersectFull(filter, template, (int)Gst.CapsIntersectMode.First);
        Gst.GstNative.MiniObjectUnref(template);
        return caps;
    }

    /// <summary>
    /// Marks a frame for the segment clipping the base class performs, the way
    /// <c>gst_base_parse_push_frame</c> does for a NULL <c>pre_push_frame</c>
    /// slot.
    /// </summary>
    /// <param name="frame">The native <c>GstBaseParseFrame</c>.</param>
    /// <remarks>gstbaseparse.c:2609.</remarks>
    internal static void MarkFrameForClipping(nint frame) =>
        ((BaseParseFrameRaw*)frame)->Flags |= (uint)Gst.Base.BaseParseFrameFlags.Clip;

    /// <summary>Reads a static pad of an element.</summary>
    /// <param name="element">The native <c>GstElement</c>.</param>
    /// <param name="name">The name of the pad.</param>
    /// <returns>The pad, referenced for the caller, or <c>0</c>.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_element_get_static_pad", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetStaticPad(nint element, string name);

    /// <summary>Reads the caps of the template a pad was made from.</summary>
    /// <param name="pad">The native <c>GstPad</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_pad_get_pad_template_caps")]
    private static partial nint GetPadTemplateCaps(nint pad);

    /// <summary>Intersects two caps.</summary>
    /// <param name="caps1">The caps whose order is kept.</param>
    /// <param name="caps2">The caps to intersect with.</param>
    /// <param name="mode">The <c>GstCapsIntersectMode</c>.</param>
    /// <returns>The intersection, owned by the caller.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_caps_intersect_full")]
    private static partial nint IntersectFull(nint caps1, nint caps2, int mode);
}
