using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A <c>GstDevice</c> that describes no hardware, so a managed provider has
/// something to announce on a machine that has no device at all.
/// </summary>
/// <remarks>
/// <para>
/// <c>Gst.Device</c> is abstract and carries no managed subclassing surface —
/// that limit is documented in <c>docs/subclassing.md</c> — so this type is
/// registered through the internal <see cref="SubclassType.Define"/> that the
/// generated <c>DefineSubclass</c> of a subclassable class calls, which the
/// test assembly reaches through <c>InternalsVisibleTo</c>. It takes over no
/// slot: <c>create_element</c> and <c>reconfigure_element</c> are the only two
/// <c>GstDevice</c> has, both are guarded by a NULL check
/// (<c>gstdevice.c:209-215, 334-339</c>), and nothing the announcement path
/// touches calls them.
/// </para>
/// </remarks>
internal sealed class ProbeDevice : Device
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestDevice";

    private static readonly SubclassType Definition = SubclassType.Define(
        new GType(GetGType()),
        GTypeName,
        (Action<ObjectClassConfig>?)null,
        [],
        null,
        null);

    /// <summary>Creates a device of the managed type from C#.</summary>
    /// <param name="displayName">The name the device reports.</param>
    internal ProbeDevice(string displayName)
        : this(Definition.NewInstance(
            new Dictionary<string, object?>
            {
                ["display-name"] = displayName,
                ["device-class"] = "Test/Probe",
            }))
    {
    }

    private ProbeDevice(SubclassCtorArgs args)
        : base(args.Handle, args.Transfer)
    {
    }
}
