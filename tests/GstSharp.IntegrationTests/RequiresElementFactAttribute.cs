extern alias gstsharp;

using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A fact that only runs when a plugin the test needs is installed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequiresGStreamerFactAttribute"/> gates on the version of the
/// library; this gates on what is beside it. The binding needs the library of a
/// plugin set to resolve its types even where nothing builds an element out of
/// it, so most of the CI matrix installs the libraries of the bad plugins
/// without the plugins themselves — the MinGW job takes
/// <c>mingw-w64-x86_64-gst-plugins-bad-libs</c>, which carries
/// <c>libgstwebrtc-1.0</c> and no <c>webrtcbin</c> — because pulling the plugins
/// in there as well would add fifty packages to a run that gains nothing from
/// them. A test that builds an element from one of those plugins therefore
/// cannot run everywhere and must not fail where it cannot.
/// </para>
/// <para>
/// One leg does carry them. The Linux job installs
/// <c>gstreamer1.0-plugins-bad</c> and <c>gstreamer1.0-nice</c> beside the
/// libraries and owns the WebRTC tests, which is what keeps them from being
/// skipped everywhere — a test that never runs guards nothing. That the
/// elements really are there is not left to this attribute, whose answer to a
/// missing one is a skip: the job names them in
/// <c>GSTSHARP_REQUIRED_ELEMENTS</c> and
/// <see cref="RequiredElementsTests"/> fails when the promise is broken.
/// </para>
/// <para>
/// The skip is computed in the constructor, which xunit runs during discovery,
/// because xunit 2 has no way to skip a test from inside it. That is the same
/// shape <see cref="RequiresGStreamerFactAttribute"/> uses, including the
/// initialisation: <c>GstSharp.Initialize()</c> is idempotent, and a failure to
/// initialise leaves the test enabled so that it fails with the real load error
/// rather than hiding it behind a skip.
/// </para>
/// <para>
/// Naming the missing element in the reason is what keeps the gap visible in
/// the test report, which an early return inside a green test would not.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresElementFactAttribute : FactAttribute
{
    /// <summary>
    /// Initialises the fact with the element factories the test needs.
    /// </summary>
    /// <param name="factoryNames">
    /// The names of the factories, for example <c>webrtcbin</c>. Every one of
    /// them has to exist for the test to run: an element is often usable only
    /// with the elements it builds its internals from — <c>webrtcbin</c> is
    /// findable wherever its plugin is installed, but leaves the NULL state
    /// only where <c>nicesrc</c> and <c>dtlssrtpenc</c> exist to build its
    /// transports from — and gating on the entry element alone runs the test
    /// on installations where it can only fail.
    /// </param>
    public RequiresElementFactAttribute(params string[] factoryNames)
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

        foreach (string factoryName in factoryNames)
        {
            // The factory is a singleton of the plugin registry and is not
            // disposed: a GObject wrapper stands for the whole process, and
            // giving up its part in the lifetime of a registry entry is not
            // this attribute's to do.
            if (Gst.ElementFactory.Find(factoryName) is null)
            {
                Skip = $"needs the \"{factoryName}\" element, which the installed GStreamer does not provide";
                return;
            }
        }
    }
}
