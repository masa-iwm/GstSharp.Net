using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed sink: it counts the buffers it is given and keeps their first
/// byte, so that a test can say what reached it and in which order.
/// </summary>
internal class ProbeSink : BaseSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeSink";

    /// <summary>The pad template, built before the registration.</summary>
    private static readonly PadTemplate SinkTemplate = NewSinkTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        RenderOverride,
        PrerollOverride,
        StartOverride,
        StopOverride,
        SetCapsOverride);

    private readonly List<string> _lifecycle = [];
    private readonly List<byte> _rendered = [];

    private int _prerolled;

    /// <summary>Creates a managed sink.</summary>
    /// <remarks>
    /// Synchronisation is turned off because the buffers of the probe source
    /// carry no timestamps: a sink that waits for the clock would have nothing
    /// to wait for and would rather not wait at all.
    /// </remarks>
    internal ProbeSink()
        : base(Definition.NewInstance())
    {
        using Value sync = Value.New(GType.Boolean);
        sync.SetBoolean(false);
        SetProperty("sync", sync);
    }

    /// <summary>Gets the type the sink is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>
    /// Gets or sets a value indicating whether the render override throws, so
    /// that the per slot exception policy can be observed.
    /// </summary>
    internal bool ThrowOnRender { get; set; }

    /// <summary>Gets how often the preroll override ran.</summary>
    internal int Prerolled => Volatile.Read(ref _prerolled);

    /// <summary>Gets the caps the sink was told to expect, or null.</summary>
    internal string? NegotiatedCaps { get; private set; }

    /// <summary>Gets the first byte of every buffer that was rendered, in order.</summary>
    internal IReadOnlyList<byte> Rendered
    {
        get
        {
            lock (_rendered)
            {
                return _rendered.ToArray();
            }
        }
    }

    /// <summary>Gets the lifecycle calls the sink has seen, oldest first.</summary>
    internal IReadOnlyList<string> Lifecycle
    {
        get
        {
            lock (_lifecycle)
            {
                return _lifecycle.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    protected override bool OnStart()
    {
        Record("start");
        return ChainUpStart();
    }

    /// <inheritdoc/>
    protected override bool OnStop()
    {
        Record("stop");
        return ChainUpStop();
    }

    /// <inheritdoc/>
    protected override bool OnSetCaps(Caps caps)
    {
        ArgumentNullException.ThrowIfNull(caps);

        NegotiatedCaps = caps.ToString();
        Record("set-caps");
        return ChainUpSetCaps(caps);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnPreroll(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        Interlocked.Increment(ref _prerolled);
        Record("preroll");
        return ChainUpPreroll(buffer);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnRender(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (ThrowOnRender)
        {
            throw new InvalidOperationException("The managed sink refuses this buffer.");
        }

        using (Gst.Buffer.MapScope map = buffer.Map(MapFlags.Read))
        {
            lock (_rendered)
            {
                _rendered.Add(map.Span.Length == 0 ? (byte)0 : map.Span[0]);
            }
        }

        return FlowReturn.Ok;
    }

    /// <summary>Describes the class, and gives it the pad it needs.</summary>
    /// <param name="config">The class being initialised.</param>
    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe sink",
            "Sink/Testing",
            "Counts the buffers it is given",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
    }

    /// <summary>Builds the sink pad template of the class.</summary>
    /// <returns>The template, which lives for the process.</returns>
    private static PadTemplate NewSinkTemplate()
    {
        using Caps caps = Caps.NewEmptySimple(ProbeSrc.MediaType);

        return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The sink pad template could not be created.");
    }

    private void Record(string call)
    {
        lock (_lifecycle)
        {
            _lifecycle.Add(call);
        }
    }
}
