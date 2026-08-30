using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst;

/// <summary>The function prototype of the callback.</summary>
/// <param name="clock">The clock that triggered the callback</param>
/// <param name="time">The time it was triggered</param>
/// <param name="id">The #GstClockID that expired</param>
/// <returns>%TRUE or %FALSE (currently unused)</returns>
/// <remarks>
/// The delegate and its trampoline are written by hand for one reason only:
/// <c>gst_clock_id_wait_async</c> is the sole consumer of the callback in the
/// gir and it is on the <c>skip</c> list, so the generator emits neither any
/// more. Both are copies of what it emitted, so that the public surface of
/// 1.28 is unchanged.
/// </remarks>
public delegate bool ClockCallback(Gst.Clock clock, Gst.ClockTime time, nint id);

/// <summary>The native entry point of <see cref="Gst.ClockCallback"/>.</summary>
internal static unsafe class ClockCallbackTrampoline
{
    /// <summary>Gets the address that is handed to native code.</summary>
    internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<nint, ulong, nint, nint, int>)&Invoke;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Invoke(nint clock, ulong time, nint id, nint userData)
    {
        try
        {
            if (Gst.Interop.CallbackHandle.GetState<Gst.ClockCallback>(userData) is not { } callback)
            {
                return default;
            }

            Gst.Clock clockValue = Gst.GObject.Object.FromNative<Gst.Clock>(clock, Gst.Interop.Transfer.None)
                ?? throw new InvalidOperationException("GstClockCallback passed no clock.");
            return callback(clockValue, new Gst.ClockTime(time), id) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return default;
        }
    }
}

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
    /// <b>A result other than <see cref="Gst.ClockReturn.Ok"/> releases the
    /// state of the callback here.</b> <c>gst_clock_id_wait_async</c> assigns
    /// the callback, its state and the destroy notification onto the entry
    /// immediately before it dispatches to the clock, and every other exit is
    /// taken before that assignment: <see cref="Gst.ClockReturn.Error"/> when
    /// the entry lost its clock, <see cref="Gst.ClockReturn.Unsupported"/> when
    /// the clock cannot wait asynchronously, and
    /// <see cref="Gst.ClockReturn.Badtime"/> when the time of the entry is
    /// invalid, which runs the callback once and returns without ever having
    /// seen the destroy notification. Nothing native releases the state on
    /// those three, so this member does, and the callback is then no longer
    /// reachable from native code either. On
    /// <see cref="Gst.ClockReturn.Ok"/> the entry owns the state and releases
    /// it when it expires, is unscheduled or is released.
    /// </para>
    /// </remarks>
    /// <param name="id">The <c>id</c> argument.</param>
    /// <param name="func">The callback function</param>
    /// <returns>the result of the non blocking wait.</returns>
    public static Gst.ClockReturn IdWaitAsync(nint id, Gst.ClockCallback func)
    {
        ArgumentNullException.ThrowIfNull(func);
        Gst.Interop.CallbackHandle funcState = Gst.Interop.CallbackHandle.Alloc(func);
        int nativeResult = ClockNative.IdWaitAsync(id, Gst.ClockCallbackTrampoline.Pointer, funcState.UserData, (nint)Gst.Interop.CallbackHandle.DestroyNotify);
        Gst.ClockReturn result = (Gst.ClockReturn)nativeResult;
        if (result != Gst.ClockReturn.Ok)
        {
            funcState.Free();
        }

        return result;
    }
}
