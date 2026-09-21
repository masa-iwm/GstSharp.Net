using System.Runtime.InteropServices;

namespace GES;

/// <content>
/// The precondition of the two calls that can add a top effect to a clip.
/// </content>
/// <remarks>
/// <para>
/// <c>ges_container_add</c> dispatches to <c>GESClip::_add_child</c> without
/// looking at the asset of the child (<c>ges-container.c:733-736</c>), and the
/// branch that takes a top effect reads the bin description of that asset
/// through <c>ges_asset_get_id (NULL)</c> and hands the result to
/// <c>strstr</c> (<c>ges-clip.c:1786-1790</c>). An effect built with
/// <c>new</c> rather than extracted from an asset therefore takes the process
/// down. The removal path guards the same read (<c>:2018-2022</c>); the add
/// path does not, on 1.24 and 1.28 alike.
/// </para>
/// </remarks>
public abstract unsafe partial class Clip
{
    /// <summary>
    /// The offset of the <c>can_add_effects</c> class field, which is data
    /// rather than a slot and has no accessor of its own.
    /// </summary>
    private static readonly int CanAddEffectsOffset = MeasureCanAddEffectsOffset();

    /// <summary>
    /// Refuses an effect that has no asset before a container takes it, because
    /// the clip that would take it as a top effect reads the id of the asset
    /// that is not there.
    /// </summary>
    /// <param name="container">The container the child is being added to.</param>
    /// <param name="child">The child being added.</param>
    /// <remarks>
    /// <para>
    /// The condition is the crashing one and no more of it: the child is a
    /// <c>GESEffect</c> (the branch is guarded by <c>GES_IS_EFFECT</c>, so a
    /// direct <c>GESBaseEffect</c> subtype never reaches the read), the
    /// container is a clip whose class sets <c>can_add_effects</c> (any other
    /// class warns and refuses the child instead, <c>ges-clip.c:1869-1884</c>),
    /// the child is not a core child of the clip that made it (a core child is
    /// one with a creator asset, <c>ges-track-element.c:2154-2158</c>, and it
    /// takes the branch above), and it has no asset at all.
    /// </para>
    /// <para>
    /// Two of the shapes it now refuses were not crashing ones: an asset-less
    /// <c>GESEffect</c> that already has a parent (<c>ges-container.c:716</c>,
    /// a critical and FALSE) or that carries another timeline
    /// (<c>ges-clip.c:1655-1661</c>, a warning and FALSE) left C before the
    /// read, so those two calls answered false and now throw. Both need a
    /// <c>SetParent</c> or <c>SetTimeline</c> written by hand on an orphan
    /// effect, and both were already failing calls, so no call that worked
    /// throws now.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="child"/> is a <c>GESEffect</c> with no asset and
    /// <paramref name="container"/> would add it as a top effect.
    /// </exception>
    internal static void ThrowIfEffectHasNoAsset(
        GES.Container container,
        GES.TimelineElement child)
    {
        if (!child.NativeType.IsA(new Gst.GObject.GType(GES.Effect.GetGType()))
            || !container.NativeType.IsA(new Gst.GObject.GType(GES.Clip.GetGType()))
            || !CanAddEffects(container.Handle)
            || (child is GES.TrackElement element && element.IsCore())
            || GesExtractableGetAsset(child.Handle) != nint.Zero)
        {
            System.GC.KeepAlive(container);
            System.GC.KeepAlive(child);
            return;
        }

        System.GC.KeepAlive(container);
        System.GC.KeepAlive(child);

        throw new InvalidOperationException(
            "A " + child.NativeType.Name + " built with new has no asset, and a clip that "
            + "takes it as a top effect reads the bin description of that asset, which "
            + "takes the process down. An effect is built through its asset instead: "
            + "GES.Asset.Request(type.GType, \"video <description>\")!.Extract<T>().");
    }

    /// <summary>
    /// Reads the <c>can_add_effects</c> field of the class of an instance.
    /// </summary>
    /// <param name="instance">The native <c>GESClip</c>.</param>
    /// <returns>Whether the class takes effects that it did not create itself.</returns>
    /// <remarks>
    /// The first word of a <c>GTypeInstance</c> is its class pointer, so this
    /// reads the flag of the class the add is about to dispatch through, the
    /// way <c>GES_CLIP_CLASS_CAN_ADD_EFFECTS</c> does (<c>ges-clip.h:42</c>).
    /// The layout it reads through is the mirror the class-struct layout tests
    /// pin.
    /// </remarks>
    private static bool CanAddEffects(nint instance) =>
        *(int*)(*(nint*)instance + CanAddEffectsOffset) != 0;

    /// <summary>
    /// Measures the offset of <c>can_add_effects</c> in the class struct.
    /// </summary>
    /// <returns>The offset in bytes.</returns>
    /// <remarks>
    /// The field is data rather than a slot, so it is measured with the
    /// instance-layout helper: <c>ClassSlot.OffsetOf</c> takes a
    /// <c>ref nint</c> and a <c>gboolean</c> is not one.
    /// </remarks>
    private static int MeasureCanAddEffectsOffset()
    {
        GES.ClipClassRaw probe = default;
        return Gst.GObject.InstanceLayout.OffsetOf(ref probe, ref probe.CanAddEffects);
    }

    /// <summary>The <c>ges_extractable_get_asset</c> entry point.</summary>
    /// <remarks>
    /// The generated extension answers a wrapper, which would take a reference
    /// and leave the caller of a precondition with one to dispose. The question
    /// here is only whether there is an asset at all.
    /// </remarks>
    [LibraryImport("GES", EntryPoint = "ges_extractable_get_asset")]
    private static partial nint GesExtractableGetAsset(nint self);
}
