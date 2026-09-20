using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A <c>GstDevice</c> that describes no hardware, so a managed provider has
/// something to announce on a machine that has no device at all, and so the two
/// slots of a device can be driven without one either.
/// </summary>
/// <remarks>
/// <para>
/// The type goes through the generated, public <c>DefineSubclass</c> of
/// <see cref="Device"/> and takes over both of its slots. All four properties of
/// a <c>GstDevice</c> are <c>CONSTRUCT_ONLY</c> (<c>gstdevice.c:89-104</c>), so
/// they are given through the dictionary overload of
/// <see cref="SubclassType.NewInstance()"/>, which is the only way to write one.
/// </para>
/// <para>
/// <see cref="Answered"/> is the one thing the probe does that the contract of
/// <c>OnCreateElement</c> tells an override not to do: it keeps the wrapper of
/// the element it answered, so that a test can compare what the forward call
/// hands back with what the override produced and can read the element after a
/// caller dropped its own reference. What makes that safe here and not in
/// general is the rest of the contract — the probe never parents the element it
/// kept and never hands the same one out twice.
/// </para>
/// </remarks>
internal sealed class ProbeDevice : Device, IManagedSubclass<ProbeDevice>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestDevice";

    /// <summary>The caps every probe device reports.</summary>
    internal const string CapsDescription = "audio/x-gstsharp-probe";

    /// <summary>The name of the structure every probe device reports.</summary>
    internal const string PropertiesName = "GstSharpProbeDeviceProperties";

    private static readonly SubclassType Definition = DefineSubclass<ProbeDevice>(
        GTypeName,
        null,
        CreateElementOverride,
        ReconfigureElementOverride);

    private int _created;

    private int _reconfigured;

    private ProbeDevice(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets how often the <c>create_element</c> override ran.</summary>
    internal int Created => Volatile.Read(ref _created);

    /// <summary>Gets how often the <c>reconfigure_element</c> override ran.</summary>
    internal int Reconfigured => Volatile.Read(ref _reconfigured);

    /// <summary>Gets the element the <c>create_element</c> override answered last.</summary>
    internal Element? Answered { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the <c>create_element</c>
    /// override answers nothing, which <c>gst_device_create_element</c> allows.
    /// </summary>
    internal bool AnswersNothing { get; set; }

    /// <summary>
    /// Gets or sets what the <c>reconfigure_element</c> override answers.
    /// </summary>
    internal bool Reconfigures { get; set; } = true;

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeDevice CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <summary>Creates a device of the managed type from C#.</summary>
    /// <param name="displayName">The name the device reports.</param>
    /// <returns>The device, owned by the caller.</returns>
    internal static ProbeDevice New(string displayName)
    {
        using Caps caps = Caps.FromString(CapsDescription)
            ?? throw new InvalidOperationException("The probe caps did not parse.");
        using Structure properties = Structure.NewEmpty(PropertiesName);

        return new ProbeDevice(Definition.NewInstance(new Dictionary<string, object?>
        {
            ["display-name"] = displayName,
            ["device-class"] = "Test/Probe",
            ["caps"] = caps,
            ["properties"] = properties,
        }));
    }

    /// <inheritdoc/>
    protected override Element? OnCreateElement(string? name)
    {
        _ = Interlocked.Increment(ref _created);
        if (AnswersNothing)
        {
            Answered = null;
            return null;
        }

        Element element = ElementFactory.Make("identity", name)
            ?? throw new InvalidOperationException("The identity element is not installed.");

        Answered = element;
        return element;
    }

    /// <inheritdoc/>
    protected override bool OnReconfigureElement(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _ = Interlocked.Increment(ref _reconfigured);
        return Reconfigures;
    }
}
