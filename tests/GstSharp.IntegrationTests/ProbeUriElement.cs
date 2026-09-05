using Gst;
using Gst.Base;
using Gst.GLib;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed push source that is a <c>GstURIHandler</c> for one protocol of its
/// own, so that <c>gst_element_make_from_uri</c> can find it.
/// </summary>
internal sealed class ProbeUriElement : PushSrc, IManagedSubclass<ProbeUriElement>, IURIHandlerImplementation
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestUriElement";

    /// <summary>The name the element factory is registered under.</summary>
    internal const string FactoryName = "gstsharptesturielement";

    /// <summary>The protocol the element handles.</summary>
    internal const string Protocol = "gstsharptest";

    /// <summary>The part of a URI that makes the element refuse it.</summary>
    internal const string RefusedHost = "refused";

    private const string MediaType = "application/x-gstsharp-uri-element";

    private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

    private static readonly SubclassType Definition = DefineSubclass<ProbeUriElement>(
        GTypeName,
        ConfigureClass,
        new SubclassOptions { Interfaces = [URIHandlerImplementation.For<ProbeUriElement>()] },
        CreateOverride);

    private static readonly bool Registered =
        Element.Register(null, FactoryName, (uint)Rank.None, Definition.GType);

    private static int _wrappersCreated;

    private string? _uri;
    private int _setUriCalls;

    private ProbeUriElement(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <inheritdoc/>
    public static URIType UriType => URIType.Src;

    /// <inheritdoc/>
    public static IReadOnlyList<string> Protocols => [Protocol];

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets a value indicating whether the factory was registered.</summary>
    internal static bool IsRegistered => Registered;

    /// <summary>Gets how many wrappers the runtime has fabricated.</summary>
    internal static int WrappersCreated => Volatile.Read(ref _wrappersCreated);

    /// <summary>Gets the URI this element was given, or null.</summary>
    internal string? Uri => Volatile.Read(ref _uri);

    /// <summary>Gets how often this element was given a URI.</summary>
    internal int SetUriCalls => Volatile.Read(ref _setUriCalls);

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset() => Volatile.Write(ref _wrappersCreated, 0);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeUriElement CreateWrapper(SubclassCtorArgs args)
    {
        _ = Interlocked.Increment(ref _wrappersCreated);
        return new ProbeUriElement(args);
    }

    /// <inheritdoc/>
    public string? GetUri() => Volatile.Read(ref _uri);

    /// <inheritdoc/>
    public bool SetUri(string uri, out GException? error)
    {
        _ = Interlocked.Increment(ref _setUriCalls);
        error = null;

        if (uri.Contains(RefusedHost, StringComparison.Ordinal))
        {
            // Refusing without an error is the shape the runtime has to cover:
            // GStreamer synthesises none and gst_element_make_from_uri reads
            // whatever is there.
            return false;
        }

        Volatile.Write(ref _uri, uri);
        return true;
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
            "GstSharp probe URI element",
            "Source/Testing",
            "A managed source that handles a URI protocol of its own",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SrcTemplate);
    }

    private static PadTemplate NewSrcTemplate()
    {
        using Caps caps = Caps.NewEmptySimple(MediaType);

        return PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The source pad template could not be created.");
    }
}
