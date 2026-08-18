using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.Controller;

/// <summary>
/// A control source that interpolates between its timed values, the
/// <c>GstInterpolationControlSource</c> of <c>libgstcontroller</c>.
/// </summary>
/// <remarks>
/// <para>
/// Set the control points with
/// <see cref="TimedValueControlSource.Set(Gst.ClockTime, double)"/>, pick a
/// <see cref="Mode"/>, and attach the source to a property with
/// <see cref="DirectControlBinding.New(Gst.Object, string, Gst.ControlSource)"/>:
/// </para>
/// <code>
/// InterpolationControlSource source = InterpolationControlSource.New();
/// source.Mode = InterpolationMode.Linear;
/// source.Set(Gst.ClockTime.Zero, 0.0);
/// source.Set(Gst.ClockTime.FromSeconds(1), 1.0);
///
/// element.AddControlBinding(DirectControlBinding.New(element, "volume", source));
/// </code>
/// <para>
/// The values are in <c>[0, 1]</c> and the binding maps them onto the range the
/// bound property declares, unless it is an absolute binding — see
/// <see cref="DirectControlBinding.NewAbsolute(Gst.Object, string, Gst.ControlSource)"/>.
/// </para>
/// </remarks>
public sealed unsafe partial class InterpolationControlSource : TimedValueControlSource
{
    /// <summary>
    /// Wraps a native <c>GstInterpolationControlSource</c>.
    /// </summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal InterpolationControlSource(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets or sets how the source fills the gaps between its control points.
    /// </summary>
    /// <remarks>
    /// This is the <c>mode</c> property, read and written through the public
    /// property surface of <see cref="Gst.GObject.Object"/>. Reading it first is
    /// how the setter gets a <c>GValue</c> of the right enumeration type without
    /// the module having to resolve <c>gst_interpolation_mode_get_type</c>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public InterpolationMode Mode
    {
        get
        {
            using Value value = GetProperty("mode");
            return (InterpolationMode)value.GetEnum();
        }

        set
        {
            using Value property = GetProperty("mode");
            property.SetEnum((int)value);
            SetProperty("mode", property);
        }
    }

    /// <summary>
    /// Creates a new interpolation control source.
    /// </summary>
    /// <returns>The control source, in <see cref="InterpolationMode.None"/>.</returns>
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
    public static InterpolationControlSource New()
    {
        nint handle = GstInterpolationControlSourceNew();

        return Object.FromNative<InterpolationControlSource>(handle, Transfer.Full)
            ?? throw new InvalidOperationException(
                "gst_interpolation_control_source_new did not produce a wrapper. The type registry has no " +
                "entry for GstInterpolationControlSource, which means the module of Gst.Controller did not " +
                "register.");
    }

    /// <summary>
    /// Returns the <c>GType</c> that GObject registered
    /// <c>GstInterpolationControlSource</c> under.
    /// </summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("GstController", EntryPoint = "gst_interpolation_control_source_get_type")]
    internal static new partial nuint GetGType();

    /// <summary>
    /// Creates the wrapper of a native instance, for the type registry.
    /// </summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static new object CreateWrapper(nint handle, Transfer transfer) =>
        new InterpolationControlSource(handle, transfer);

    [LibraryImport("GstController", EntryPoint = "gst_interpolation_control_source_new")]
    private static partial nint GstInterpolationControlSourceNew();
}
