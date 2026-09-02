using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.Audio;

/// <summary>
/// Decides how far the playout pointer of an audio sink is to be moved, from
/// the two clock times the sink reports.
/// </summary>
/// <param name="sink">The sink the times were read on.</param>
/// <param name="etime">
/// The time of the element clock of the sink, or
/// <see cref="Gst.ClockTime.None"/> on a discontinuity.
/// </param>
/// <param name="itime">
/// The time of the internal audio clock, with the calibration of that clock
/// already applied, or <see cref="Gst.ClockTime.None"/> on a discontinuity.
/// </param>
/// <param name="discontReason">
/// Why the sink reports a discontinuity, or
/// <see cref="Gst.Audio.AudioBaseSinkDiscontReason.NoDiscont"/> for the
/// ordinary call.
/// </param>
/// <returns>
/// The skew to request, in nanoseconds: how far the playout pointer is to be
/// moved, and <c>0</c> to leave it where it is. The value is ignored on a
/// discontinuity, where the C caller has no storage to write it into.
/// </returns>
/// <remarks>
/// <para>
/// This is <c>GstAudioBaseSinkCustomSlavingCallback</c>, whose
/// <c>requested_skew</c> is an out parameter and not a value: the C caller
/// passes the address of a local initialised to <c>0</c> and reads it back to
/// move the pointer (<c>gstaudiobasesink.c:1290-1294</c> and <c>:1302</c>).
/// The binding therefore spells it as the return of the handler, which is why
/// this delegate is written by hand and why the one that shipped in 1.28.5,
/// <see cref="Gst.Audio.AudioBaseSinkCustomSlavingCallback"/>, is obsolete.
/// </para>
/// <para>
/// The difference between <paramref name="etime"/> and
/// <paramref name="itime"/> is the skew of the two clocks. A skew of 0 means
/// they are perfectly in sync; <c>itime &gt; etime</c> means the external clock
/// is going slower and <c>itime &lt; etime</c> that it is going faster than the
/// internal one.
/// </para>
/// <para>
/// On a discontinuity both times are <see cref="Gst.ClockTime.None"/> and
/// <paramref name="discontReason"/> is not
/// <see cref="Gst.Audio.AudioBaseSinkDiscontReason.NoDiscont"/>. That is the
/// point at which a custom slaving algorithm resets whatever state it keeps.
/// </para>
/// <para>
/// <b>The handler must not touch the sink.</b> The ordinary call runs on the
/// streaming thread with no lock of the sink held, but a discontinuity is also
/// reported from the ring buffer thread with <c>GST_OBJECT_LOCK</c> held
/// (<c>gstaudiobasesink.c:797-800</c>), so reading a property or asking for the
/// state of <paramref name="sink"/> deadlocks. Read the two times, decide, and
/// answer.
/// </para>
/// <para>
/// An exception that leaves the handler does not cross the native frame: it is
/// reported through <see cref="Gst.Interop.ExceptionTrap"/> and no skew is
/// requested.
/// </para>
/// </remarks>
public delegate long AudioBaseSinkCustomSlavingHandler(
    Gst.Audio.AudioBaseSink sink,
    Gst.ClockTime etime,
    Gst.ClockTime itime,
    Gst.Audio.AudioBaseSinkDiscontReason discontReason);

/// <summary>
/// The shape the custom slaving callback shipped with in 1.28.5, which never
/// worked.
/// </summary>
/// <param name="sink">The sink the times were read on.</param>
/// <param name="etime">The time of the element clock of the sink.</param>
/// <param name="itime">The time of the internal audio clock.</param>
/// <param name="requestedSkew">
/// What the shipped trampoline handed over: the value of the
/// <c>GstClockTimeDiff*</c> the C caller passes, which is an address and never
/// a skew. The bridge that keeps this delegate alive passes <c>0</c> here.
/// </param>
/// <param name="discontReason">Why the sink reports a discontinuity, if it does.</param>
/// <remarks>
/// The C caller passes the address of the storage the skew is to be written
/// into, and the gir types the parameter as the bare <c>gint64</c> it points
/// at, so a handler could neither read a skew nor request one: it was handed a
/// pointer value on the ordinary call and <c>0</c> on a discontinuity, where
/// the C caller passes <c>NULL</c>. Use
/// <see cref="Gst.Audio.AudioBaseSinkCustomSlavingHandler"/>, whose return is
/// the skew.
/// </remarks>
[Obsolete("The requested skew of this delegate was the address the library passes and never a skew; use " +
    "AudioBaseSinkCustomSlavingHandler, whose return is the skew. It will be removed in 1.30.", error: false)]
