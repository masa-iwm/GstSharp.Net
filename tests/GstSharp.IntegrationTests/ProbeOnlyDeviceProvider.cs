using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstDeviceProvider</c> subclass that takes over <c>probe</c> and
/// nothing else, which is the second of the two shapes a managed provider may
/// have: either slot closes the unguarded <c>klass-&gt;probe</c> call of
/// <c>gst_device_provider_start</c>, so a provider that only enumerates what is
/// there needs no <c>start</c> at all.
/// </summary>
/// <remarks>
/// One device is kept in a field across every call, which is what pins that the
/// list hand-out leaves the wrappers usable; a second one is minted per call, so
/// that a test can tell the two answers of two calls apart. The switches are the
/// three answers the contract refuses or singles out: an exception, a null entry
/// and the same device twice.
/// </remarks>
internal sealed class ProbeOnlyDeviceProvider : DeviceProvider, IManagedSubclass<ProbeOnlyDeviceProvider>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeOnlyDeviceProvider";

    /// <summary>The message the refusing override throws with.</summary>
    internal const string Excuse = "The probe override refused to answer.";

    private static readonly SubclassType Definition = DefineSubclass<ProbeOnlyDeviceProvider>(
        GTypeName,
        null,
        ProbeOverride);

    private readonly ProbeDevice _kept = ProbeDevice.New("Kept probe device");

    private int _probed;

    /// <summary>Creates a provider of the managed type from C#.</summary>
    internal ProbeOnlyDeviceProvider()
        : base(Definition.NewInstance())
    {
    }

    private ProbeOnlyDeviceProvider(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets how often the <c>probe</c> override ran.</summary>
    internal int Probed => Volatile.Read(ref _probed);

    /// <summary>Gets the device the override answers on every call.</summary>
    internal ProbeDevice Kept => _kept;

    /// <summary>Gets the device the override minted in its last call.</summary>
    internal ProbeDevice? Fresh { get; private set; }

    /// <summary>Gets or sets a value indicating whether the override throws.</summary>
    internal bool Throws { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the override answers a list with
    /// an empty entry, which the hand-out refuses.
    /// </summary>
    internal bool AnswersANullEntry { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the override answers the kept
    /// device alone, with no freshly minted one beside it.
    /// </summary>
    internal bool AnswersTheKeptDeviceAlone { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the override answers the kept
    /// device twice in one list, which is legal and mints a reference per
    /// occurrence.
    /// </summary>
    internal bool AnswersTheKeptDeviceTwice { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the override answers what the
    /// implementation below it answers, which is what the default override
    /// does: no class below a managed provider implements <c>probe</c>, so the
    /// chain-up reads a NULL slot and answers no devices.
    /// </summary>
    internal bool ChainsUp { get; set; }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeOnlyDeviceProvider CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _kept.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<Device> OnProbe()
    {
        int round = Interlocked.Increment(ref _probed);

        if (Throws)
        {
            throw new InvalidOperationException(Excuse);
        }

        if (ChainsUp)
        {
            return ChainUpProbe();
        }

        if (AnswersANullEntry)
        {
            return [_kept, null!];
        }

        if (AnswersTheKeptDeviceTwice)
        {
            return [_kept, _kept];
        }

        if (AnswersTheKeptDeviceAlone)
        {
            return [_kept];
        }

        ProbeDevice fresh = ProbeDevice.New("Fresh probe device " + round.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Fresh = fresh;
        return [_kept, fresh];
    }
}
