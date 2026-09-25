using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GES;

/// <summary>
/// A function <see cref="GES.BaseEffect.SetTimeTranslationFuncs"/> installs to
/// translate a time of one side of a time effect into the other side.
/// </summary>
/// <param name="effect">The effect the translation is for.</param>
/// <param name="time">The time to translate, never <see cref="Gst.ClockTime.None"/>.</param>
/// <param name="timePropertyValues">
/// The values of the child properties registered with
/// <see cref="GES.BaseEffect.RegisterTimeProperty"/>, keyed by the names given
/// there. These are the candidate values the caller wants the translation for,
/// which need not be the ones currently set (<c>ges-base-effect.h:66-72</c>).
/// Every value is a copy valid only for the length of the call: the binding
/// disposes each one after the function returns, whether it returned or
/// threw. A function that wants to keep one calls
/// <see cref="Gst.GObject.Value.Copy"/> and disposes that copy itself; it must
/// never dispose a value it was handed, because the struct shares its payload
/// with the one the binding disposes.
/// </param>
/// <returns>
/// The translated time, or <see cref="Gst.ClockTime.None"/> when it cannot be
/// translated. <see cref="Gst.ClockTime.None"/> makes
/// <see cref="GES.Clip.GetTimelineTimeFromInternalTime"/> and
/// <see cref="GES.Clip.GetInternalTimeFromTimelineTime"/> answer that the time
/// cannot be converted (<c>ges-clip.c:4136</c>, <c>:4280</c>), and means that
/// the track sets no limit in the computation of the duration limit of a clip
/// (<c>ges-clip.c:299-301</c>, <c>:478</c>).
/// </returns>
/// <remarks>
/// <para>
/// An exception that leaves the function is reported through
/// <see cref="Gst.Interop.ExceptionTrap"/> and answered with
/// <paramref name="time"/> unchanged, which is what the C does for a direction
/// that has no function.
/// </para>
/// <para>
/// The function should read <paramref name="effect"/> rather than capture the
/// wrapper of the effect: the native effect keeps the function alive until it
/// is disposed, so a captured wrapper keeps itself alive through it until the
/// effect is disposed explicitly.
/// </para>
/// </remarks>
public delegate Gst.ClockTime BaseEffectTimeTranslationFunc(
    GES.BaseEffect effect, Gst.ClockTime time,
    System.Collections.Generic.IReadOnlyDictionary<string, Gst.GObject.Value> timePropertyValues);

