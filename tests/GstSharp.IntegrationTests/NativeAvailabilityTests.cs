extern alias gstsharp;

using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The shared 1.28 gate against the installed library.
/// </summary>
/// <remarks>
/// A gate nothing measures is a gate that may be wrong on the one leg that
/// needs it. These tests run everywhere and pin the probe of
/// <see cref="NativeAvailability"/> to the version the library reports of
/// itself, in both directions, so that a probe symbol that stopped being a
/// 1.28 marker — because it was backported, renamed or removed — turns the
/// suite red instead of silently skipping every test behind it.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class NativeAvailabilityTests
{
    /// <summary>
    /// The probe agrees with the version the library reports: the entry point
    /// is there on 1.28 and newer, and nowhere else.
    /// </summary>
    [Fact]
    public void The128ProbeAgreesWithTheReportedVersion()
    {
        Gst.Version version = gstsharp::GstSharp.NativeVersion;

        // The binding supports no GStreamer 2, and a 2.x would carry the entry
        // point as well, so the comparison is on the minor version of 1.x.
        bool expected = version.Major > 1 || (version.Major == 1 && version.Minor >= 28);

        Assert.Equal(expected, NativeAvailability.Has128);
    }

    /// <summary>
    /// A fact behind the version gate only runs where the entry points exist,
    /// which is what every test of a 1.28 only member relies on: the two gates
    /// are separate mechanisms and have to agree wherever one of them lets a
    /// test through.
    /// </summary>
    [RequiresGStreamerFact(28)]
    public void AGatedFactRunsOnlyWhereTheEntryPointsExist()
    {
        Assert.True(NativeAvailability.Has128);
    }
}