public delegate void AudioBaseSinkCustomSlavingCallback(
    Gst.Audio.AudioBaseSink sink,
    Gst.ClockTime etime,
    Gst.ClockTime itime,
    long requestedSkew,
    Gst.Audio.AudioBaseSinkDiscontReason discontReason);

public unsafe partial class AudioBaseSink
{
    /// <summary>
    /// Installs the callback that decides the skew while the
    /// <c>slave-method</c> property is
    /// <see cref="Gst.Audio.AudioBaseSinkSlaveMethod.Custom"/>.
    /// </summary>
    /// <param name="handler">The handler to install.</param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_audio_base_sink_set_custom_slaving_callback</c>. The
    /// handler is invoked while the sink plays samples and the slave method is
    /// the custom one; see
    /// <see cref="Gst.Audio.AudioBaseSinkCustomSlavingHandler"/> for the
    /// threads it runs on and for what it may do there.
    /// </para>
    /// <para>
    /// <b>Install once per sink.</b> The C function overwrites the callback,
    /// its state and the notification that releases that state without running
    /// the notification that is already there
    /// (<c>gstaudiobasesink.c:761-765</c>); only the disposal of the sink runs
    /// it (<c>:315-316</c>). The trio is read without a lock while the sink
    /// plays (<c>:1292-1294</c>), so a second call leaks the state of the first
    /// and can race an invocation that is already under way. Keep one handler
    /// per sink and branch inside it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="handler"/> is <see langword="null"/>. Use
    /// <see cref="ClearCustomSlavingCallback"/> to clear the slot.
    /// </exception>
    public void SetCustomSlavingCallback(Gst.Audio.AudioBaseSinkCustomSlavingHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        nint instanceHandle = Handle;
        Gst.Interop.CallbackHandle callbackState = Gst.Interop.CallbackHandle.Alloc(handler);
        AudioBaseSinkNative.SetCustomSlavingCallback(
            instanceHandle,
            CustomSlavingTrampoline,
            callbackState.UserData,
            (nint)Gst.Interop.CallbackHandle.DestroyNotify);
        GC.KeepAlive(this);
    }

    /// <summary>
    /// The shape this call shipped with in 1.28.5, which could never request a
    /// skew.
    /// </summary>
    /// <param name="callback">The callback to install.</param>
    /// <remarks>
    /// The callback is wrapped in a handler that invokes it and answers
    /// <c>0</c>, so no skew is ever requested — which is what the shipped
    /// member did as well, the C caller having read back the local it
    /// initialised to <c>0</c> and the trampoline having written nothing. The
    /// behaviour is <em>not</em> identical: the shipped trampoline handed the
    /// callback the address of that local as its <c>requestedSkew</c>, while
    /// the bridge passes <c>0</c>. Use
    /// <see cref="SetCustomSlavingCallback(AudioBaseSinkCustomSlavingHandler)"/>
    /// instead.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="callback"/> is <see langword="null"/>.
    /// </exception>
    [Obsolete("This overload could never request a skew and was handed the library's pointer as its " +
        "requestedSkew; use the overload that takes an AudioBaseSinkCustomSlavingHandler. It will be " +
        "removed in 1.30.", error: false)]
    public void SetCustomSlavingCallback(Gst.Audio.AudioBaseSinkCustomSlavingCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        SetCustomSlavingCallback(Adapt(callback));
    }

