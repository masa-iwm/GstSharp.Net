using System.Runtime.InteropServices;
using Gst;
using Gst.Base;
using Gst.GLib;
using Gst.GObject;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed element that implements <c>GstURIHandler</c>: the interface is
/// attached when the type is defined, its type-keyed slots answer while the
/// element factory is registered, and its instance slots answer for a wrapper
/// the runtime fabricates on the path <c>gst_element_make_from_uri</c> takes.
/// </summary>
/// <remarks>
/// All of it needs a registered <c>GType</c> and a running library, so it is an
/// integration test. What the validations refuse before any type exists is
/// pinned here as well.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class SubclassUriHandlerTests
{
    /// <summary>
    /// The path the whole interface exists for: a URI nobody has a wrapper for
    /// yet picks the managed factory, the element is built, and its
    /// <c>set_uri</c> reaches the managed instance.
    /// </summary>
    [Fact]
    public void MakingAnElementFromAUriReachesTheManagedHandler()
    {
        Assert.True(ProbeUriElement.IsRegistered);

        ProbeUriElement.Reset();

        using Element made = Element.MakeFromUri(URIType.Src, "gstsharptest://first", null);
        ProbeUriElement probe = Assert.IsType<ProbeUriElement>(made);

        Assert.Equal(1, probe.SetUriCalls);
        Assert.Equal("gstsharptest://first", probe.Uri);

        // The wrapper was fabricated on this path and only once: nothing in C#
        // asked for the element before gst_element_make_from_uri built it.
        Assert.Equal(1, ProbeUriElement.WrappersCreated);
    }

    /// <summary>
    /// The consumer surface of the interface, over an instance of the managed
    /// type: what <c>set_uri</c> stored is what <c>get_uri</c> answers, and a
    /// second URI replaces the first.
    /// </summary>
    [Fact]
    public void TheUriRoundTripsThroughTheConsumerSurface()
    {
        Assert.True(ProbeUriElement.IsRegistered);

        using Element made = Element.MakeFromUri(URIType.Src, "gstsharptest://round-trip", null);
        IURIHandler handler = made.As<IURIHandler>()
            ?? throw new InvalidOperationException("The managed element is not a URI handler.");

        Assert.Equal("gstsharptest://round-trip", handler.GetUri());

        Assert.True(handler.SetUri("gstsharptest://second"));
        Assert.Equal("gstsharptest://second", handler.GetUri());
        Assert.Equal("gstsharptest://second", Assert.IsType<ProbeUriElement>(made).Uri);
    }

    /// <summary>
    /// The two slots that are asked about the type rather than about an
    /// instance answer what the type declared. The instance path is the raw
    /// pointer one - <c>gst_uri_handler_get_protocols</c> hands the pinned
    /// array straight to the caller - so this is what pins the lifetime rule.
    /// </summary>
    [Fact]
    public void TheTypeKeyedSlotsAnswerWhatTheTypeDeclared()
    {
        Assert.True(ProbeUriElement.IsRegistered);

        using Element made = Element.MakeFromUri(URIType.Src, "gstsharptest://declared", null);
        IURIHandler handler = made.As<IURIHandler>()
            ?? throw new InvalidOperationException("The managed element is not a URI handler.");

        Assert.Equal(ProbeUriElement.UriType, handler.GetUriType());
        Assert.Equal(new[] { ProbeUriElement.Protocol }, handler.GetProtocols());
    }

    /// <summary>
    /// A refusal that names no reason still reaches the caller as a
    /// <c>GST_URI_ERROR</c>: GStreamer synthesises none of its own and
    /// <c>gst_element_make_from_uri</c> reads the message of whatever is there.
    /// </summary>
    [Fact]
    public void ARefusalWithoutAReasonArrivesAsASynthesisedError()
    {
        Assert.True(ProbeUriElement.IsRegistered);

        GException error = Assert.Throws<GException>(
            () => Element.MakeFromUri(URIType.Src, "gstsharptest://refused", null));

        Assert.Equal(URIErrorExtensions.Quark(), error.Domain);
        Assert.Equal((int)URIError.BadUri, error.Code);
        Assert.Contains(ProbeUriElement.GTypeName, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal that does name a reason keeps it. A handler author writes a
    /// <c>GException</c> with a message and no domain, so the message is what
    /// travels and the runtime supplies the <c>GST_URI_ERROR</c> around it.
    /// </summary>
    [Fact]
    public void ARefusalThatNamesAReasonKeepsIt()
    {
        Assert.True(ProbeUriElement.IsRegistered);

        GException error = Assert.Throws<GException>(
            () => Element.MakeFromUri(URIType.Src, "gstsharptest://reason", null));

        Assert.Equal(ProbeUriElement.StatedReason, error.Message);
        Assert.Equal(URIErrorExtensions.Quark(), error.Domain);
        Assert.Equal((int)URIError.BadUri, error.Code);
    }

    /// <summary>
    /// The same for a type that refuses everything, which is the candidate
    /// <c>gst_element_make_from_uri</c> would have dereferenced a null error
    /// for. Nothing crashes and the loop ends with the synthesised error.
    /// </summary>
    [Fact]
    public void ATypeThatRefusesEverythingDoesNotCrashTheAutoplugPath()
    {
        Assert.True(ProbeFailingUriElement.IsRegistered);

        GException error = Assert.Throws<GException>(
            () => Element.MakeFromUri(URIType.Src, "gstsharpfail://anything", null));

        Assert.Equal(URIErrorExtensions.Quark(), error.Domain);
        Assert.Equal((int)URIError.BadUri, error.Code);
    }

    /// <summary>The new type really conforms to the interface.</summary>
    [Fact]
    public void TheRegisteredTypeReportsTheInterface()
    {
        Assert.True(ProbeUriElement.IsRegistered);

        GType uriHandler = new(UriHandlerGetType());

        Assert.Contains(uriHandler, ProbeUriElement.RegisteredType.GetInterfaces());
        Assert.True(ProbeUriElement.RegisteredType.IsA(uriHandler));
    }

    /// <summary>
    /// The wrapper the fabrication built owns the instance: the element is sunk
    /// once a bin holds it, and disposing the wrapper after the bin is gone
    /// frees it.
    /// </summary>
    [Fact]
    public void TheFabricatedWrapperOwnsTheElement()
    {
        Assert.True(ProbeUriElement.IsRegistered);

        ProbeUriElement.Reset();

        Element made = Element.MakeFromUri(URIType.Src, "gstsharptest://owned", null);
        nint handle = made.Handle;

        try
        {
            Assert.Equal(1, ProbeUriElement.WrappersCreated);

            using (Bin bin = Bin.New("uri-owner") ?? throw new InvalidOperationException("No bin."))
            {
                Assert.True(bin.Add(made));
                Assert.Equal(0, GObjectNative.ObjectIsFloating(handle));
            }

            WeakProbe.Arm(handle);
        }
        finally
        {
            made.Dispose();
        }

        Assert.Equal(1, WeakProbe.Freed);
    }

    /// <summary>
    /// A type whose protocol list is empty is refused before anything is
    /// registered: an element factory with no protocol would never be picked
    /// and GStreamer would refuse it anyway.
    /// </summary>
    [Fact]
    public void AnEmptyProtocolListIsRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => URIHandlerImplementation.For<NoProtocolsElement>());

        Assert.Contains("Protocols", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A handler that is neither a source nor a sink is refused:
    /// <c>GST_URI_TYPE_IS_VALID</c> is what <c>gst_element_register</c> checks.
    /// </summary>
    [Fact]
    public void AHandlerThatIsNeitherSourceNorSinkIsRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => URIHandlerImplementation.For<UnknownUriTypeElement>());

        Assert.Contains("UriType", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same interface twice is a caller mistake, and it is caught before
    /// the <c>GType</c> is registered: a static type cannot be unregistered, so
    /// a name a failed registration consumed would be lost for the process.
    /// </summary>
    [Fact]
    public void DeclaringTheSameInterfaceTwiceIsRefused()
    {
        SubclassOptions options = new()
        {
            Interfaces =
            [
                URIHandlerImplementation.For<ProbeUriElement>(),
                URIHandlerImplementation.For<ProbeUriElement>(),
            ],
        };

        _ = Assert.Throws<ArgumentException>(
            () => PushSrc.DefineSubclass<ProbeUriElement>(
                "GstSharpTestDuplicateInterface",
                static _ => { },
                options,
                PushSrc.CreateOverride));

        // Nothing was registered, so the name is still free.
        Assert.Equal(0u, TypeFromName("GstSharpTestDuplicateInterface"));
    }

    /// <summary>
    /// An interface the parent implements already is refused too: GLib would
    /// hand the subclass a copy of the parent's slots, and a managed
    /// implementation has no way to chain up through those.
    /// </summary>
    [Fact]
    public void AnInterfaceTheParentImplementsIsRefused()
    {
        GType childProxy = new(ChildProxyGetType());

        Assert.True(new GType(Bin.GetGType()).IsA(childProxy));

        SubclassOptions options = new() { Interfaces = [new ChildProxyDeclaration(childProxy)] };

        _ = Assert.Throws<ArgumentException>(
            () => Bin.DefineSubclass<NeverRegisteredBin>(
                "GstSharpTestParentInterface",
                static _ => { },
                options));
    }

    [LibraryImport("Gst", EntryPoint = "gst_uri_handler_get_type")]
    private static partial nuint UriHandlerGetType();

    [LibraryImport("Gst", EntryPoint = "gst_child_proxy_get_type")]
    private static partial nuint ChildProxyGetType();

    [LibraryImport("GObject", EntryPoint = "g_type_from_name", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint TypeFromName(string name);

    /// <summary>A declaration of an interface whose vtable is never filled in.</summary>
    private sealed class ChildProxyDeclaration : InterfaceImplementation
    {
        /// <summary>Names the interface.</summary>
        /// <param name="interfaceType">The type of the interface.</param>
        internal ChildProxyDeclaration(GType interfaceType)
            : base(interfaceType)
        {
        }

        /// <inheritdoc/>
        internal override unsafe void InitializeVTable(void* iface, GType instanceType) =>
            throw new NotSupportedException("The declaration is refused before it is used.");
    }

    /// <summary>A bin subclass that is only ever used to be refused.</summary>
    private sealed class NeverRegisteredBin : Bin, IManagedSubclass<NeverRegisteredBin>
    {
        private NeverRegisteredBin(SubclassCtorArgs args)
            : base(args)
        {
        }

        /// <summary>Builds the wrapper of an instance native code created.</summary>
        /// <param name="args">What the runtime says about the instance.</param>
        /// <returns>The wrapper.</returns>
        public static NeverRegisteredBin CreateWrapper(SubclassCtorArgs args) => new(args);
    }

    /// <summary>A handler that answers an empty protocol list.</summary>
    private sealed class NoProtocolsElement : PushSrc, IManagedSubclass<NoProtocolsElement>,
        IURIHandlerImplementation
    {
        private NoProtocolsElement(SubclassCtorArgs args)
            : base(args)
        {
        }

        /// <inheritdoc/>
        public static URIType UriType => URIType.Src;

        /// <inheritdoc/>
        public static IReadOnlyList<string> Protocols => [];

        /// <summary>Builds the wrapper of an instance native code created.</summary>
        /// <param name="args">What the runtime says about the instance.</param>
        /// <returns>The wrapper.</returns>
        public static NoProtocolsElement CreateWrapper(SubclassCtorArgs args) => new(args);

        /// <inheritdoc/>
        public string? GetUri() => null;

        /// <inheritdoc/>
        public bool SetUri(string uri, out GException? error)
        {
            _ = uri;
            error = null;
            return false;
        }
    }

    /// <summary>A handler that is neither a source nor a sink.</summary>
    private sealed class UnknownUriTypeElement : PushSrc, IManagedSubclass<UnknownUriTypeElement>,
        IURIHandlerImplementation
    {
        private UnknownUriTypeElement(SubclassCtorArgs args)
            : base(args)
        {
        }

        /// <inheritdoc/>
        public static URIType UriType => URIType.Unknown;

        /// <inheritdoc/>
        public static IReadOnlyList<string> Protocols => ["gstsharpunknown"];

        /// <summary>Builds the wrapper of an instance native code created.</summary>
        /// <param name="args">What the runtime says about the instance.</param>
        /// <returns>The wrapper.</returns>
        public static UnknownUriTypeElement CreateWrapper(SubclassCtorArgs args) => new(args);

        /// <inheritdoc/>
        public string? GetUri() => null;

        /// <inheritdoc/>
        public bool SetUri(string uri, out GException? error)
        {
            _ = uri;
            error = null;
            return false;
        }
    }
}
