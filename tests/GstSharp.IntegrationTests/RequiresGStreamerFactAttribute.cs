extern alias gstsharp;

using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A fact that only runs when the loaded GStreamer is new enough.
/// </summary>
/// <remarks>
/// <para>
/// The CI matrix runs these tests against every installation the binding
/// supports, and the oldest of them (GStreamer 1.24 from apt) predates some of
/// the entry points the binding covers. A test for such an entry point cannot
/// pass there and must not fail there either — the missing export is the
/// documented behavior, not a bug. Skipping with the version in the reason
/// keeps the gap visible in the test report, which a silently-green early
/// return would not.
/// </para>
/// <para>
/// The attribute initialises the binding itself, because xunit constructs it
/// during discovery, before any collection fixture ran.
/// <c>GstSharp.Initialize()</c> is idempotent and takes no options here, so it
/// composes with whatever a fixture does later. When initialisation itself
/// fails the test is left enabled: it then fails with the real load error,
/// which is the diagnosis, instead of hiding it behind a skip.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresGStreamerFactAttribute : FactAttribute
{
    /// <summary>
    /// Initialises the fact with the minimum GStreamer 1.x minor version.
    /// </summary>
    /// <param name="minor">The minimum minor version, for example 28.</param>
    public RequiresGStreamerFactAttribute(uint minor)
    {
        try
        {
            gstsharp::GstSharp.Initialize();
        }
        catch
        {
            // Let the test run and fail with the real load error.
            return;
        }

        Gst.Version version = gstsharp::GstSharp.NativeVersion;
        if (version.Major == 1 && version.Minor < minor)
        {
            Skip = FormattableString.Invariant(
                $"needs GStreamer 1.{minor} or newer, and {version} is installed");
        }
    }
}
