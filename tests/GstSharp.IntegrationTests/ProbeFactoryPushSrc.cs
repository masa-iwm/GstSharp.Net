using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed push source that is registered as an element factory, so that a
/// pipeline description is all it takes to make GStreamer create one — and so
/// that the first managed code the instance ever reaches is a vfunc trampoline
/// on a streaming thread.
/// </summary>
/// <remarks>
/// <para>
/// This is the one shape that exercises the fabrication as it really happens:
/// no C# code touches the instance between <c>g_object_new</c> and the first
/// <c>create</c> call, so the wrapper is built by the trampoline, on the task
/// thread of the source, for an instance the application never asked for.
/// </para>
/// <para>
/// <c>create</c> is the only slot the class declares on purpose. Every other
/// slot of a source — <c>start</c>, <c>is_seekable</c>, <c>change_state</c> —
/// runs on the thread that sets the state, which is the test thread, and would
/// make "the wrapper was built on a streaming thread" depend on the order the
/// state change happens to take.
/// </para>
/// </remarks>
internal sealed class ProbeFactoryPushSrc : PushSrc, IManagedSubclass<ProbeFactoryPushSrc>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestFactoryPushSrc";

    /// <summary>The name the element factory is registered under.</summary>
    internal const string FactoryName = "gstsharptestfactorypushsrc";

    /// <summary>How many buffers one instance produces before it ends the stream.</summary>
    internal const int BufferCount = 3;

    /// <summary>The media type of the source, private to these tests.</summary>
    private const string MediaType = "application/x-gstsharp-factory-pushsrc";

    /// <summary>
    /// The pad template, built <em>before</em> the registration: the class
    /// initialiser may only add one, never build one. Field initialisers run in
    /// declaration order, which is what puts this one before the definition.
    /// </summary>
    private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

    private static readonly SubclassType Definition = DefineSubclass<ProbeFactoryPushSrc>(
        GTypeName,
        ConfigureClass,
        CreateOverride);

    private static readonly bool Registered =
        Element.Register(null, FactoryName, (uint)Rank.None, Definition.GType);

    private static int _wrappersBuilt;
    private static int _wrapperThreadId;
    private static ProbeFactoryPushSrc? _lastWrapper;

    private int _produced;

    /// <summary>
    /// Wraps an instance native code created. The body is empty because it runs
    /// inside the fabrication gate — see <c>docs/subclassing.md</c> §5.4.
    /// </summary>
    /// <param name="args">What the runtime says about the instance.</param>
    private ProbeFactoryPushSrc(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the type the source is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets a value indicating whether the factory was registered.</summary>
    internal static bool IsRegistered => Registered;

    /// <summary>Gets how many wrappers were fabricated since the last reset.</summary>
    internal static int WrappersBuilt => Volatile.Read(ref _wrappersBuilt);

    /// <summary>Gets the managed thread the last wrapper was built on, or zero.</summary>
    internal static int WrapperThreadId => Volatile.Read(ref _wrapperThreadId);

    /// <summary>Gets the wrapper that was fabricated last, or null.</summary>
    internal static ProbeFactoryPushSrc? LastWrapper => Volatile.Read(ref _lastWrapper);

    /// <summary>Gets how many buffers this instance produced.</summary>
    internal int Produced => Volatile.Read(ref _produced);

    /// <summary>Gets the managed thread the last <c>create</c> ran on, or zero.</summary>
    internal int CreateThreadId { get; private set; }

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset()
    {
        Volatile.Write(ref _wrappersBuilt, 0);
        Volatile.Write(ref _wrapperThreadId, 0);
        Volatile.Write(ref _lastWrapper, null);
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeFactoryPushSrc CreateWrapper(SubclassCtorArgs args)
    {
        ProbeFactoryPushSrc wrapper = new(args);

        // Recording is what this probe is for, and it is the one thing that may
        // happen here besides handing the arguments on: no native call, no
        // property, no waiting.
        Volatile.Write(ref _wrapperThreadId, Environment.CurrentManagedThreadId);
        Volatile.Write(ref _lastWrapper, wrapper);
        _ = Interlocked.Increment(ref _wrappersBuilt);
        return wrapper;
    }

    /// <inheritdoc/>
    protected override FlowReturn OnCreate(out Gst.Buffer? buffer)
    {
        CreateThreadId = Environment.CurrentManagedThreadId;

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

    /// <summary>Describes the class, and gives it the pad it needs.</summary>
    /// <param name="config">The class being initialised.</param>
    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe factory push source",
            "Source/Testing",
            "A managed push source an element factory creates",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SrcTemplate);
    }

    /// <summary>Builds the source pad template of the class.</summary>
    /// <returns>The template, which lives for the process.</returns>
    private static PadTemplate NewSrcTemplate()
    {
        using Caps caps = Caps.NewEmptySimple(MediaType);

        return PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The source pad template could not be created.");
    }
}
