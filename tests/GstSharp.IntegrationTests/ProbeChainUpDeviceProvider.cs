using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstDeviceProvider</c> subclass that declares the <c>start</c>
/// slot without overriding <see cref="DeviceProvider.OnStart"/>, so starting it
/// goes through the generated chain-up onto a parent class that leaves the slot
/// NULL. That is the one path the base class has no implementation for, and it
/// has to answer "nothing below refuses" rather than call through a NULL
/// pointer or throw.
/// </summary>
internal sealed class ProbeChainUpDeviceProvider
    : DeviceProvider, IManagedSubclass<ProbeChainUpDeviceProvider>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestChainUpDeviceProvider";

    private static readonly SubclassType Definition = DefineSubclass<ProbeChainUpDeviceProvider>(
        GTypeName,
        null,

        // Declaring the slot without implementing it is exactly the shape this
        // fixture exists to measure - what the chain-up of a slot no parent
        // class implements answers - so GST0004, which asks for the pair,
        // is suppressed here rather than the fixture being removed.
#pragma warning disable GST0004
        StartOverride);
#pragma warning restore GST0004

    /// <summary>Creates a provider of the managed type from C#.</summary>
    internal ProbeChainUpDeviceProvider()
        : base(Definition.NewInstance())
    {
    }

    private ProbeChainUpDeviceProvider(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeChainUpDeviceProvider CreateWrapper(SubclassCtorArgs args) => new(args);
}
