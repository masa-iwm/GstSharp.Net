using System.Runtime.InteropServices;

namespace Gst.Base;

/// <summary>
/// The preroll entry point of <c>GstBaseSink</c> and the lock it has to be
/// called under, for a sink that renders on a thread of its own.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_base_sink_do_preroll</c> is bound by hand because the planner does
/// not marshal a bare <c>GstMiniObject*</c>. The lock is a macro in C
/// (<c>GST_BASE_SINK_PREROLL_LOCK</c>, <c>gstbasesink.h:50-51</c>) over the
/// <c>preroll_lock</c> field, and <c>flushing</c> is a field the header marks
/// private; both are read at the offsets of the generated mirror
/// <see cref="BaseSinkOwnFieldsRaw"/>.
/// </para>
/// <para>
/// This is the one place where managed code takes a GLib lock of the library,
/// and it is meant for a thread the subclass owns, outside every override: see
/// "Prerolling from a thread of your own" in docs/subclassing.md.
/// </para>
/// </remarks>
public unsafe partial class BaseSink
{
    /// <summary>
    /// Gets the offset of <c>preroll_lock</c> from the first own field of the
    /// instance.
    /// </summary>
    internal static int PrerollLockOffset { get; } = MeasurePrerollLock();

    /// <summary>
    /// Gets the offset of <c>flushing</c> from the first own field of the
    /// instance.
    /// </summary>
    internal static int FlushingOffset { get; } = MeasureFlushing();

    /// <summary>
    /// Gets a value indicating whether the sink is flushing: its pad is being
    /// deactivated or a flush is in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value is a plain read of the <c>flushing</c> field, which the
    /// library writes under PREROLL_LOCK (<c>gstbasesink.c:4667</c>,
    /// <c>:3776</c>, <c>:2442</c>, <c>:1760</c>; the <c>with LOCK</c> of
    /// <c>gstbasesink.h:108</c> is stale). It is only meaningful between
    /// <see cref="PrerollLock"/> and <see cref="PrerollUnlock"/>.
    /// </para>
    /// <para>
    /// A thread of the subclass reads it before every <see cref="DoPreroll"/>:
    /// <c>gst_base_sink_do_preroll</c> has no check of its own
    /// (<c>gstbasesink.c:2479-2564</c>), so a call made after the flush was
    /// set waits for a signal that never comes. Both callers in the library
    /// that own the lock read it first (<c>gstbasesink.c:3776</c>,
    /// <c>gstaudiobasesink.c:2326</c>).
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    protected bool IsFlushing
    {
        get
        {
            bool flushing = *(int*)FieldAddress(FlushingOffset) != 0;
            GC.KeepAlive(this);
            return flushing;
        }
    }

    /// <summary>
    /// Prerolls on <paramref name="obj"/> if the sink needs to, and then waits
    /// until the state of the element changes.
    /// </summary>
    /// <param name="obj">
    /// The buffer, buffer list or event that caused the preroll, or
    /// <see langword="null"/>. A buffer, or the first buffer of a list, is
    /// handed to <see cref="OnPrepare"/> and <see cref="OnPreroll"/> first; an
    /// event or <see langword="null"/> only commits the state and waits
    /// (<c>gstbasesink.c:2484-2504</c>). The call borrows it and keeps no
    /// reference.
    /// </param>
    /// <returns>
    /// <see cref="Gst.FlowReturn.Ok"/> when the preroll completed, or at once
    /// when the sink did not need one, and processing can continue. Any other
    /// value, <see cref="Gst.FlowReturn.Flushing"/> above all, is what the
    /// caller hands on: a render override returns it, a thread of its own stops.
    /// </returns>
    /// <remarks>
    /// <para>
    /// PREROLL_LOCK has to be held on entry (<c>gstbasesink.c:2474</c>), the
    /// same as for <see cref="Wait"/>, <see cref="WaitPreroll"/> and
    /// <see cref="WaitClock"/>. The library already holds it inside
    /// <see cref="OnRender"/>, <see cref="OnRenderList"/>,
    /// <see cref="OnPrepare"/>, <see cref="OnPreroll"/>,
    /// <see cref="OnWaitEvent"/>, a serialized <see cref="OnEvent"/>,
    /// <see cref="OnUnlockStop"/>, <see cref="OnSetCaps"/> and
    /// <see cref="OnGetTimes"/>, so an override calls this directly. A thread
    /// the subclass owns wraps it in <see cref="PrerollLock"/> and
    /// <see cref="PrerollUnlock"/> after a look at <see cref="IsFlushing"/>.
    /// </para>
    /// <para>
    /// The call may release the lock and take it again while it waits
    /// (<c>gstbasesink.c:1751-1757</c>), so nothing the lock guards is held
    /// still across it. A render override that waited on the clock calls it
    /// to catch a PLAYING to PAUSED change made while the lock was released
    /// (<c>gstbasesink.c:2412-2421</c>).
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper, or <paramref name="obj"/>, was disposed.</exception>
    public Gst.FlowReturn DoPreroll(Gst.MiniObject? obj)
    {
        int result = GstBaseSinkDoPreroll(Handle, obj is null ? 0 : obj.Handle);
        GC.KeepAlive(this);
        GC.KeepAlive(obj);
        return (Gst.FlowReturn)result;
    }

