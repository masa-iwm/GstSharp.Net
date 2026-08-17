namespace Gst.Controller;

/// <summary>
/// How an <see cref="InterpolationControlSource"/> fills the gaps between the
/// timed values it was given.
/// </summary>
/// <remarks>
/// This is <c>GstInterpolationMode</c>. The values are the ones the C enumeration
/// declares, because the mode travels through the <c>mode</c> property as a
/// plain enumeration value.
/// </remarks>
public enum InterpolationMode
{
    /// <summary>
    /// Steps: the value of the last control point before the timestamp, held
    /// until the next one. This is the default of a fresh control source.
    /// </summary>
    None = 0,

    /// <summary>Straight lines between neighbouring control points.</summary>
    Linear = 1,

    /// <summary>
    /// Natural cubic interpolation, which is smooth but may overshoot the
    /// values of the control points it passes through.
    /// </summary>
    Cubic = 2,

    /// <summary>
    /// Monotonic cubic interpolation, which stays inside the values of the
    /// control points and therefore never overshoots.
    /// </summary>
    CubicMonotonic = 3,
}
