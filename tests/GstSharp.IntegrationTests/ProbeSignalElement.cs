using Gst;
using Gst.Base;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed push source that defines three signals of its own: one that
/// answers nothing and has a class handler, one that stops at the first handler
/// which says it handled the emission, and one that stops at the first handler
/// to answer at all.
/// </summary>
internal sealed class ProbeSignalElement : PushSrc, IManagedSubclass<ProbeSignalElement>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestSignalElement";

    /// <summary>The name the element factory is registered under.</summary>
    internal const string FactoryName = "gstsharptestsignalelement";

    /// <summary>The signal that answers nothing.</summary>
    internal const string PingSignal = "gstsharp-ping";

    /// <summary>The signal whose emission stops at the first handler that returns true.</summary>
    internal const string HandledSignal = "gstsharp-handled";

    /// <summary>The signal whose emission stops at the first handler to answer.</summary>
    internal const string FirstWinsSignal = "gstsharp-first-wins";

    private const string MediaType = "application/x-gstsharp-signal-element";

    private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

    private static readonly SubclassType Definition = DefineSubclass<ProbeSignalElement>(
        GTypeName,
        ConfigureClass,
        CreateOverride);

    private static readonly bool Registered =
        Element.Register(null, FactoryName, (uint)Rank.None, Definition.GType);

    private static uint _pingSignalId;
    private static int _classHandlerCalls;
    private static GObjectObject? _classHandlerSender;
    private static object?[]? _classHandlerArgs;

    private ProbeSignalElement(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets a value indicating whether the factory was registered.</summary>
    internal static bool IsRegistered => Registered;

    /// <summary>Gets the identifier <c>AddSignal</c> answered for the ping signal.</summary>
    internal static uint PingSignalId => Volatile.Read(ref _pingSignalId);

    /// <summary>Gets how many times the class handler ran.</summary>
    internal static int ClassHandlerCalls => Volatile.Read(ref _classHandlerCalls);

    /// <summary>Gets the instance the class handler was given last.</summary>
    internal static GObjectObject? ClassHandlerSender => Volatile.Read(ref _classHandlerSender);

    /// <summary>Gets the arguments the class handler was given last.</summary>
    internal static object?[]? ClassHandlerArgs => Volatile.Read(ref _classHandlerArgs);

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset()
    {
        Volatile.Write(ref _classHandlerCalls, 0);
        Volatile.Write(ref _classHandlerSender, null);
        Volatile.Write(ref _classHandlerArgs, null);
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeSignalElement CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override FlowReturn OnCreate(out Gst.Buffer? buffer)
    {
        buffer = null;
        return FlowReturn.Eos;
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe signal element",
            "Source/Testing",
            "A managed source that defines signals",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SrcTemplate);

        Volatile.Write(
            ref _pingSignalId,
            config.AddSignal(
                PingSignal,
                SignalFlags.RunLast,
                GType.None,
                [GType.Int],
                OnPing));

        _ = config.AddSignal(
            HandledSignal,
            SignalFlags.RunLast,
            GType.Boolean,
            [],
            null,
            SignalAccumulator.TrueHandled);

        _ = config.AddSignal(
            FirstWinsSignal,
            SignalFlags.RunLast,
            GType.Int,
            [],
            null,
            SignalAccumulator.FirstWins);
    }

    private static object? OnPing(GObjectObject sender, object?[] args)
    {
        _ = Interlocked.Increment(ref _classHandlerCalls);
        Volatile.Write(ref _classHandlerSender, sender);
        Volatile.Write(ref _classHandlerArgs, args);
        return null;
    }

    private static PadTemplate NewSrcTemplate()
    {
        using Caps caps = Caps.NewEmptySimple(MediaType);

        return PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The source pad template could not be created.");
    }
}
