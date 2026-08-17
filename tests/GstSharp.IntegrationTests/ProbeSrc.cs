using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed push source: it produces a fixed number of one byte buffers whose
/// content counts up, and then ends the stream.
/// </summary>
/// <remarks>
/// This is the shape stage 1 of <c>docs/subclassing.md</c> exists for. It
/// declares five slots — the one that produces data and the four that make an
/// element behave like a real source — and it needs nothing from the runtime
/// that is not public.
/// </remarks>
internal class ProbeSrc : PushSrc
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeSrc";

    /// <summary>
    /// The media type the probes negotiate. It is a private one so that the
    /// negotiation really runs: caps of <c>ANY</c> make <c>GstBaseSrc</c> skip
    /// it, and then no <c>set_caps</c> would ever be called.
    /// </summary>
    internal const string MediaType = "application/x-gstsharp-subclass";

    /// <summary>
    /// The pad template, built <em>before</em> the registration: the class
    /// initialiser may only add one, never build one. Field initialisers run in
    /// declaration order, which is what puts this one before
    /// <see cref="Definition"/>.
    /// </summary>
    private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        CreateOverride,
        StartOverride,
        StopOverride,
        SetCapsOverride,
        IsSeekableOverride);

    private readonly List<string> _lifecycle = [];

    private int _produced;

    /// <summary>Creates a managed source.</summary>
    internal ProbeSrc()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the source is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets or sets how many buffers to produce before the stream ends.</summary>
    internal int BufferCount { get; set; } = 3;

    /// <summary>Gets how many buffers the override produced.</summary>
    internal int Produced => Volatile.Read(ref _produced);

    /// <summary>Gets the caps the source was told to produce, or null.</summary>
    internal string? NegotiatedCaps { get; private set; }

    /// <summary>Gets the lifecycle calls the source has seen, oldest first.</summary>
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
    protected override bool OnIsSeekable()
    {
        Record("is-seekable");
        return false;
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
    protected override FlowReturn OnCreate(out Gst.Buffer? buffer)
    {
        int index = _produced;

        if (index >= BufferCount)
        {
            buffer = null;
            Record("eos");
            return FlowReturn.Eos;
        }

        // The wrapper owns the buffer until the trampoline hands the reference
        // to GStreamer, which is what "out" means here.
        buffer = Gst.Buffer.NewMemdup([(byte)index]);
        Volatile.Write(ref _produced, index + 1);
        return FlowReturn.Ok;
    }

    /// <summary>Describes the class, and gives it the pad it needs.</summary>
    /// <param name="config">The class being initialised.</param>
    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe source",
            "Source/Testing",
            "Produces a fixed number of counting buffers",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SrcTemplate);
    }

    /// <summary>Builds the source pad template of the class.</summary>
    /// <returns>The template, which lives for the process.</returns>
    private static PadTemplate NewSrcTemplate()
    {
        // gst_pad_template_new takes its own reference to the caps, so the
        // wrapper of the caps is done once the template exists.
        using Caps caps = Caps.NewEmptySimple(MediaType);

        return PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The source pad template could not be created.");
    }

    private void Record(string call)
    {
        lock (_lifecycle)
        {
            _lifecycle.Add(call);
        }
    }
}
