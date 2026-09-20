using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstDevice</c> subclass that declares both of its slots without
/// overriding either, so every call goes through the generated chain-up onto a
/// parent class that leaves both slots NULL. That is the one path no class in
/// the chain implements, and it has to answer what the C answers for an empty
/// slot — nothing for <c>create_element</c> and false for
/// <c>reconfigure_element</c> (<c>gstdevice.c:210-215, :338-341</c>) — rather
/// than call through a NULL pointer or throw.
/// </summary>
internal sealed class ProbeChainUpDevice : Device, IManagedSubclass<ProbeChainUpDevice>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestChainUpDevice";

    private static readonly SubclassType Definition = DefineSubclass<ProbeChainUpDevice>(
        GTypeName,
        null,

        // Declaring a slot without implementing it is exactly the shape this
        // fixture exists to measure - what the chain-up of a slot no class in
        // the chain implements answers - so GST0004, which asks for the pair,
        // is suppressed here rather than the fixture being removed.
#pragma warning disable GST0004
        CreateElementOverride,
        ReconfigureElementOverride);
#pragma warning restore GST0004

    private ProbeChainUpDevice(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeChainUpDevice CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <summary>Creates a device of the managed type from C#.</summary>
    /// <returns>The device, owned by the caller.</returns>
    internal static ProbeChainUpDevice New() =>
        new(Definition.NewInstance(new Dictionary<string, object?>
        {
            ["display-name"] = "Chain up device",
            ["device-class"] = "Test/Probe",
        }));
}
