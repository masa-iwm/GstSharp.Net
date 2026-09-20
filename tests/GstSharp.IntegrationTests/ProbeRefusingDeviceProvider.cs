using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstDeviceProvider</c> whose <c>start</c> override refuses to
/// start, either by answering <see langword="false"/> or by throwing, which
/// the trampoline reports and turns into the same answer.
/// </summary>
internal sealed class ProbeRefusingDeviceProvider
    : DeviceProvider, IManagedSubclass<ProbeRefusingDeviceProvider>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestRefusingDeviceProvider";

    /// <summary>What the <c>start</c> override says when it refuses.</summary>
    internal const string Excuse = "the probe provider refuses to start";

    private static readonly SubclassType Definition =
        DefineSubclass<ProbeRefusingDeviceProvider>(
            GTypeName,
            null,
            StartOverride,
            StopOverride);

    private int _started;

    private int _stopped;

    /// <summary>Creates a provider of the managed type from C#.</summary>
    internal ProbeRefusingDeviceProvider()
        : base(Definition.NewInstance())
    {
    }

    private ProbeRefusingDeviceProvider(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets how often the <c>start</c> override ran.</summary>
    internal int Started => Volatile.Read(ref _started);

    /// <summary>Gets how often the <c>stop</c> override ran.</summary>
    internal int Stopped => Volatile.Read(ref _stopped);

    /// <summary>
    /// Gets or sets whether the override throws rather than answering
    /// <see langword="false"/>.
    /// </summary>
    internal bool Throws { get; set; }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeRefusingDeviceProvider CreateWrapper(SubclassCtorArgs args) =>
        new(args);

    /// <inheritdoc/>
    protected override bool OnStart()
    {
        _ = Interlocked.Increment(ref _started);

        if (Throws)
        {
            throw new InvalidOperationException(Excuse);
        }

        return false;
    }

    /// <inheritdoc/>
    protected override void OnStop() => _ = Interlocked.Increment(ref _stopped);
}
