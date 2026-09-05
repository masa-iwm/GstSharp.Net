using Gst;
using Gst.Base;
using Gst.GLib;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed URI handler that refuses every URI and reports no reason, which is
/// the shape <c>gst_element_make_from_uri</c> would dereference a null
/// <c>GError</c> for.
/// </summary>
/// <remarks>
/// It handles a protocol nobody else does, so it is the only candidate for its
/// URIs and the error the runtime synthesises reaches the caller unchanged.
/// </remarks>
internal sealed class ProbeFailingUriElement
    : PushSrc, IManagedSubclass<ProbeFailingUriElement>, IURIHandlerImplementation
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestFailingUriElement";

    /// <summary>The name the element factory is registered under.</summary>
    internal const string FactoryName = "gstsharptestfailingurielement";

    /// <summary>The protocol the element claims to handle.</summary>
    internal const string Protocol = "gstsharpfail";

    private const string MediaType = "application/x-gstsharp-failing-uri-element";

    private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

    private static readonly SubclassType Definition = DefineSubclass<ProbeFailingUriElement>(
        GTypeName,
        ConfigureClass,
        new SubclassOptions { Interfaces = [URIHandlerImplementation.For<ProbeFailingUriElement>()] },
        CreateOverride);

    private static readonly bool Registered =
        Element.Register(null, FactoryName, (uint)Rank.None, Definition.GType);

    private ProbeFailingUriElement(SubclassCtorArgs args)
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

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeFailingUriElement CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    public string? GetUri() => null;

    /// <inheritdoc/>
    public bool SetUri(string uri, out GException? error)
    {
        _ = uri;
        error = null;
        return false;
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
            "GstSharp probe failing URI element",
            "Source/Testing",
            "A managed source that refuses every URI without saying why",
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
