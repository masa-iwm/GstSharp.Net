// A GStreamer source element written in C#: it produces a fixed number of one
// byte buffers and then ends the stream. Together with ManagedSink it is what
// makes ILC compile the whole stage 1 subclassing surface — the registration,
// the class configuration, the UnmanagedCallersOnly trampolines of five slots
// and the chain-ups through the class struct mirrors.
using Gst;
using Gst.Base;
using Gst.GObject;

/// <summary>
/// A managed push source that counts up.
/// </summary>
internal sealed class ManagedSource : PushSrc
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedSrc";

    /// <summary>The media type the sample negotiates.</summary>
    internal const string MediaType = "application/x-aotsmoke";

    // The pad template exists before the registration: the class initialiser
    // may add one, never build one. Field initialisers run in declaration
    // order, which is what puts this one first.
    private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        CreateOverride,
        StartOverride,
        StopOverride);

    private int _produced;

    /// <summary>Creates a managed source.</summary>
    internal ManagedSource()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the source is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets or sets how many buffers to produce.</summary>
    internal int BufferCount { get; set; } = 4;

    /// <summary>Gets how many buffers were produced.</summary>
    internal int Produced => Volatile.Read(ref _produced);

    /// <summary>Gets a value indicating whether the source was started and stopped again.</summary>
    internal bool Cycled => Started && Stopped;

    private bool Started { get; set; }

    private bool Stopped { get; set; }

    /// <inheritdoc/>
    protected override bool OnStart()
    {
        Started = true;
        return ChainUpStart();
    }

    /// <inheritdoc/>
    protected override bool OnStop()
    {
        Stopped = true;
        return ChainUpStop();
    }

    /// <inheritdoc/>
    protected override FlowReturn OnCreate(out Gst.Buffer? buffer)
    {
        int index = _produced;

        if (index >= BufferCount)
        {
            buffer = null;
            return FlowReturn.Eos;
        }

        buffer = Gst.Buffer.NewMemdup([(byte)index]);
        Volatile.Write(ref _produced, index + 1);
        return FlowReturn.Ok;
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "AotSmoke managed source",
            "Source/Testing",
            "Produces counting buffers from C#",
            "GstSharp.Net");

        config.AddPadTemplate(SrcTemplate);
    }

    private static PadTemplate NewSrcTemplate()
    {
        using Caps caps = Caps.NewEmptySimple(MediaType);

        return PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The source pad template could not be created.");
    }
}
