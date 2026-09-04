// A GStreamer sink element written in C#: it keeps the first byte of every
// buffer ManagedSource produced, so that the smoke test can say that the data
// really travelled from one managed element to the other.
using Gst;
using Gst.Base;
using Gst.GObject;

/// <summary>
/// A managed sink that remembers what it was given.
/// </summary>
internal sealed class ManagedSink : BaseSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedSink";

    private static readonly PadTemplate SinkTemplate = NewSinkTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        RenderOverride,
        SetCapsOverride);

    private readonly List<byte> _rendered = [];

    /// <summary>Creates a managed sink.</summary>
    /// <remarks>
    /// The buffers carry no timestamps, so there is nothing to synchronise to.
    /// </remarks>
    internal ManagedSink()
        : base(Definition.NewInstance())
    {
        using Value sync = Value.New(GType.Boolean);
        sync.SetBoolean(false);
        SetProperty("sync", sync);
    }

    /// <summary>Gets the type the sink is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets the caps the sink was told to expect, or null.</summary>
    internal string? NegotiatedCaps { get; private set; }

    /// <summary>Gets the first byte of every buffer that was rendered, in order.</summary>
    internal byte[] Rendered
    {
        get
        {
            lock (_rendered)
            {
                return _rendered.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    protected override bool OnSetCaps(Caps caps)
    {
        ArgumentNullException.ThrowIfNull(caps);

        NegotiatedCaps = caps.ToString();
        return ChainUpSetCaps(caps);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnRender(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        using (Gst.Buffer.MapScope map = buffer.Map(MapFlags.Read))
        {
            lock (_rendered)
            {
                _rendered.Add(map.Span.Length == 0 ? (byte)0 : map.Span[0]);
            }
        }

        return FlowReturn.Ok;
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "AotSmoke managed sink",
            "Sink/Testing",
            "Counts the buffers it is given, in C#",
            "GstSharp.Net");

        config.AddPadTemplate(SinkTemplate);
    }

    private static PadTemplate NewSinkTemplate()
    {
        using Caps caps = Caps.NewEmptySimple(ManagedSource.MediaType);

        return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The sink pad template could not be created.");
    }
}
