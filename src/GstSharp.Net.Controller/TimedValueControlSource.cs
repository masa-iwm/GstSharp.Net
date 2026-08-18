using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Controller;

/// <summary>
/// A control source that is described by a set of timestamp and value pairs,
/// the <c>GstTimedValueControlSource</c> of <c>libgstcontroller</c>.
/// </summary>
/// <remarks>
/// <para>
/// The pairs are the control points, and what a concrete source does with them
/// is its own business: <see cref="InterpolationControlSource"/> interpolates
/// between them, <see cref="TriggerControlSource"/> answers at them and nowhere
/// else. Setting the same timestamp twice replaces the value there.
/// </para>
/// <para>
/// A control source on its own changes nothing. It is attached to a property of
/// a <see cref="Gst.Object"/> through a control binding —
/// <see cref="DirectControlBinding.New(Gst.Object, string, Gst.ControlSource)"/> — and the property then
/// follows it
/// whenever <c>gst_object_sync_values</c> runs, which for an element inside a
/// running pipeline is once per buffer.
/// </para>
/// <para>
/// <b>This wrapper is a GObject wrapper</b> and follows the rule of
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md">docs/ownership.md</see>:
/// it is interned, it is the same instance on every lookup, and normally it is
/// not disposed. A control binding holds the source for as long as it lives.
/// </para>
/// <para>
/// <b>It derives from the generated <see cref="Gst.ControlSource"/></b>, whose
/// <c>(nint, Transfer)</c> constructor is <c>protected</c> for exactly this. The
/// managed hierarchy therefore follows the native one: this is a
/// <see cref="Gst.ControlSource"/> and a <see cref="Gst.Object"/>, their members
/// are inherited rather than bound again, and generated API that takes a control
/// source — <c>GES.TrackElement.SetControlSource</c> is the one that matters —
/// accepts a source from this module. It costs no
/// <c>InternalsVisibleTo</c>: the constructor is the whole of what is open.
/// </para>
/// </remarks>
public abstract unsafe partial class TimedValueControlSource : Gst.ControlSource
{
    /// <summary>
    /// Wraps a native <c>GstTimedValueControlSource</c>.
    /// </summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <remarks>
    /// The obligations of <see cref="Gst.ControlSource(nint, Transfer)"/> apply:
    /// this is reached from the factory of the type registry and from
    /// <see cref="Gst.GObject.Object.FromNative(nint, Transfer)"/>, never from
    /// application code.
    /// </remarks>
    protected TimedValueControlSource(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets the number of control points that are set.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public int Count
    {
        get
        {
            int count = GstTimedValueControlSourceGetCount(Handle);
            GC.KeepAlive(this);
            return count;
        }
    }

    /// <summary>
    /// Sets the value of one control point.
    /// </summary>
    /// <param name="timestamp">The time the value belongs to.</param>
    /// <param name="value">
    /// The value, which the control binding maps onto the range of the bound
    /// property.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the control point could not be set, which
    /// for this source means an invalid timestamp.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public bool Set(Gst.ClockTime timestamp, double value)
    {
        int result = GstTimedValueControlSourceSet(Handle, timestamp.Nanoseconds, value);
        GC.KeepAlive(this);
        return result != 0;
    }

    /// <summary>
    /// Removes the control point at one timestamp.
    /// </summary>
    /// <param name="timestamp">The time to clear.</param>
    /// <returns>
    /// <see langword="false"/> when there was no control point at that
    /// timestamp.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public bool Unset(Gst.ClockTime timestamp)
    {
        int result = GstTimedValueControlSourceUnset(Handle, timestamp.Nanoseconds);
        GC.KeepAlive(this);
        return result != 0;
    }

    /// <summary>
    /// Removes every control point.
    /// </summary>
    /// <remarks>
    /// A source without control points answers nothing, so a property bound to
    /// it keeps whatever value it had at the last synchronisation.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void UnsetAll()
    {
        GstTimedValueControlSourceUnsetAll(Handle);
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Asks the control source for its value at one timestamp.
    /// </summary>
    /// <param name="timestamp">The time to evaluate at.</param>
    /// <param name="value">The value of the source at that time.</param>
    /// <returns>
    /// <see langword="false"/> when the source has no value there, which is
    /// what a timestamp before the first control point gives.
    /// </returns>
    /// <remarks>
    /// This is <c>gst_control_source_get_value</c>, which lives in the core
    /// GStreamer library rather than in <c>libgstcontroller</c>, and the module
    /// does not bind it: it is inherited from
    /// <see cref="Gst.ControlSource.ControlSourceGetValue"/>, which is what
    /// deriving from the generated wrapper buys. The name here is the one this
    /// module has always used. The value is the raw one, before the control
    /// binding maps it onto the range of a property.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public bool TryGetValue(Gst.ClockTime timestamp, out double value) =>
        ControlSourceGetValue(timestamp, out value);

    /// <summary>
    /// Returns the <c>GType</c> that GObject registered
    /// <c>GstTimedValueControlSource</c> under.
    /// </summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("GstController", EntryPoint = "gst_timed_value_control_source_get_type")]
    internal static partial nuint GetGType();

    /// <summary>
    /// Creates the wrapper of a native instance, for the type registry.
    /// </summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) => new Concrete(handle, transfer);

    [LibraryImport("GstController", EntryPoint = "gst_timed_value_control_source_set")]
    private static partial int GstTimedValueControlSourceSet(nint self, ulong timestamp, double value);

    [LibraryImport("GstController", EntryPoint = "gst_timed_value_control_source_unset")]
    private static partial int GstTimedValueControlSourceUnset(nint self, ulong timestamp);

    [LibraryImport("GstController", EntryPoint = "gst_timed_value_control_source_unset_all")]
    private static partial void GstTimedValueControlSourceUnsetAll(nint self);

    [LibraryImport("GstController", EntryPoint = "gst_timed_value_control_source_get_count")]
    private static partial int GstTimedValueControlSourceGetCount(nint self);

    /// <summary>
    /// The wrapper of a native type that derives from
    /// <c>GstTimedValueControlSource</c> and has no wrapper class of its own —
    /// one that a later release of GStreamer, or another library, adds.
    /// </summary>
    private sealed class Concrete : TimedValueControlSource
    {
        internal Concrete(nint handle, Transfer transfer)
            : base(handle, transfer)
        {
        }
    }
}