/// <content>
/// The time translation of an effect, which the generator cannot emit because
/// its callback is lent a table of <c>GValue</c>s and the two functions share
/// one closure.
/// </content>
public abstract unsafe partial class BaseEffect
{
    /// <summary>
    /// Sets the functions that translate times between the source and the sink
    /// side of this effect, which makes it a time effect.
    /// </summary>
    /// <param name="sourceToSink">
    /// Translates a time of the source side into the sink side, or
    /// <see langword="null"/> for the identity.
    /// </param>
    /// <param name="sinkToSource">
    /// Translates a time of the sink side into the source side, or
    /// <see langword="null"/> for the identity.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the functions were set;
    /// <see langword="false"/> when the effect is already in a clip or its
    /// <see cref="GES.TrackElement.HasInternalSource"/> is
    /// <see langword="true"/>, in which case nothing changes.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>ges_base_effect_set_time_translation_funcs</c>, written by
    /// hand. Call it before the effect is added to a clip, and never on an
    /// effect whose <c>has-internal-source</c> is set: the C refuses both
    /// (<c>ges-base-effect.c:309-311</c>), and this member answers
    /// <see langword="false"/> for them without calling it. A successful call
    /// forbids <c>has-internal-source</c> from then on.
    /// </para>
    /// <para>
    /// Functions set earlier are released when new ones are set and when the
    /// effect is disposed. Passing <see langword="null"/> for both clears the
    /// translation and still forbids <c>has-internal-source</c>; a cleared
    /// effect stays a time effect only if it has registered time properties.
    /// <see cref="RegisterTimeProperty"/> is independent of this call, but it
    /// is what fills the table the functions are handed.
    /// </para>
    /// <para>
    /// Nothing here is safe to use from several threads at once on GES 1.28:
    /// the functions run on whichever thread changes or queries the timeline
    /// (GES declares no thread safety, <c>ges.c:195-196</c>). The time a
    /// function is handed is never <see cref="Gst.ClockTime.None"/>
    /// (<c>ges-base-effect.c:399-400</c>, <c>:418-419</c>); see
    /// <see cref="GES.BaseEffectTimeTranslationFunc"/> for the rest of the
    /// contract.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public bool SetTimeTranslationFuncs(
        GES.BaseEffectTimeTranslationFunc? sourceToSink,
        GES.BaseEffectTimeTranslationFunc? sinkToSource)
    {
        // The two refusals of the C are g_return_val_if_fail checks that log a
        // critical (ges-base-effect.c:309-311), so they are answered here
        // instead. The parent wrapper is interned and may be one the caller
        // holds, so it is not disposed.
        GES.TimelineElement? parent = GetParent();
        if (parent is not null || HasInternalSource())
        {
            System.GC.KeepAlive(this);
            return false;
        }

        // Read before anything is allocated, so that nothing can throw between
        // the allocation of the handle and the call that takes it over.
        nint self = Handle;
        int ret;
        if (sourceToSink is null && sinkToSource is null)
        {
            ret = GesBaseEffectSetTimeTranslationFuncs(self, 0, 0, 0, 0);
        }
        else
        {
            Gst.Interop.CallbackHandle handle =
                Gst.Interop.CallbackHandle.Alloc(new TimeTranslationState(sourceToSink, sinkToSource));
            ret = GesBaseEffectSetTimeTranslationFuncs(
                self,
                sourceToSink is null ? 0 : SourceToSinkTrampoline.Pointer,
                sinkToSource is null ? 0 : SinkToSourceTrampoline.Pointer,
                handle.UserData,
                (nint)Gst.Interop.CallbackHandle.DestroyNotify);
            if (ret == 0)
            {
                // The C refused before it stored the user data
                // (ges-base-effect.c:304-311), so the destroy never runs.
                // The pre-check above answers both refusals first, so this is
                // reached only if the effect changed in between; no test can
                // reach it without bypassing the pre-check.
                handle.Free();
            }
        }

        System.GC.KeepAlive(this);
        return ret != 0;
    }

    /// <summary>Runs one translation function on the table the C lent.</summary>
    /// <param name="function">The function, or <see langword="null"/> for the identity.</param>
    /// <param name="effect">The effect the translation is for.</param>
    /// <param name="time">The time to translate.</param>
    /// <param name="table">The <c>GHashTable</c> of the registered time properties.</param>
    /// <returns>The translated time.</returns>
    private static ulong Translate(GES.BaseEffectTimeTranslationFunc? function, nint effect, ulong time, nint table)
    {
        if (function is null)
        {
            return time;
        }

        GES.BaseEffect effectValue = Gst.GObject.Object.FromNative<GES.BaseEffect>(effect, Gst.Interop.Transfer.None)
            ?? throw new InvalidOperationException("GESBaseEffectTimeTranslationFunc passed no effect.");
        Dictionary<string, Gst.GObject.Value> values = Gst.Interop.HashTableMarshal.ToValueDictionary(table);
        try
        {
            // A read-only view, so that the function cannot take a copy out of
            // the dictionary the loop below disposes.
            return function(
                effectValue,
                new Gst.ClockTime(time),
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, Gst.GObject.Value>(values)).Nanoseconds;
        }
        finally
        {
            foreach (Gst.GObject.Value value in values.Values)
            {
                value.Dispose();
            }
        }
    }

    /// <summary>The state the native effect keeps for its translation functions.</summary>
    /// <remarks>
    /// Only the two functions are kept, never the wrapper of the effect: the
    /// native effect roots this state until it is disposed
    /// (<c>ges-base-effect.c:144-145</c>), and a wrapper held here would keep
    /// the effect alive through itself.
    /// </remarks>
    private sealed class TimeTranslationState
    {
        internal TimeTranslationState(
            GES.BaseEffectTimeTranslationFunc? sourceToSink,
            GES.BaseEffectTimeTranslationFunc? sinkToSource)
        {
            SourceToSink = sourceToSink;
            SinkToSource = sinkToSource;
        }

        /// <summary>Gets the function from the source side to the sink side.</summary>
        internal GES.BaseEffectTimeTranslationFunc? SourceToSink { get; }

        /// <summary>Gets the function from the sink side to the source side.</summary>
        internal GES.BaseEffectTimeTranslationFunc? SinkToSource { get; }
    }

    /// <summary>The native entry point of the source to sink translation.</summary>
    private static class SourceToSinkTrampoline
    {
        /// <summary>Gets the address that is handed to native code.</summary>
        internal static nint Pointer =>
            (nint)(delegate* unmanaged[Cdecl]<nint, ulong, nint, nint, ulong>)&Invoke;

        /// <summary>Translates one time from the source side to the sink side.</summary>
        /// <param name="effect">The effect.</param>
        /// <param name="time">The time to translate.</param>
        /// <param name="table">The table of the registered time properties.</param>
        /// <param name="userData">The <c>GCHandle</c> of the <see cref="TimeTranslationState"/>.</param>
        /// <returns>The translated time.</returns>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static ulong Invoke(nint effect, ulong time, nint table, nint userData)
        {
            try
            {
                TimeTranslationState? state = Gst.Interop.CallbackHandle.GetState<TimeTranslationState>(userData);
                return Translate(state?.SourceToSink, effect, time, table);
            }
            catch (Exception exception)
            {
                Gst.Interop.ExceptionTrap.Report(exception);
                return time;
            }
        }
    }

    /// <summary>The native entry point of the sink to source translation.</summary>
    private static class SinkToSourceTrampoline
    {
        /// <summary>Gets the address that is handed to native code.</summary>
        internal static nint Pointer =>
            (nint)(delegate* unmanaged[Cdecl]<nint, ulong, nint, nint, ulong>)&Invoke;

        /// <summary>Translates one time from the sink side to the source side.</summary>
        /// <param name="effect">The effect.</param>
        /// <param name="time">The time to translate.</param>
        /// <param name="table">The table of the registered time properties.</param>
        /// <param name="userData">The <c>GCHandle</c> of the <see cref="TimeTranslationState"/>.</param>
        /// <returns>The translated time.</returns>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static ulong Invoke(nint effect, ulong time, nint table, nint userData)
        {
            try
            {
                TimeTranslationState? state = Gst.Interop.CallbackHandle.GetState<TimeTranslationState>(userData);
                return Translate(state?.SinkToSource, effect, time, table);
            }
            catch (Exception exception)
            {
                Gst.Interop.ExceptionTrap.Report(exception);
                return time;
            }
        }
    }

    /// <summary>The <c>ges_base_effect_set_time_translation_funcs</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_base_effect_set_time_translation_funcs")]
    private static partial int GesBaseEffectSetTimeTranslationFuncs(nint effect, nint sourceToSink, nint sinkToSource, nint userData, nint destroy);
}
