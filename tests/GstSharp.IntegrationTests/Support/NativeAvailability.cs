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
/// <see cref="Has128"/> is the behaviour gate: it is what a test that runs
/// everywhere asks when it only has to branch on what it may expect. A test
/// whose subject does not exist before 1.28 is skipped whole instead, with
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
