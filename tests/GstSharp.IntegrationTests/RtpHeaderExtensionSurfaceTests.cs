using Gst;
using Gst.Rtp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The header extension surface of a payloader and a depayloader: the
/// <c>Extensions</c> snapshot and the <c>AddExtension</c> and
/// <c>ClearExtensions</c> members that emit the two action signals of the C.
/// </summary>
/// <remarks>
/// <para>
/// <c>rtpL16pay</c> and <c>rtpL16depay</c> are in the required element set of
/// the legs that promise one; <c>rtphdrextclientaudiolevel</c> - the RFC 6464
/// client-to-mixer audio level extension - comes from <c>rtpmanager</c> and is
/// promised by nobody, so the tests skip where it is not installed, the way
/// every other test of an optional plugin does.
/// </para>
/// <para>
/// The id of 0 the C refuses with a critical and a silent no-op is raised on
/// instead, which is asserted before a usable id is set and the extension is
/// added for real. An extension that was never given an id carries
/// <c>G_MAXUINT32</c> rather than 0 (<c>gstrtphdrext.c:200</c>), which the add
/// check of the C lets through and every later use of the extension refuses, so
/// the test sets the refused id itself.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtpHeaderExtensionSurfaceTests
{
    /// <summary>The URI of the extension the tests add.</summary>
    private const string Uri = "urn:ietf:params:rtp-hdrext:ssrc-audio-level";

    /// <summary>The id the extension is given.</summary>
    private const uint Id = 1;

    /// <summary>
    /// A payloader hands out what was added to it, and nothing once it has been
    /// cleared.
    /// </summary>
    [RequiresElementFact("rtpL16pay", "rtphdrextclientaudiolevel")]
    public void APayloaderCarriesTheExtensionsItWasGiven() => AnElementCarriesTheExtensionsItWasGiven(
        "rtpL16pay",
        static (element, extension) => ((RTPBasePayload)element).AddExtension(extension),
        static element => ((RTPBasePayload)element).ClearExtensions(),
        static element => ((RTPBasePayload)element).Extensions);

    /// <summary>
    /// A depayloader hands out what was added to it, and nothing once it has
    /// been cleared.
    /// </summary>
    [RequiresElementFact("rtpL16depay", "rtphdrextclientaudiolevel")]
    public void ADepayloaderCarriesTheExtensionsItWasGiven() => AnElementCarriesTheExtensionsItWasGiven(
        "rtpL16depay",
        static (element, extension) => ((RTPBaseDepayload)element).AddExtension(extension),
        static element => ((RTPBaseDepayload)element).ClearExtensions(),
        static element => ((RTPBaseDepayload)element).Extensions);

    /// <summary>The body both tests above share.</summary>
    /// <param name="factory">The factory of the element to measure.</param>
    /// <param name="add">Adds an extension to the element.</param>
    /// <param name="clear">Removes every extension from the element.</param>
    /// <param name="read">Reads the extensions of the element.</param>
    private static void AnElementCarriesTheExtensionsItWasGiven(
        string factory,
        Action<Element, RTPHeaderExtension> add,
        Action<Element> clear,
        Func<Element, IReadOnlyList<RTPHeaderExtension>> read)
    {
        using Element? element = ElementFactory.Make(factory, null);
        Assert.NotNull(element);

        // Nothing has been added and nothing has been negotiated, so the
        // property is an empty array rather than an absent one.
        Assert.Empty(read(element));

        using RTPHeaderExtension? extension = RTPHeaderExtension.CreateFromUri(Uri);
        Assert.NotNull(extension);

        // An extension that was never given an id carries G_MAXUINT32 rather
        // than zero (gstrtphdrext.c:200), so the id the C refuses is set here
        // on purpose.
        Assert.Equal(uint.MaxValue, extension.GetId());
        extension.SetId(0);

        ArgumentException refused = Assert.Throws<ArgumentException>(() => add(element, extension));
        Assert.Equal("extension", refused.ParamName);
        Assert.Empty(read(element));

        Assert.Throws<ArgumentNullException>(() => add(element, null!));

        extension.SetId(Id);
        add(element, extension);

        IReadOnlyList<RTPHeaderExtension> added = read(element);
        RTPHeaderExtension carried = Assert.Single(added);

        // A GObject wrapper is interned, so the element answers the very
        // instance that was added rather than a second wrapper of it.
        Assert.Same(extension, carried);
        Assert.Equal(Id, carried.GetId());
        Assert.Equal(Uri, carried.GetUri());

        // The list is a snapshot: clearing the element leaves it as it was.
        clear(element);

        Assert.Empty(read(element));
        Assert.Single(added);
    }
}
