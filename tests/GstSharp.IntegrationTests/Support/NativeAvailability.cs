extern alias gstsharp;

using System.Runtime.InteropServices;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Asks the installed GStreamer whether it carries the entry points of a
/// release newer than the floor the CI matrix is built on.
/// </summary>
/// <remarks>
/// <para>
/// The binding is generated from the GStreamer 1.28 <c>.gir</c> files, so its
/// managed surface names members that older installations do not export. The
/// Linux leg of the matrix runs GStreamer 1.24 on purpose — it is the floor the
/// struct layouts are validated against, see <c>eng/ci-notes.md</c> — and a
/// call into a member that arrived after that floor throws an
/// <see cref="EntryPointNotFoundException"/> there. That is the documented
/// behaviour of the binding rather than a defect, so a test for such a member
/// must not run where it cannot pass.
/// </para>
/// <para>
/// This type is <b>the</b> place where that gate is decided. Tests do not probe
/// symbols of their own, because a probe that lives beside its test is a probe
/// every new test copies, and the copies drift.
/// </para>
/// <para>
/// <see cref="Has128"/> and <see cref="Has126"/> are the behaviour gates: they
/// are what a test that runs everywhere asks when it only has to branch on what
/// it may expect. A test whose subject does not exist before the release it
/// arrived in — 1.26 or 1.28 — is skipped whole instead, with
/// <see cref="RequiresGStreamerFactAttribute"/>, which gates on the version
/// number the library reports of itself.
/// </para>
/// <para>
/// The probe is a real call into the library rather than a version comparison,
/// so what it reports is what a test would actually hit: a build that reports
/// 1.28 but was configured without a symbol answers <see langword="false"/>
/// here and would answer <see langword="true"/> to a version check.
/// </para>
/// </remarks>
internal static partial class NativeAvailability
{
    // Lazy is what makes the probe run once per process and be safe to ask for
    // from several tests at the same time; the collection is serialized today,
    // but a gate is not the place to depend on that.
    private static readonly Lazy<bool> Gst128 = new(Probe128, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<bool> Gst126 = new(Probe126, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<bool> TrackDiscard =
        new(ProbeTrackDiscard, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets a value indicating whether the installed GStreamer exports the
    /// entry points that arrived in 1.28.
    /// </summary>
    /// <remarks>
    /// The probe symbol is <c>gst_value_unique_list_get_type</c>, the
    /// <c>GstValueUniqueList</c> fundamental type, which the <c>.gir</c> marks
    /// <c>version="1.28"</c>. It is a plain getter of a <c>GType</c>: calling it
    /// registers the type and has no other effect, which is what makes it usable
    /// as a probe at all.
    /// </remarks>
    /// <exception cref="DllNotFoundException">
    /// No GStreamer installation was found. A broken installation is a red
    /// suite, never a skip, so this is deliberately not caught.
    /// </exception>
    internal static bool Has128 => Gst128.Value;

    private static bool Probe128()
    {
        // Both calls are idempotent. Initialize loads the library, and the
        // resolver registration is what lets the import below find it from this
        // assembly, which GstFixture also does but may not have done yet: xunit
        // constructs the fact attributes during discovery, before any fixture.
        gstsharp::GstSharp.Initialize();
        NativeLoader.EnsureRegistered(typeof(NativeAvailability).Assembly);

        try
        {
            _ = ValueUniqueListGetType();
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the installed GStreamer exports the
    /// entry points that arrived in 1.26.
    /// </summary>
    /// <remarks>
    /// The probe symbol is <c>gst_id_str_get_type</c>, the <c>GstIdStr</c> boxed
    /// type, which the <c>.gir</c> marks <c>version="1.26"</c> and whose header
    /// does not exist in the 1.24 tree at all. It is a plain getter of a
    /// <c>GType</c>, so calling it registers the type and has no other effect.
    /// This is the twin of <see cref="Has128"/> and it exists for the same
    /// reason: the <c>_id_str</c> half of every structure family — including
    /// the three <c>foreach</c> and <c>map_in_place</c> walks — is unreachable
    /// on the 1.24 floor, where only the deprecated <c>GQuark</c> half is.
    /// </remarks>
    /// <exception cref="DllNotFoundException">
    /// No GStreamer installation was found. A broken installation is a red
    /// suite, never a skip, so this is deliberately not caught.
    /// </exception>
    internal static bool Has126 => Gst126.Value;

    private static bool Probe126()
    {
        gstsharp::GstSharp.Initialize();
        NativeLoader.EnsureRegistered(typeof(NativeAvailability).Assembly);

        try
        {
            _ = IdStrGetType();
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the installed GES discards a track
    /// element that no <c>select-element-track</c> handler claimed, instead of
    /// falling back to <c>select-tracks-for-object</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two probes above answer "is the API there", which a symbol can be
    /// asked. This answers "does the library carry a behaviour change", which
    /// no symbol can: upstream commit d3d8989798 ("ges: timeline: Respect
    /// SELECT_ELEMENT_TRACK signal discard decision") added no export, it
    /// gated an existing emission on <c>g_signal_has_handler_pending</c>
    /// (1.28.6 ges-timeline.c:1496-1500). So this is a version comparison, and
    /// it is the version comparison of a backport: the change first shipped in
    /// 1.27.50 and 1.28.0, and reached the 1.26 branch as 40fa67b4d8, whose
    /// first tag is 1.26.7. That is why there are two branches rather than one
    /// <c>IsAtLeast(1, 26, 7)</c> — the unstable 1.27.1 and 1.27.2 are at least
    /// 1.26.7 and do not carry it — and why no 1.24.x answers
    /// <see langword="true"/>, since the change was never backported that far.
    /// </para>
    /// <para>
    /// It lives here for the reason the probes do: this type is the one place
    /// the suite asks a version question of the library, see the remarks on the
    /// type itself.
    /// </para>
    /// </remarks>
    /// <exception cref="DllNotFoundException">
    /// No GStreamer installation was found. A broken installation is a red
    /// suite, never a skip, so this is deliberately not caught.
    /// </exception>
    internal static bool DiscardsUnselectedTrackElements => TrackDiscard.Value;

    private static bool ProbeTrackDiscard()
    {
        // Initialize is what makes the version readable at all, the same way
        // the two probes above need it to make the library loadable.
        gstsharp::GstSharp.Initialize();

        Gst.Version version = gstsharp::GstSharp.NativeVersion;

        return version.IsAtLeast(1, 27, 50)
            || (version.Major == 1 && version.Minor == 26 && version.IsAtLeast(1, 26, 7));
    }

    /// <summary>The <c>GType</c> of a <c>GstValueUniqueList</c>, new in 1.28.</summary>
    /// <returns><c>GST_TYPE_VALUE_UNIQUE_LIST</c>.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_value_unique_list_get_type")]
    private static partial nuint ValueUniqueListGetType();

    /// <summary>The <c>GType</c> of a <c>GstIdStr</c>, new in 1.26.</summary>
    /// <returns><c>GST_TYPE_ID_STR</c>.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_id_str_get_type")]
    private static partial nuint IdStrGetType();
}
