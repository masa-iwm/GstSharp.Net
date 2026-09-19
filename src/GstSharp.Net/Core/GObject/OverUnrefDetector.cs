namespace Gst.GObject;

/// <summary>
/// The switch of the over-unref detector: a debug aid that reports a
/// <c>GObject</c> which dies while the binding still holds its toggle
/// reference.
/// </summary>
/// <remarks>
/// <para>
/// The detector is off unless <c>GSTSHARP_DETECT_OVER_UNREF=1</c> is set in
/// the environment, and costs nothing at all while it is: no weak reference is
/// installed, no stack is captured, and no extra native call is made when a
/// wrapper is built or released.
/// </para>
/// <para>
/// The switch is an environment variable rather than a member of
/// <c>GstSharpOptions</c> because wrappers are built before, and during,
/// <c>GstSharp.Initialize</c> — an option that is only known once the
/// initialisation is over would miss them. The variable is read once, and the
/// value stays settable so that a test can turn the detector on around the
/// wrappers it builds.
/// </para>
/// <para>
/// Whether a given wrapper is being watched is decided once, when it is
/// constructed, and is remembered per wrapper: flipping the switch in between
/// must never make a release remove a weak reference that was never installed.
/// </para>
/// </remarks>
internal static class OverUnrefDetector
{
    private static volatile bool _enabled =
        string.Equals(
            Environment.GetEnvironmentVariable("GSTSHARP_DETECT_OVER_UNREF"),
            "1",
            StringComparison.Ordinal);

    /// <summary>
    /// Gets or sets a value indicating whether wrappers built from now on
    /// watch their object for an over-unref.
    /// </summary>
    internal static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}