    /// <summary>
    /// Clears the custom slaving callback of the sink, which makes it behave as
    /// if <see cref="Gst.Audio.AudioBaseSinkSlaveMethod.None"/> were in use.
    /// </summary>
    /// <remarks>
    /// This passes <c>NULL</c> for the callback, its state and the
    /// notification, which is what the C documentation describes
    /// (<c>gstaudiobasesink.c:748-750</c>). The state of the handler that was
    /// installed is <em>never</em> released: the C function overwrites all
    /// three slots without running the notification it replaces
    /// (<c>:761-765</c>), so the notification slot is <c>NULL</c> afterwards
    /// and the disposal of the sink, which runs whatever notification it finds
    /// there (<c>:315-316</c>), runs nothing. The handle installed by
    /// <see cref="SetCustomSlavingCallback(AudioBaseSinkCustomSlavingHandler)"/>
    /// stays alive for the life of the process.
    /// </remarks>
    public void ClearCustomSlavingCallback()
    {
        nint instanceHandle = Handle;
        AudioBaseSinkNative.SetCustomSlavingCallback(instanceHandle, 0, 0, 0);
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Wraps the delegate that shipped in 1.28.5 into the handler the binding
    /// installs.
    /// </summary>
    /// <param name="callback">The callback to wrap.</param>
    /// <returns>A handler that invokes it and requests no skew.</returns>
    /// <remarks>
    /// This is its own member so that the bridge can be exercised without the
    /// sink: the tests allocate a state of their own around the handler this
    /// answers and call the trampoline through its address.
    /// </remarks>
    [Obsolete("The delegate this adapts is obsolete; the member exists only to keep it working. It will " +
        "be removed in 1.30.", error: false)]
    internal static Gst.Audio.AudioBaseSinkCustomSlavingHandler Adapt(
        Gst.Audio.AudioBaseSinkCustomSlavingCallback callback) =>
        (sink, etime, itime, discontReason) =>
        {
            callback(sink, etime, itime, 0, discontReason);
            return 0;
        };

    /// <summary>
    /// The address of the native entry point of
    /// <see cref="Gst.Audio.AudioBaseSinkCustomSlavingHandler"/>.
    /// </summary>
    internal static nint CustomSlavingTrampoline =>
        (nint)(delegate* unmanaged[Cdecl]<nint, ulong, ulong, long*, int, nint, void>)&InvokeCustomSlaving;

    /// <summary>
    /// The native entry point of
    /// <see cref="Gst.Audio.AudioBaseSinkCustomSlavingHandler"/>.
    /// </summary>
    /// <param name="sink">The sink the times were read on.</param>
    /// <param name="etime">The time of the element clock of the sink.</param>
    /// <param name="itime">The time of the internal audio clock.</param>
    /// <param name="requestedSkew">
    /// Where the skew the handler answers is written, or <c>NULL</c> on a
    /// discontinuity.
    /// </param>
    /// <param name="discontReason">Why the sink reports a discontinuity, if it does.</param>
    /// <param name="userData">The state of the handler.</param>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void InvokeCustomSlaving(
        nint sink,
        ulong etime,
        ulong itime,
        long* requestedSkew,
        int discontReason,
        nint userData)
    {
        try
        {
            if (Gst.Interop.CallbackHandle.GetState<Gst.Audio.AudioBaseSinkCustomSlavingHandler>(userData)
                is not { } handler)
            {
                return;
            }

            Gst.Audio.AudioBaseSink sinkValue =
                Gst.GObject.Object.FromNative<Gst.Audio.AudioBaseSink>(sink, Gst.Interop.Transfer.None)
                ?? throw new InvalidOperationException("GstAudioBaseSinkCustomSlavingCallback passed no sink.");

            long skew = handler(
                sinkValue,
                new Gst.ClockTime(etime),
                new Gst.ClockTime(itime),
                (Gst.Audio.AudioBaseSinkDiscontReason)discontReason);

            // The pointer is NULL on the discontinuity path, where the C caller
            // has no storage to read a skew back out of and ignores whatever
            // the handler decided.
            if (requestedSkew is not null)
            {
                *requestedSkew = skew;
            }
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
        }
    }
}
