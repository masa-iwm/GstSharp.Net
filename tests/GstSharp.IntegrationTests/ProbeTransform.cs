using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed in place filter: it adds one to every byte of every buffer that
/// passes through it.
/// </summary>
/// <remarks>
/// Mutating the buffer is the point. The buffer a <c>transform_ip</c> override
/// is given is writable, and it stays writable only because the wrapper the
/// trampoline builds borrows it instead of taking a reference of its own — a
/// second reference would make <c>gst_buffer_map</c> refuse the write mapping
/// below.
/// </remarks>
internal class ProbeTransform : BaseTransform
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeTransform";

    /// <summary>The pad templates, built before the registration.</summary>
    private static readonly PadTemplate SinkTemplate = NewTemplate("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = NewTemplate("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        TransformIpOverride,
        SetCapsOverride,
        StartOverride,
        StopOverride);

    private readonly List<string> _lifecycle = [];

    private int _transformed;

    /// <summary>Creates a managed filter.</summary>
    internal ProbeTransform()
        : base(Definition.NewInstance())
    {
        // Both pads carry the same caps, so this only makes the intent
        // explicit: the element rewrites buffers where they lie.
        SetInPlace(true);
    }

    /// <summary>Gets the type the filter is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets how many buffers the override rewrote.</summary>
    internal int Transformed => Volatile.Read(ref _transformed);

    /// <summary>Gets whether the buffer of the last call was writable.</summary>
    internal bool LastBufferWasWritable { get; private set; }

    /// <summary>Gets the lifecycle calls the filter has seen, oldest first.</summary>
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
    protected override bool OnSetCaps(Caps inCaps, Caps outCaps)
    {
        Record("set-caps");
        return ChainUpSetCaps(inCaps, outCaps);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnTransformIp(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        LastBufferWasWritable = buffer.IsWritable;

        using (Gst.Buffer.MapScope map = buffer.Map(MapFlags.Write))
        {
            Span<byte> bytes = map.Span;

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i]++;
            }
        }

        Interlocked.Increment(ref _transformed);
        return FlowReturn.Ok;
    }

    /// <summary>Describes the class, and gives it the two pads it needs.</summary>
    /// <param name="config">The class being initialised.</param>
    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe filter",
            "Filter/Testing",
            "Adds one to every byte that passes through",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }

    /// <summary>Builds one pad template of the class.</summary>
    /// <param name="name">The name of the template.</param>
    /// <param name="direction">Which way the pad points.</param>
    /// <returns>The template, which lives for the process.</returns>
    private static PadTemplate NewTemplate(string name, PadDirection direction)
    {
        using Caps caps = Caps.NewEmptySimple(ProbeSrc.MediaType);

        return PadTemplate.New(name, direction, PadPresence.Always, caps)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }

    private void Record(string call)
    {
        lock (_lifecycle)
        {
            _lifecycle.Add(call);
        }
    }
}
