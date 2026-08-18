using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.Controller;

/// <summary>
/// A control source that answers only at the timestamps it was given, the
/// <c>GstTriggerControlSource</c> of <c>libgstcontroller</c>.
/// </summary>
/// <remarks>
/// <para>
/// The control points are set the way they are on every
/// <see cref="TimedValueControlSource"/>, but nothing is filled in between
/// them: a timestamp that does not hit a control point has no value at all, so
/// <see cref="TimedValueControlSource.TryGetValue"/> answers
/// <see langword="false"/> there and a property bound to the source keeps
/// whatever it had. That is what makes this the source for one-shot events —
/// a note, a flash, a trigger — rather than for a curve.
/// </para>
/// <code>
/// TriggerControlSource source = TriggerControlSource.New();
/// source.Set(ClockTime.FromMilliseconds(500), 1.0);
/// source.Tolerance = (long)ClockTime.FromMilliseconds(10).Nanoseconds;
///
/// element.AddControlBinding(DirectControlBinding.NewAbsolute(element, "volume", source));
/// </code>
/// <para>
/// <b>Hitting a timestamp exactly is rare</b> — a pipeline evaluates its
/// bindings once per buffer, at whatever timestamps the buffers happen to
/// carry — which is what <see cref="Tolerance"/> is for.
/// </para>
/// </remarks>
public sealed unsafe partial class TriggerControlSource : TimedValueControlSource
{
    /// <summary>
    /// Wraps a native <c>GstTriggerControlSource</c>.
    /// </summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal TriggerControlSource(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets or sets how far a timestamp may be off a control point, in
    /// nanoseconds, and still be answered by it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is zero, which means that only the exact timestamps of the
    /// control points have a value. A tolerance of <em>t</em> widens each of
    /// them to a window that reaches <em>t</em> after its own control point and
    /// <em>t</em> before the next one — a timestamp in the second half of that
    /// window is answered with the value of the control point that is about to
    /// come, so a trigger fires early rather than late.
    /// </para>
    /// <para>
    /// A timestamp before the <em>first</em> control point is never answered,
    /// however close it is: the source has no control point at or before it to
    /// widen, so the tolerance has nothing to work from.
    /// </para>
    /// <para>
    /// This is the <c>tolerance</c> property, a <c>gint64</c>, read and written
    /// through the property surface of <see cref="Gst.GObject.Object"/> as the
    /// properties of <see cref="LFOControlSource"/> are.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The tolerance is negative.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public long Tolerance
    {
        get => GetProperty<long>("tolerance");

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            SetProperty("tolerance", value);
        }
    }

    /// <summary>
    /// Creates a new trigger control source.
    /// </summary>
    /// <returns>The control source, without control points and without tolerance.</returns>
    /// <remarks>
    /// The wrapper is produced by
    /// <see cref="Gst.GObject.Object.FromNative{T}(nint, Transfer)"/> rather
    /// than by calling the constructor, which is the supported way to turn a
    /// handle into a wrapper and the reason this type appears in the type
    /// registry of the module.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The module was not registered, so the type registry could not build the
    /// wrapper. Calling this method is a call into this assembly and runs its
    /// module initialiser, so this should not happen.
    /// </exception>
    public static TriggerControlSource New()
    {
        nint handle = GstTriggerControlSourceNew();

        return Object.FromNative<TriggerControlSource>(handle, Transfer.Full)
            ?? throw new InvalidOperationException(
                "gst_trigger_control_source_new did not produce a wrapper. The type registry has no entry " +
                "for GstTriggerControlSource, which means the module of Gst.Controller did not register.");
    }

    /// <summary>
    /// Returns the <c>GType</c> that GObject registered
    /// <c>GstTriggerControlSource</c> under.
    /// </summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("GstController", EntryPoint = "gst_trigger_control_source_get_type")]
    internal static new partial nuint GetGType();

    /// <summary>
    /// Creates the wrapper of a native instance, for the type registry.
    /// </summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static new object CreateWrapper(nint handle, Transfer transfer) =>
        new TriggerControlSource(handle, transfer);

    [LibraryImport("GstController", EntryPoint = "gst_trigger_control_source_new")]
    private static partial nint GstTriggerControlSourceNew();
}
