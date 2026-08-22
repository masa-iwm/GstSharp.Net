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
/// There are two ways to use it, and they answer different questions.
/// <see cref="RequiresGst128FactAttribute"/> skips a whole test whose subject
/// only exists on 1.28; <see cref="Has128"/> is for a test that runs everywhere
/// and only has to branch on what it may expect. Where the subject is dated by
/// its version rather than by one symbol, the older
/// <see cref="RequiresGStreamerFactAttribute"/> gates on the version number the
/// library reports and takes any minor version, which is what the
/// <c>appsink</c> tests use.
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
    /// <summary>
    /// The message a test is skipped with when the installed GStreamer predates
    /// 1.28. It names the requirement rather than the symbol, because the
    /// symbol is an implementation detail of this probe.
    /// </summary>
    internal const string Gst128SkipReason = "GStreamer 1.28 or newer is required for this test";

    // Lazy is what makes the probe run once per process and be safe to ask for
    // from several tests at the same time; the collection is serialized today,
    // but a gate is not the place to depend on that.
    private static readonly Lazy<bool> Gst128 = new(Probe128, LazyThreadSafetyMode.ExecutionAndPublication);

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

    /// <summary>The <c>GType</c> of a <c>GstValueUniqueList</c>, new in 1.28.</summary>
    /// <returns><c>GST_TYPE_VALUE_UNIQUE_LIST</c>.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_value_unique_list_get_type")]
    private static partial nuint ValueUniqueListGetType();
}

/// <summary>
/// A fact that only runs when the installed GStreamer exports the entry points
/// of 1.28.
/// </summary>
/// <remarks>
/// <para>
/// The skip is computed in the constructor, which xunit runs during discovery,
/// because xunit 2 has no way to skip a test from inside it — there is no
/// <c>Assert.Skip</c> here, and the gate has to be an attribute for that reason
/// alone. That is the same shape <see cref="RequiresGStreamerFactAttribute"/>
/// and <see cref="RequiresElementFactAttribute"/> use.
/// </para>
/// <para>
/// A failure to load the library leaves the test enabled, so that it fails with
/// the real load error rather than hiding it behind a skip.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresGst128FactAttribute : FactAttribute
{
    /// <summary>Initialises the fact and decides whether it may run.</summary>
    public RequiresGst128FactAttribute()
    {
        bool available;
        try
        {
            available = NativeAvailability.Has128;
        }
        catch
        {
            // Let the test run and fail with the real load error.
            return;
        }

        if (!available)
        {
            Skip = NativeAvailability.Gst128SkipReason;
        }
    }
}