    /// <summary>
    /// Takes PREROLL_LOCK, the <c>GST_BASE_SINK_PREROLL_LOCK</c> macro of
    /// <c>gstbasesink.h:50</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is for a thread the subclass owns and for nothing else: every
    /// override already runs either under the lock or where the library does
    /// not want it taken, and docs/subclassing.md keeps the rule that an
    /// override does not reach for a lock. The lock is not held in
    /// <see cref="OnUnlock"/>, a non-serialized <see cref="OnEvent"/>,
    /// <see cref="OnStart"/>, <see cref="OnStop"/>, <see cref="OnQuery"/>,
    /// <see cref="OnActivatePull"/> or <see cref="OnProposeAllocation"/>, and
    /// it is held in every override listed on <see cref="DoPreroll"/>.
    /// </para>
    /// <para>
    /// The lock is a non-recursive <c>GMutex</c>: taking it where it is
    /// already held deadlocks the thread and nothing detects it
    /// (<c>gthread.c:1275-1278</c>). It belongs to the thread that took it, so
    /// the same thread releases it, with no <see langword="await"/> in between
    /// (<c>gthread.c:1293-1294</c>). The order is STREAM_LOCK, then
    /// PREROLL_LOCK, then the object lock (<c>gstbasesink.c:4089</c>,
    /// <c>:1684</c>, <c>:2343</c>, <c>:2274</c>): never take it while the
    /// object lock is held.
    /// </para>
    /// <para>
    /// The library wakes a thread blocked under this lock from its own
    /// <see cref="OnUnlock"/> call, made before it takes the lock
    /// (<c>gstbasesink.c:4406-4413</c>, <c>:4660-4671</c>,
    /// <c>:5794-5803</c>); an override of it must wake whatever the thread
    /// waits on outside <see cref="DoPreroll"/>. Pad deactivation sets the
    /// flush before it calls <see cref="OnActivatePull"/> with
    /// <see langword="false"/> (<c>gstbasesink.c:4936-4938</c>), so a thread
    /// parked in <see cref="DoPreroll"/> returns
    /// <see cref="Gst.FlowReturn.Flushing"/> before the override joins it.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    protected void PrerollLock()
    {
        GMutexLock(FieldAddress(PrerollLockOffset));
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Releases PREROLL_LOCK, the <c>GST_BASE_SINK_PREROLL_UNLOCK</c> macro of
    /// <c>gstbasesink.h:51</c>.
    /// </summary>
    /// <remarks>
    /// Only the thread that took the lock with <see cref="PrerollLock"/>
    /// releases it; a release by any other thread is undefined behaviour in
    /// GLib (<c>gthread.c:1293-1294</c>).
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    protected void PrerollUnlock()
    {
        GMutexUnlock(FieldAddress(PrerollLockOffset));
        GC.KeepAlive(this);
    }

    /// <summary>Measures where <c>preroll_lock</c> sits in the mirror.</summary>
    /// <returns>The offset from the first own field.</returns>
    private static int MeasurePrerollLock()
    {
        BaseSinkOwnFieldsRaw probe = default;
        return Gst.GObject.InstanceLayout.OffsetOf(ref probe, ref probe.PrerollLock);
    }

    /// <summary>Measures where <c>flushing</c> sits in the mirror.</summary>
    /// <returns>The offset from the first own field.</returns>
    private static int MeasureFlushing()
    {
        BaseSinkOwnFieldsRaw probe = default;
        return Gst.GObject.InstanceLayout.OffsetOf(ref probe, ref probe.Flushing);
    }

    /// <summary>The address of an own field of this instance.</summary>
    /// <param name="offset">The offset of the field from the first own field.</param>
    /// <returns>The native address.</returns>
    private nint FieldAddress(int offset) => Handle + BaseSinkOwnFieldsRaw.OwnOffset + offset;

    /// <summary>The <c>gst_base_sink_do_preroll</c> entry point.</summary>
    [LibraryImport("GstBase", EntryPoint = "gst_base_sink_do_preroll")]
    private static partial int GstBaseSinkDoPreroll(nint sink, nint obj);

    /// <summary>The <c>g_mutex_lock</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_mutex_lock")]
    private static partial void GMutexLock(nint mutex);

    /// <summary>The <c>g_mutex_unlock</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_mutex_unlock")]
    private static partial void GMutexUnlock(nint mutex);
}
