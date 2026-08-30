namespace Gst;

public abstract unsafe partial class Clock
{
    /// <summary>
    /// Registers a callback on the given #GstClockID @id with the given
    /// function and user_data. When passing a #GstClockID with an invalid
    /// time to this function, the callback will be called immediately
    /// with  a time set to %GST_CLOCK_TIME_NONE. The callback will
    /// be called when the time of @id has been reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The callback @func can be invoked from any thread, either provided by the
    /// core or from a streaming thread. The application should be prepared for this.
    /// </para>
    /// <para>
    /// <b>A refusal that the entry never saw releases the state of the callback
    /// here.</b> <c>gst_clock_id_wait_async</c> assigns the callback, its state
    /// and the destroy notification onto the entry immediately before it
    /// dispatches to the clock, so the exits in front of that assignment are the
    /// ones nothing native releases the state on:
    /// <see cref="Gst.ClockReturn.Badtime"/> when the time of the entry is
    /// invalid, which runs the callback once and returns without ever having
    /// seen the destroy notification, <see cref="Gst.ClockReturn.Unsupported"/>
    /// when the clock cannot wait asynchronously, and
    /// <see cref="Gst.ClockReturn.Error"/> when the entry lost its clock. This
    /// member releases the state on those, and the callback is then no longer
    /// reachable from native code either. Every result of the dispatch itself
    /// leaves the state with the entry, which releases it when it expires, is
    /// unscheduled or is released: <see cref="Gst.ClockReturn.Ok"/>, but also
    /// <see cref="Gst.ClockReturn.Unscheduled"/> for an entry that was
    /// unscheduled beforehand and <see cref="Gst.ClockReturn.Error"/> for a
    /// clock that could not start its waiting thread, where a release here would
    /// be a second one. The two kinds of <see cref="Gst.ClockReturn.Error"/> are
    /// told apart by the clock of the entry, which this member holds a reference
    /// to across the call: an entry keeps nothing but a weak reference to its
    /// clock, so only an entry whose clock is already gone can take the exit in
    /// front of the assignment.
    /// </para>
    /// </remarks>
    /// <param name="id">The <c>id</c> argument.</param>
    /// <param name="func">The callback function</param>
    /// <returns>the result of the non blocking wait.</returns>
    public static Gst.ClockReturn IdWaitAsync(nint id, Gst.ClockCallback func)
    {
        ArgumentNullException.ThrowIfNull(func);

        // A reference on the clock of the entry, held across the call so that
        // the weak reference the entry keeps cannot be cleared while it runs:
        // with the clock alive, the exit that answers Error in front of the
        // assignment is out of reach, and an Error is therefore one the entry
        // has already taken the state over on. It stays a bare handle on
        // purpose. Wrappers are interned, so wrapping it would hand back the
        // wrapper the caller already holds, and releasing that would dispose
        // the clock of the caller instead of this one reference.
        nint clockHandle = ClockNative.IdGetClock(id);
        try
        {
            Gst.Interop.CallbackHandle funcState = Gst.Interop.CallbackHandle.Alloc(func);
            int nativeResult = ClockNative.IdWaitAsync(id, Gst.ClockCallbackTrampoline.Pointer, funcState.UserData, (nint)Gst.Interop.CallbackHandle.DestroyNotify);
            Gst.ClockReturn result = (Gst.ClockReturn)nativeResult;
            if (result is Gst.ClockReturn.Badtime or Gst.ClockReturn.Unsupported
                || (result is Gst.ClockReturn.Error && clockHandle == 0))
            {
                funcState.Free();
            }

            return result;
        }
        finally
        {
            if (clockHandle != 0)
            {
                Gst.Interop.GObjectNative.ObjectUnref(clockHandle);
            }
        }
    }
}
