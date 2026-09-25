using Gst;
using Gst.GObject;
using Gst.Video;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed video sink that implements <c>GstColorBalance</c> with the four
/// channels <c>playsink</c> looks for, and lets a test change what its members
/// answer.
/// </summary>
/// <remarks>
/// The values live in the element and every change is announced through
/// <see cref="ColorBalanceExtensions.ValueChanged"/>, the way
/// <c>videobalance</c> does it (<c>gstvideobalance.c:731-734</c>).
/// </remarks>
internal sealed class ProbeColorBalanceSink : VideoSink, IManagedSubclass<ProbeColorBalanceSink>,
    IColorBalanceImplementation
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestColorBalanceSink";

    /// <summary>The lowest value of every channel.</summary>
    internal const int MinValue = -1000;

    /// <summary>The highest value of every channel.</summary>
    internal const int MaxValue = 1000;

    private static readonly PadTemplate SinkTemplate = NewSinkTemplate();

    private static readonly SubclassType Definition = DefineSubclass<ProbeColorBalanceSink>(
        GTypeName,
        ConfigureClass,
        new SubclassOptions { Interfaces = [ColorBalanceImplementation.For<ProbeColorBalanceSink>()] });

    private readonly System.Threading.Lock _gate = new();

    private readonly Dictionary<ColorBalanceChannel, int> _values = new(ReferenceEqualityComparer.Instance);

    private IReadOnlyList<ColorBalanceChannel>? _answer;

    private int _listCalls;

    private int _setValueCalls;

    private string? _lastSetLabel;

    private int _lastSetValue;

    /// <summary>Creates an element of the type.</summary>
    internal ProbeColorBalanceSink()
        : this(Definition.NewInstance())
    {
    }

    private ProbeColorBalanceSink(SubclassCtorArgs args)
        : base(args)
    {
        Channels = NewChannels();
        _answer = Channels;
    }

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets the four channels the element was made with.</summary>
    internal IReadOnlyList<ColorBalanceChannel> Channels { get; }

    /// <summary>
    /// Gets or sets what <see cref="ListChannels"/> answers, the four channels
    /// by default.
    /// </summary>
    internal IReadOnlyList<ColorBalanceChannel>? Answer
    {
        get => Volatile.Read(ref _answer);
        set => Volatile.Write(ref _answer, value);
    }

    /// <summary>Gets or sets a value indicating whether <see cref="ListChannels"/> throws.</summary>
    internal bool ThrowOnList { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="GetValue"/> throws.</summary>
    internal bool ThrowOnGetValue { get; set; }

    /// <summary>Gets how often <see cref="ListChannels"/> was called.</summary>
    internal int ListCalls => Volatile.Read(ref _listCalls);

    /// <summary>Gets how often <see cref="SetValue"/> was called.</summary>
    internal int SetValueCalls => Volatile.Read(ref _setValueCalls);

    /// <summary>Gets the label of the channel the last <see cref="SetValue"/> was for.</summary>
    internal string? LastSetLabel => Volatile.Read(ref _lastSetLabel);

    /// <summary>Gets the value the last <see cref="SetValue"/> was given.</summary>
    internal int LastSetValue => Volatile.Read(ref _lastSetValue);

    /// <inheritdoc/>
    public ColorBalanceType BalanceType { get; set; } = ColorBalanceType.Hardware;

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeColorBalanceSink CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    public IReadOnlyList<ColorBalanceChannel> ListChannels()
    {
        _ = Interlocked.Increment(ref _listCalls);

        if (ThrowOnList)
        {
            throw new InvalidOperationException("The probe was told to throw from ListChannels.");
        }

        return Answer!;
    }

    /// <inheritdoc/>
    public void SetValue(ColorBalanceChannel channel, int value)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _ = Interlocked.Increment(ref _setValueCalls);
        Volatile.Write(ref _lastSetLabel, channel.Label);
        Volatile.Write(ref _lastSetValue, value);

        int clamped = Math.Clamp(value, channel.MinValue, channel.MaxValue);
        bool changed;

        lock (_gate)
        {
            changed = !_values.TryGetValue(channel, out int previous) || previous != clamped;
            _values[channel] = clamped;
        }

        if (changed)
        {
            (As<IColorBalance>() ?? throw new InvalidOperationException("The probe is not a color balance."))
                .ValueChanged(channel, clamped);
        }
    }

    /// <inheritdoc/>
    public int GetValue(ColorBalanceChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (ThrowOnGetValue)
        {
            throw new InvalidOperationException("The probe was told to throw from GetValue.");
        }

        lock (_gate)
        {
            return _values.TryGetValue(channel, out int value) ? value : 0;
        }
    }

    private static ColorBalanceChannel[] NewChannels() =>
    [
        ColorBalanceChannel.New("BRIGHTNESS", MinValue, MaxValue),
        ColorBalanceChannel.New("CONTRAST", MinValue, MaxValue),
        ColorBalanceChannel.New("HUE", MinValue, MaxValue),
        ColorBalanceChannel.New("SATURATION", MinValue, MaxValue),
    ];

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe color balance sink",
            "Sink/Video",
            "A managed video sink that balances colors",
            "GstSharp.Net integration tests");

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
