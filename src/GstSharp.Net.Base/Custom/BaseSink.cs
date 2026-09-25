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
/// (<c>GST_BASE_SINK_PREROLL_LOCK</c>, <c>gstbasesink.h:49-51</c>) over the
/// <c>preroll_lock</c> field, <c>flushing</c> is a field the header marks
/// private, and <c>can_activate_pull</c> has no C accessor; all three are read
/// at the offsets of the generated mirror <see cref="BaseSinkOwnFieldsRaw"/>.
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
    /// Gets the offset of <c>can_activate_pull</c> from the first own field of
    /// the instance.
    /// </summary>
    internal static int CanActivatePullOffset { get; } = MeasureCanActivatePull();

    /// <summary>
    /// Gets or sets a value indicating whether the sink pad may be activated in
    /// pull mode: the <c>can_activate_pull</c> field
    /// (<c>gstbasesink.h:93</c>), which is <see langword="false"/> unless a
    /// subclass sets it (<c>gstbasesink.c:711</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set it in the constructor of the subclass, before the sink pad is
    /// activated: <c>gst_base_sink_pad_activate</c> reads it when it chooses the
    /// scheduling mode and falls back to push mode when it is not set
    /// (<c>gstbasesink.c:4738</c>). A write made after that has no effect on the
    /// activation that already happened.
    /// </para>
    /// <para>
    /// The value is a plain read and write of the field, with no lock.
    /// <c>GstBaseSink</c> reads it only while it activates the sink pad
    /// (<c>gstbasesink.c:711</c>, <c>:4738</c>), and a constructor runs before
    /// anything else holds the instance; <c>gstaudiobasesink.c:887</c> also
    /// reads it, in <c>get_property</c>. The header marks the field
    /// <c>with LOCK</c> (<c>gstbasesink.h:91</c>), but the base class reads it
    /// at activation without taking that lock. <c>GstAudioBaseSink</c> writes the
    /// same field through its <c>can-activate-pull</c> property
    /// (<c>gstaudiobasesink.c:846-847</c>), which is why
    /// <c>AudioBaseSink.CanActivatePull</c> hides this member.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    protected bool CanActivatePull
    {
        get
        {
            bool value = *(int*)FieldAddress(CanActivatePullOffset) != 0;
            GC.KeepAlive(this);
            return value;
        }

        set
        {
            *(int*)FieldAddress(CanActivatePullOffset) = value ? 1 : 0;
            GC.KeepAlive(this);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the sink is flushing or shutting down:
    /// a flush is in progress, its pad is being deactivated or inactive, or
    /// the state is going down to READY (<c>gstbasesink.c:1796</c>,
    /// <c>:1817</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value is a plain read of the <c>flushing</c> field, which the
    /// library reads and writes under PREROLL_LOCK (writes at
    /// <c>gstbasesink.c:4667</c>, <c>:1796</c>, <c>:1817</c>; reads at
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
    /// (<c>gstbasesink.c:2484-2504</c>); a list must not be empty, or the
    /// library aborts (<c>gstbasesink.c:2496</c>). The call borrows it: the
    /// wrapper keeps its reference, and the sink may take one of its own on
    /// the buffer as the last sample (<c>gstbasesink.c:2494</c>,
    /// <c>:2500</c>, <c>:1053</c>), after which the buffer is no longer writable in place.
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
    /// <see cref="OnGetTimes"/> (<c>gstbasesink.c:2153</c>, <c>:3822</c>), so
    /// an override calls this directly. A thread
    /// the subclass owns wraps it in <see cref="PrerollLock"/> and
    /// <see cref="PrerollUnlock"/> after a look at <see cref="IsFlushing"/>.
    /// </para>
    /// <para>
    /// The call may release the lock and take it again while it waits
    /// (<c>gstbasesink.c:1751-1757</c>, <c>:2440</c>; the wait family at
    /// <c>:2379-2383</c>), so nothing the lock guards is held
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
    /// <c>gstbasesink.h:49</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is for a thread the subclass owns and for nothing else: every
    /// override already runs either under the lock or where the library does
    /// not want it taken, and docs/subclassing.md keeps the rule that an
    /// override does not reach for a lock. The lock is not held in
    /// <see cref="OnUnlock"/>, a non-serialized <see cref="OnEvent"/> or
    /// FLUSH_STOP, <see cref="OnStart"/>, <see cref="OnStop"/>,
    /// the <c>query</c> virtual method, which the binding does not expose
    /// (<c>gstbasesink.c:5665</c>), <see cref="OnActivatePull"/>,
    /// <see cref="OnProposeAllocation"/> or an override of
    /// <c>OnChangeState</c>, and it is held in every override listed on
    /// <see cref="DoPreroll"/>.
    /// </para>
    /// <para>
    /// On such a thread the shape is the one of
    /// <c>gstaudiobasesink.c:2296-2346</c>: take the lock, stop if
    /// <see cref="IsFlushing"/>, otherwise call <see cref="DoPreroll"/>, then
    /// release it. The binding has no accessor for the stream lock of a pad,
    /// so a managed thread pulls without STREAM_LOCK, where
    /// <c>gstaudiobasesink.c:2308</c> takes it.
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

    /// <summary>Measures where <c>can_activate_pull</c> sits in the mirror.</summary>
    /// <returns>The offset from the first own field.</returns>
    private static int MeasureCanActivatePull()
    {
        BaseSinkOwnFieldsRaw probe = default;
        return Gst.GObject.InstanceLayout.OffsetOf(ref probe, ref probe.CanActivatePull);
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
