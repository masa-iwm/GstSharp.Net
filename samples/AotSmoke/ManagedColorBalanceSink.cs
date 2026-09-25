// A managed video sink with GstColorBalance attached when the type is defined.
// It is here so that ILC has to compile the four interface slots of the color
// balance, the per-instance channel list they lend, and the GDestroyNotify that
// releases it with the element.
using Gst;
using Gst.GObject;
using Gst.Video;

/// <summary>
/// A managed video sink that offers one color balance channel.
/// </summary>
internal sealed class ManagedColorBalanceSink : VideoSink, IManagedSubclass<ManagedColorBalanceSink>,
    IColorBalanceImplementation
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedColorBalanceSink";

    private static readonly PadTemplate SinkTemplate = NewSinkTemplate();

    private static readonly SubclassType Definition = DefineSubclass<ManagedColorBalanceSink>(
        GTypeName,
        ConfigureClass,
        new SubclassOptions { Interfaces = [ColorBalanceImplementation.For<ManagedColorBalanceSink>()] });

    private readonly ColorBalanceChannel[] _channels = [ColorBalanceChannel.New("BRIGHTNESS", -100, 100)];

    private int _brightness;

    /// <summary>Creates a managed color balance sink.</summary>
    internal ManagedColorBalanceSink()
        : base(Definition.NewInstance())
    {
    }

    private ManagedColorBalanceSink(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the type the sink is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <inheritdoc/>
    public ColorBalanceType BalanceType => ColorBalanceType.Software;

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ManagedColorBalanceSink CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    public IReadOnlyList<ColorBalanceChannel> ListChannels() => _channels;

    /// <inheritdoc/>
    public void SetValue(ColorBalanceChannel channel, int value)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Volatile.Write(ref _brightness, Math.Clamp(value, channel.MinValue, channel.MaxValue));
    }

    /// <inheritdoc/>
    public int GetValue(ColorBalanceChannel channel) => Volatile.Read(ref _brightness);

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "AotSmoke managed color balance sink",
            "Sink/Video",
            "Offers a brightness channel, in C#",
            "GstSharp.Net");

        config.AddPadTemplate(SinkTemplate);
    }

    private static PadTemplate NewSinkTemplate()
    {
        using Caps caps = Caps.FromString("video/x-raw")
            ?? throw new InvalidOperationException("The sink caps could not be parsed.");

        return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The sink pad template could not be created.");
    }
}
