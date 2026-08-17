using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The order of the steps of <c>GstSharp.Initialize</c>, measured against the
/// GLib log of the process.
/// </summary>
/// <remarks>
/// <para>
/// Initialisation used to freeze the type registry before it called
/// <c>gst_init</c>, so every registered <c>get_type</c> ran against an
/// uninitialised GStreamer. Most of them survive that;
/// <c>gst_encoding_audio_profile_get_type</c> does not, and logs three GLib
/// criticals on the way to registering a type that is missing entries. Nothing
/// threw, nothing was returned, and the only outward sign was the criticals —
/// which is what <see cref="InitializeLogProbe"/> is there to see.
/// </para>
/// <para>
/// The measurement happens in a module initialiser rather than here, because
/// this test cannot cause it: <c>GstSharp.Initialize</c> is idempotent and by
/// the time xunit runs a test the first call is long done. What this class does
/// is read the verdict and insist that the probe was in a position to reach
/// one.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class InitializeOrderTests
{
    /// <summary>
    /// The probe has to have claimed the GLib writer, or an empty list of
    /// criticals means nothing at all.
    /// </summary>
    [Fact]
    public void TheProbeIsWatchingTheLog()
    {
        Assert.True(
            InitializeLogProbe.IsInstalled,
            "The GLib log writer was not installed, so the assertion below would pass vacuously.");
    }

    /// <summary>
    /// Initialising GStreamer with the Pbutils module registered logs no GLib
    /// criticals, which it only manages when <c>gst_init</c> runs before the
    /// type registry is frozen.
    /// </summary>
    [Fact]
    public void FreezingTheRegistryAfterGstInitLogsNoCriticals()
    {
        Assert.Null(InitializeLogProbe.Failure);
        Assert.Empty(InitializeLogProbe.InitializeCriticals);
    }
}
