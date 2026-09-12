using System.Runtime.InteropServices;
using Gst.Interop;

namespace GES;

/// <content>
/// <c>ges_container_ungroup</c>, whose ownership no generated shape expresses.
/// </content>
/// <remarks>
/// <para>
/// The gir marks the instance <c>transfer-ownership="full"</c>, and the C never
/// releases the reference the caller holds: nothing in
/// <c>ges_container_ungroup</c> (ges-container.c:950-966), in
/// <c>GESClip::ungroup</c> (ges-clip.c:2136-2205) or in
/// <c>GESGroup::ungroup</c> (ges-group.c:425-452) unreferences the container.
/// What the annotation describes is the timeline dropping its own reference to
/// a group whose last child has left (ges-timeline.c:1421), which is a
/// reference this binding never held.
/// </para>
/// <para>
/// The list is not uniformly owned either. Every element of it carries one
/// added reference (ges-clip.c:2075, ges-group.c:439) except on the one early
/// return of a childless clip, which hands the clip itself back with none
/// (ges-clip.c:2150-2152). A generated shape can state one transfer for the
/// whole list and not two, so the member is written here.
/// </para>
/// </remarks>
public abstract unsafe partial class Container
{
    /// <summary>
    /// Splits the container into the containers its children make up.
    /// </summary>
    /// <param name="recursive">
    /// Whether to split the children of the children as well. GStreamer 1.28
    /// reads the flag nowhere: neither <c>GESClip::ungroup</c>
    /// (ges-clip.c:2136-2205) nor <c>GESGroup::ungroup</c>
    /// (ges-group.c:425-452) looks at it, so the value has no effect. It is
    /// taken all the same, because the silence is upstream behaviour rather
    /// than a gap of the binding.
    /// </param>
    /// <returns>
    /// The containers the split produced, which the caller owns and disposes.
    /// A clip answers itself together with one new clip per further track type
    /// of its children; a group answers its children and is left empty; an
    /// empty group answers an empty list.
    /// </returns>
    /// <exception cref="System.ObjectDisposedException">
    /// This wrapper was disposed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This container is not consumed. The wrapper keeps its reference and
    /// stays usable, which is what the split of a group needs: the timeline
    /// releases the reference it held once the last child has left
    /// (ges-timeline.c:1421), and a caller that owned nothing would be left
    /// with a dangling pointer.
    /// </para>
    /// <para>
    /// The original clip is always in the answer of a clip. The copies beside
    /// it are made with <c>ges_timeline_element_copy</c>, which answers a
    /// floating instance, and GES only sinks one into a layer when the clip
    /// being split has one (ges-clip.c:2170-2177): a clip outside a layer
    /// therefore answers copies that are in no layer, and each wrapper here
    /// settles that floating reference so that every element of the answer is
    /// owned exactly once.
    /// </para>
    /// </remarks>
    public System.Collections.Generic.IReadOnlyList<GES.Container> Ungroup(bool recursive)
    {
        nint self = Handle;

        // Which of the two ownerships the answer carries cannot be read off
        // its shape: a clip whose children are all of one track type answers
        // itself alone WITH an added reference, and a childless clip answers
        // itself alone WITHOUT one. The children are what tells the two apart,
        // so they are counted before the split. The list is the one
        // ges_container_get_children hands out, whose elements carry a
        // reference each, and nothing here needs a wrapper of a child.
        nint[] childHandles = GListMarshal.CollectAndFreeSpine(GesContainerGetChildren(self, 0));
        foreach (nint childHandle in childHandles)
        {
            GObjectNative.ObjectUnref(childHandle);
        }

        bool childless = childHandles.Length == 0;

        nint[] items = GListMarshal.CollectAndFreeSpine(GesContainerUngroup(self, recursive ? 1 : 0));
        System.Collections.Generic.List<GES.Container> result = new(items.Length);
        foreach (nint item in items)
        {
            if (item == 0)
            {
                continue;
            }

            Transfer transfer = childless && item == self ? Transfer.None : Transfer.Full;

            // A copy that was never added to a layer is still floating and
            // carries the added reference on top of the floating one. Sinking
            // it turns the floating reference into an owned one and leaves two,
            // so the added one is released here: the wrapper below then adopts
            // the single reference that is left.
            if (transfer == Transfer.Full && GObjectNative.ObjectIsFloating(item) != 0)
            {
                GObjectNative.ObjectRefSink(item);
                GObjectNative.ObjectUnref(item);
            }

            if (Gst.GObject.Object.FromNative<GES.Container>(item, transfer) is { } adopted)
            {
                result.Add(adopted);
            }
        }

        System.GC.KeepAlive(this);
        return result;
    }

    /// <summary>The <c>ges_container_ungroup</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_container_ungroup")]
    private static partial nint GesContainerUngroup(nint container, int recursive);
}
