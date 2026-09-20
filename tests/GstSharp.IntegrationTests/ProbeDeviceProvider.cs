using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstDeviceProvider</c> subclass that takes over both
/// <c>start</c> and <c>stop</c>, which is the pair a managed provider has to
/// declare: <c>gst_device_provider_start</c> falls back to <c>klass-&gt;probe</c>
/// with no NULL check when <c>start</c> is NULL, and no managed provider
/// installs a <c>probe</c>.
/// </summary>
internal sealed class ProbeDeviceProvider : DeviceProvider, IManagedSubclass<ProbeDeviceProvider>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestDeviceProvider";

    private static readonly SubclassType Definition = DefineSubclass<ProbeDeviceProvider>(
        GTypeName,
        null,
        StartOverride,
        StopOverride);

    private readonly List<Device> _announce = [];

    private int _started;

    private int _stopped;

    /// <summary>Creates a provider of the managed type from C#.</summary>
    internal ProbeDeviceProvider()
        : base(Definition.NewInstance())
    {
    }

    private ProbeDeviceProvider(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets how often the <c>start</c> override ran.</summary>
    internal int Started => Volatile.Read(ref _started);

    /// <summary>Gets how often the <c>stop</c> override ran.</summary>
    internal int Stopped => Volatile.Read(ref _stopped);

    /// <summary>
    /// Gets the devices the provider announces from its <c>start</c> override
    /// and withdraws again from its <c>stop</c> override.
    /// </summary>
    internal IList<Device> Announce => _announce;

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeDeviceProvider CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override bool OnStart()
    {
        _ = Interlocked.Increment(ref _started);
        foreach (Device device in _announce)
        {
            DeviceAdd(device);
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void OnStop()
    {
        _ = Interlocked.Increment(ref _stopped);
        foreach (Device device in _announce)
        {
            DeviceRemove(device);
        }
    }
}
