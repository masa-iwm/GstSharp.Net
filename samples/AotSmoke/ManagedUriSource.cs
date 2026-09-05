// A managed element that carries everything stage 3b added: an installed
// property answered by the two property slots, a signal defined on the class
// with a class handler behind it, and GstURIHandler attached when the type is
// defined. It is here so that ILC has to compile those paths as well - the
// property trampolines, the dynamic signal closure with its meta marshaller,
// and the four interface slots of the URI handler.
using Gst;
using Gst.Base;
using Gst.GLib;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

/// <summary>
/// A managed push source with a property, a signal and a URI protocol.
/// </summary>
internal sealed class ManagedUriSource : PushSrc, IManagedSubclass<ManagedUriSource>, IURIHandlerImplementation
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedUriSource";

    /// <summary>The name the element factory is registered under.</summary>
    internal const string FactoryName = "aotsmokemanagedurisource";

    /// <summary>The protocol the element handles.</summary>
    internal const string Protocol = "aotsmoke";

    /// <summary>The signal the element defines on its own class.</summary>
    internal const string ReadySignal = "aotsmoke-ready";

    /// <summary>The identifier of the <c>value</c> property.</summary>
    internal const uint ValueId = 1;

    private const ParamFlags ReadWrite = ParamFlags.Readable | ParamFlags.Writable;

    private const string MediaType = "application/x-aotsmoke-uri";

    private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

    private static readonly ParamSpecInt ValueSpec =
        ParamSpecInt.New("value", "Value", "An integer a caller may write", 0, 100, 0, ReadWrite);

    private static readonly SubclassType Definition = DefineSubclass<ManagedUriSource>(
        GTypeName,
        ConfigureClass,
        new SubclassOptions { Interfaces = [URIHandlerImplementation.For<ManagedUriSource>()] },
        CreateOverride,
        GObjectObject.SetPropertyOverride,
        GObjectObject.GetPropertyOverride);

    private static int _classHandlerCalls;

    private string? _uri;
    private int _value;

    private ManagedUriSource(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <inheritdoc/>
    public static URIType UriType => URIType.Src;

    /// <inheritdoc/>
    public static IReadOnlyList<string> Protocols => [Protocol];

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets how often the class handler of the signal ran.</summary>
    internal static int ClassHandlerCalls => Volatile.Read(ref _classHandlerCalls);

    /// <summary>Gets what the last write to the property stored.</summary>
    internal int Value => Volatile.Read(ref _value);

    /// <summary>Gets the URI the element was given, or null.</summary>
    internal string? Uri => Volatile.Read(ref _uri);

    /// <summary>Registers the element factory.</summary>
    /// <returns><see langword="true"/> when the factory was registered.</returns>
    internal static bool RegisterFactory() =>
        Element.Register(null, FactoryName, (uint)Rank.None, Definition.GType);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ManagedUriSource CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    public string? GetUri() => Volatile.Read(ref _uri);

    /// <inheritdoc/>
    public bool SetUri(string uri, out GException? error)
    {
        error = null;
        Volatile.Write(ref _uri, uri);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnSetProperty(uint propertyId, ValueView value, ParamSpec pspec)
    {
        if (propertyId == ValueId)
        {
            Volatile.Write(ref _value, value.GetInt());
            return;
        }

        base.OnSetProperty(propertyId, value, pspec);
    }

    /// <inheritdoc/>
    protected override void OnGetProperty(uint propertyId, ValueRef value, ParamSpec pspec)
    {
        if (propertyId == ValueId)
        {
            value.SetInt(Volatile.Read(ref _value));
            return;
        }

        base.OnGetProperty(propertyId, value, pspec);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnCreate(out Gst.Buffer? buffer)
    {
        buffer = null;
        return FlowReturn.Eos;
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "AotSmoke managed URI source",
            "Source/Testing",
            "A managed source with a property, a signal and a URI protocol",
            "GstSharp.Net");

        config.AddPadTemplate(SrcTemplate);
        config.InstallProperty(ValueId, ValueSpec);

        _ = config.AddSignal(ReadySignal, SignalFlags.RunLast, GType.None, [GType.Int], OnReady);
    }

    private static object? OnReady(GObjectObject sender, object?[] arguments)
    {
        _ = sender;
        _ = arguments;
        _ = Interlocked.Increment(ref _classHandlerCalls);
        return null;
    }

    private static PadTemplate NewSrcTemplate()
    {
        using Caps caps = Caps.NewEmptySimple(MediaType);

        return PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The source pad template could not be created.");
    }
}
