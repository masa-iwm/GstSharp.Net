using System.Runtime.CompilerServices;
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
/// <para>
/// The slot behind it is hand bound here as well. A managed clip that declares
/// <see cref="UngroupOverride"/> answers the split itself, and the forward
/// member below reads the runtime class slot of the container to tell the two
/// ownerships apart: the childless quirk belongs to <c>GESClip::ungroup</c>
/// (ges-clip.c:2150-2152), so a container whose slot is the managed one never
/// carries it, while a managed clip that leaves the slot alone inherits the
/// pointer of <c>GESClip</c> and with it the quirk.
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
    /// owned exactly once. The member settles it itself rather than leaving it
    /// to the wrapper: a fresh wrapper does sink a handle that arrives floating
    /// (Core/GObject/Object.cs:215-219), but it sinks it without releasing the
    /// reference that came with the transfer, which would leave a copy of the
    /// split owned twice.
    /// </para>
    /// </remarks>
    public System.Collections.Generic.IReadOnlyList<GES.Container> Ungroup(bool recursive) =>
        UngroupThrough(recursive, chainUp: false);

    /// <summary>
    /// Gets the declaration of <c>GESContainer.ungroup</c>, for a subclass that
    /// overrides <see cref="OnUngroup"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slot answers a <c>GList</c> the caller takes over together with one
    /// reference per container, a shape no generated one expresses, so the
    /// declaration, the override, its chain-up and its trampoline are written
    /// here.
    /// </para>
    /// <para>
    /// It is declared with <see cref="GES.Clip.DefineSubclass{TSelf}(string, Action{Gst.GObject.ObjectClassConfig}?, Gst.GObject.VfuncOverride[])"/>
    /// and the other allowlisted clip bases. <see cref="GES.Container"/> itself
    /// cannot be registered against: it is not on the allowlist and carries no
    /// registration of its own. What the registration checks is that the parent
    /// type derives from the class the slot belongs to, which every clip does.
    /// </para>
    /// </remarks>
    public static Gst.GObject.VfuncOverride UngroupOverride { get; } = new(
        &GetGType,
        GES.ContainerClassRaw.UngroupOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, int, nint>)&UngroupTrampoline);

    /// <summary>
    /// Splits this container into the containers its children make up.
    /// </summary>
    /// <param name="recursive">
    /// Whether to split the children of the children as well. No implementation
    /// in GES 1.28 reads the flag: neither <c>GESClip::ungroup</c>
    /// (ges-clip.c:2136-2205) nor <c>GESGroup::ungroup</c>
    /// (ges-group.c:425-452) looks at it.
    /// </param>
    /// <returns>
    /// The containers the split produced. The list is consumed: the caller
    /// receives a new list and one added reference per container, this one
    /// included when it is in the answer. Every wrapper keeps the reference it
    /// owns, stays usable, and may be answered again. An empty list is answered
    /// as <c>NULL</c>. A null entry is not allowed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This container is not consumed, whatever the gir says: no implementation
    /// of the slot releases the reference of its caller, and the transfer the
    /// annotation describes is the timeline dropping its own reference to an
    /// emptied group (ges-timeline.c:1421).
    /// </para>
    /// <para>
    /// The default chains up to the implementation below, which for a clip is
    /// <c>GESClip::ungroup</c>: it answers this clip together with one new clip
    /// per further track type of its children, and a clip without children
    /// answers itself alone.
    /// </para>
    /// <para>
    /// An exception that leaves this override is reported through the exception
    /// trap and the slot answers an empty list.
    /// </para>
    /// </remarks>
    protected virtual System.Collections.Generic.IReadOnlyList<GES.Container> OnUngroup(bool recursive) =>
        ChainUpUngroup(recursive);

    /// <summary>Runs the implementation of <c>ungroup</c> below the managed override.</summary>
    /// <param name="recursive">Whether to split the children of the children as well.</param>
    /// <returns>
    /// The containers the implementation below answered, which the caller owns
    /// and disposes.
    /// </returns>
    /// <remarks>
    /// The implementation below a managed clip is a native one by construction,
    /// so the childless quirk of <c>GESClip::ungroup</c> applies: a clip without
    /// children is handed back without a reference of its own
    /// (ges-clip.c:2150-2152), which this adopts as a borrow rather than as a
    /// transfer.
    /// </remarks>
    /// <exception cref="System.ObjectDisposedException">
    /// This wrapper was disposed.
    /// </exception>
    protected System.Collections.Generic.IReadOnlyList<GES.Container> ChainUpUngroup(bool recursive) =>
        UngroupThrough(recursive, chainUp: true);

    /// <summary>
    /// Runs one split and adopts what it answered.
    /// </summary>
    /// <param name="recursive">Whether to split the children of the children as well.</param>
    /// <param name="chainUp">
    /// <see langword="true"/> to call the implementation below the managed
    /// override, <see langword="false"/> to call <c>ges_container_ungroup</c>.
    /// </param>
    /// <returns>The containers the split produced.</returns>
    private System.Collections.Generic.IReadOnlyList<GES.Container> UngroupThrough(bool recursive, bool chainUp)
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

        // The quirk belongs to the native implementation, not to the shape of
        // the container. A chain-up always reaches a native one - a managed
        // override below a managed override is refused - and the forward call
        // reaches whichever function the runtime class carries, which is the
        // managed trampoline only for a container that declared it. A managed
        // clip that left the slot alone inherits the pointer of GESClip and
        // answers the childless case the way GESClip does.
        bool answeredByNativeClip = chainUp || RuntimeUngroupSlot(self) != UngroupOverride.Function;

        nint head = chainUp
            ? ChainUpUngroup(self, recursive ? 1 : 0)
            : GesContainerUngroup(self, recursive ? 1 : 0);

        nint[] items = GListMarshal.CollectAndFreeSpine(head);
        System.Collections.Generic.List<GES.Container> result = new(items.Length);
        foreach (nint item in items)
        {
            if (item == 0)
            {
                continue;
            }

            bool unreferencedSelf = childless && item == self && answeredByNativeClip;
            Transfer transfer = unreferencedSelf ? Transfer.None : Transfer.Full;

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

    /// <summary>
    /// Reads the <c>ungroup</c> slot the class of an instance carries.
    /// </summary>
    /// <param name="instance">The native <c>GESContainer</c>.</param>
    /// <returns>The function the slot holds, which may be <see cref="nint.Zero"/>.</returns>
    /// <remarks>
    /// The first word of a <c>GTypeInstance</c> is its class pointer, so this is
    /// the dispatch the forward call is about to make, rather than a guess from
    /// the managed type of the wrapper.
    /// </remarks>
    private static nint RuntimeUngroupSlot(nint instance) =>
        *(nint*)(*(nint*)instance + GES.ContainerClassRaw.UngroupOffset);

    /// <summary>
    /// Returns the class the registration captured, which is the one an override
    /// chains up through.
    /// </summary>
    /// <param name="instance">The native <c>GESContainer</c>.</param>
    /// <returns>The parent class of the managed subclass, read as a container class.</returns>
    /// <remarks>
    /// A <c>GESClipClass</c> starts with a <c>GESContainerClass</c>, so the
    /// captured parent class of any clip below this one reads as the mirror of
    /// this class.
    /// </remarks>
    private static GES.ContainerClassRaw* ContainerParentClassOf(nint instance) =>
        (GES.ContainerClassRaw*)Gst.GObject.SubclassRegistry.DescriptorFor(instance).ParentClass;

    private static nint ChainUpUngroup(nint container, int recursive)
    {
        delegate* unmanaged[Cdecl]<nint, int, nint> slot =
            (delegate* unmanaged[Cdecl]<nint, int, nint>)ContainerParentClassOf(container)->Ungroup;

        // A class without the slot answers nothing, which is what
        // ges_container_ungroup reads out of one (ges-container.c:960-963):
        // NULL is the empty list, not a failure.
        if (slot is null)
        {
            return nint.Zero;
        }

        return slot(container, recursive);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nint UngroupTrampoline(nint container, int recursive)
    {
        try
        {
            if (Gst.GObject.Object.TryGetOrFabricate(container) is not Container managed)
            {
                return ChainUpUngroup(container, recursive);
            }

            return GListMarshal.BuildOwnedObjectList(
                managed.OnUngroup(recursive != 0),
                "OnUngroup",
                "ungroup");
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            return default;
        }
    }

    /// <summary>The <c>ges_container_ungroup</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_container_ungroup")]
    private static partial nint GesContainerUngroup(nint container, int recursive);
}
