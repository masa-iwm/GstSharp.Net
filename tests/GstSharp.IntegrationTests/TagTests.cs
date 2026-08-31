using System.Text;
using Gst;
using Gst.Tag;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstTag</c> binding against the library that is installed: the tables
/// the module ships (languages, licenses, ID3 genres) and the container formats
/// it converts a <see cref="TagList"/> to and from — ID3v1, ID3v2, Vorbis
/// comments, XMP and EXIF.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here needs an element, a pipeline or a plugin of its own: every
/// member under test is a plain function of <c>libgsttag</c>, and every one of
/// them shipped long before the 1.24 floor the Linux leg of the matrix runs
/// against, so no fact is gated on a version.
/// </para>
/// <para>
/// Three of them are the reason the module carries annotation corrections. The
/// EXIF facts prove the corrected return: a tag list EXIF has a slot for
/// serialises, and one it does not answers <see langword="null"/> rather than
/// throwing. The ID3v1 facts prove the length guard the fixed size argument
/// gets, and the Vorbis facts prove that the vendor string travels out of a
/// call whose C implementation reads that slot back on its error path.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class TagTests
{
    /// <summary>The byte order EXIF calls little endian, which is <c>G_LITTLE_ENDIAN</c>.</summary>
    private const int ByteOrderLittleEndian = 1234;

    /// <summary>A one by one RGBA PNG, small enough to write down and real enough to typefind.</summary>
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
        0x1F, 0x00, 0x05, 0x00, 0x01, 0xFF, 0x89, 0x99, 0x3D, 0x1D, 0x00, 0x00,
        0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    /// <summary>
    /// The identification header a Vorbis comment packet carries in front of
    /// its payload: the packet type byte and the codec name.
    /// </summary>
    private static readonly byte[] VorbisIdData = [0x03, 0x76, 0x6F, 0x72, 0x62, 0x69, 0x73];

    /// <summary>
    /// The table of ISO-639 codes is compiled into the library, so it is there
    /// whatever else the installation carries.
    /// </summary>
    [Fact]
    public void TheLanguageTableIsPopulated()
    {
        string[]? codes = TagGlobal.TagGetLanguageCodes();

        Assert.NotNull(codes);
        Assert.NotEmpty(codes);
        Assert.Contains("en", codes);

        // Every code the table hands out is one the checker recognises.
        Assert.True(TagGlobal.TagCheckLanguageCode(codes[0]));
    }

    /// <summary>
    /// A language name is looked up from a code, and an unknown code answers
    /// nothing rather than throwing.
    /// </summary>
    [Fact]
    public void ALanguageNameIsLookedUpFromItsCode()
    {
        // The name is translated when the library was built against iso-codes,
        // so only its presence can be asserted, not its spelling.
        Assert.NotNull(TagGlobal.TagGetLanguageName("en"));

        Assert.Null(TagGlobal.TagGetLanguageName("zzz"));
        Assert.False(TagGlobal.TagCheckLanguageCode("zzz"));
    }

    /// <summary>
    /// The three ISO-639 spellings of one language convert into each other:
    /// German is <c>de</c>, <c>ger</c> in the bibliographic set and <c>deu</c>
    /// in the terminological one.
    /// </summary>
    [Fact]
    public void TheThreeIsoSpellingsOfALanguageConvertIntoEachOther()
    {
        Assert.Equal("de", TagGlobal.TagGetLanguageCodeIso6391("ger"));
        Assert.Equal("de", TagGlobal.TagGetLanguageCodeIso6391("deu"));
        Assert.Equal("ger", TagGlobal.TagGetLanguageCodeIso6392B("de"));
        Assert.Equal("deu", TagGlobal.TagGetLanguageCodeIso6392T("de"));

        Assert.Null(TagGlobal.TagGetLanguageCodeIso6391("zzz"));
    }

    /// <summary>
    /// The Creative Commons table is read through every one of its accessors.
    /// </summary>
    [Fact]
    public void TheLicenseTableIsReadThroughEveryAccessor()
    {
        const string ByShareAlike = "http://creativecommons.org/licenses/by-sa/3.0/";

        string[]? licenses = TagGlobal.TagGetLicenses();

        Assert.NotNull(licenses);
        Assert.NotEmpty(licenses);
        Assert.Contains(ByShareAlike, licenses);

        TagLicenseFlags flags = TagGlobal.TagGetLicenseFlags(ByShareAlike);

        Assert.True(flags.HasFlag(TagLicenseFlags.CreativeCommonsLicense));
        Assert.True(flags.HasFlag(TagLicenseFlags.RequiresAttribution));
        Assert.True(flags.HasFlag(TagLicenseFlags.RequiresShareAlike));
        Assert.True(flags.HasFlag(TagLicenseFlags.PermitsDerivativeWorks));
        Assert.False(flags.HasFlag(TagLicenseFlags.ProhibitsCommercialUse));

        // The nick is built from the reference and is untranslated, so it can
        // be spelled out; the title is translated and only its presence is
        // asserted. The description is not asserted at all: the table carries
        // one per license and this entry has none, which the accessor answers
        // as null the same way an unknown reference does.
        Assert.Equal("CC BY-SA 3.0", TagGlobal.TagGetLicenseNick(ByShareAlike));
        Assert.Equal("3.0", TagGlobal.TagGetLicenseVersion(ByShareAlike));
        Assert.NotNull(TagGlobal.TagGetLicenseTitle(ByShareAlike));

        // The generic version of a license carries no jurisdiction.
        Assert.Null(TagGlobal.TagGetLicenseJurisdiction(ByShareAlike));
    }

    /// <summary>
    /// A reference the table does not know is nothing, not an exception, from
    /// every accessor at once.
    /// </summary>
    [Fact]
    public void AnUnknownLicenseReferenceIsNothing()
    {
        const string Unknown = "http://example.invalid/licenses/nope/1.0/";

        Assert.Equal(default, TagGlobal.TagGetLicenseFlags(Unknown));
        Assert.Null(TagGlobal.TagGetLicenseNick(Unknown));
        Assert.Null(TagGlobal.TagGetLicenseTitle(Unknown));
        Assert.Null(TagGlobal.TagGetLicenseDescription(Unknown));
        Assert.Null(TagGlobal.TagGetLicenseVersion(Unknown));
        Assert.Null(TagGlobal.TagGetLicenseJurisdiction(Unknown));
    }

    /// <summary>
    /// The ID3v1 genre table is there, and the name tables translate an ID3v2
    /// frame identifier and a Vorbis comment key into a GStreamer tag and back.
    /// </summary>
    [Fact]
    public void TheId3AndVorbisNameTablesTranslateBothWays()
    {
        Assert.True(TagGlobal.TagId3GenreCount() > 0);
        Assert.Equal("Blues", TagGlobal.TagId3GenreGet(0));
        Assert.Null(TagGlobal.TagId3GenreGet(TagGlobal.TagId3GenreCount()));

        Assert.Equal("title", TagGlobal.TagFromId3Tag("TIT2"));
        Assert.Equal("TIT2", TagGlobal.TagToId3Tag("title"));
        Assert.Null(TagGlobal.TagFromId3Tag("ZZZZ"));

        Assert.Equal("title", TagGlobal.TagFromVorbisTag("TITLE"));
        Assert.Equal("TITLE", TagGlobal.TagToVorbisTag("title"));
        Assert.Null(TagGlobal.TagFromVorbisTag("NO-SUCH-KEY"));
    }

    /// <summary>
    /// A hand written ID3v1 record parses into the tags it carries.
    /// </summary>
    [Fact]
    public void AnId3v1RecordParsesIntoATagList()
    {
        byte[] record = BuildId3v1("Take Five", "Dave Brubeck", "Time Out");

        using TagList? tags = TagGlobal.TagListNewFromId3v1(record);

        Assert.NotNull(tags);
        Assert.True(tags.GetString("title", out string? title));
        Assert.Equal("Take Five", title);
        Assert.True(tags.GetString("artist", out string? artist));
        Assert.Equal("Dave Brubeck", artist);
        Assert.True(tags.GetString("album", out string? album));
        Assert.Equal("Time Out", album);

        // The record has to start with "TAG"; anything else is not an ID3v1
        // record and is answered with nothing.
        Assert.Null(TagGlobal.TagListNewFromId3v1(new byte[128]));
    }

    /// <summary>
    /// The ID3v1 parser reads a fixed 128 bytes without being told a length, so
    /// the binding measures the span rather than letting the call read past its
    /// end.
    /// </summary>
    [Fact]
    public void AnId3v1RecordOfTheWrongLengthIsRefused()
    {
        ArgumentException tooShort = Assert.Throws<ArgumentException>(
            static () => TagGlobal.TagListNewFromId3v1(new byte[127]));
        Assert.Equal("data", tooShort.ParamName);

        Assert.Throws<ArgumentException>(static () => TagGlobal.TagListNewFromId3v1(default));
        Assert.Throws<ArgumentException>(static () => TagGlobal.TagListNewFromId3v1(new byte[129]));
    }

    /// <summary>
    /// A tag list survives a round trip through a Vorbis comment packet, and
    /// the vendor string comes back with it.
    /// </summary>
    [Fact]
    public void ATagListSurvivesARoundTripThroughAVorbisComment()
    {
        using TagList written = TagList.NewEmpty();
        written.AddString(TagMergeMode.Replace, "title", "Take Five");
        written.AddString(TagMergeMode.Replace, "artist", "Dave Brubeck");

        using Gst.Buffer packet = TagGlobal.TagListToVorbiscommentBuffer(written, VorbisIdData, "GstSharp.Net");

        using TagList? read = TagGlobal.TagListFromVorbiscommentBuffer(packet, VorbisIdData, out string? vendor);

        Assert.NotNull(read);

        // The C implementation writes the vendor string before it reads the
        // first tag, so a list that came back has a vendor string with it.
        Assert.Equal("GstSharp.Net", vendor);
        Assert.True(read.GetString("title", out string? title));
        Assert.Equal("Take Five", title);
        Assert.True(read.GetString("artist", out string? artist));
        Assert.Equal("Dave Brubeck", artist);
    }

    /// <summary>
    /// The same packet parses from a span as well, and a packet that does not
    /// begin with the identification data it is read with is refused — with the
    /// vendor string left at <see langword="null"/> rather than at whatever an
    /// uninitialised slot held.
    /// </summary>
    [Fact]
    public void AVorbisCommentIsAlsoParsedFromASpan()
    {
        using TagList written = TagList.NewEmpty();
        written.AddString(TagMergeMode.Replace, "title", "Blue Rondo");

        using Gst.Buffer packet = TagGlobal.TagListToVorbiscommentBuffer(written, VorbisIdData, null);
        byte[] bytes;
        using (Gst.Buffer.MapScope map = packet.Map(MapFlags.Read))
        {
            bytes = map.Span.ToArray();
        }

        using (TagList? read = TagGlobal.TagListFromVorbiscomment(bytes, VorbisIdData, out string? vendor))
        {
            Assert.NotNull(read);

            // A null vendor string is the C spelling of "use the default one",
            // so what comes back out is that default rather than nothing.
            Assert.NotNull(vendor);
            Assert.True(read.GetString("title", out string? title));
            Assert.Equal("Blue Rondo", title);
        }

        // Read with identification data the packet does not begin with: the
        // parse fails, and both the list and the vendor string are nothing.
        byte[] wrongId = [0x03, 0x6E, 0x6F, 0x70, 0x65, 0x21, 0x21];
        using (TagList? refused = TagGlobal.TagListFromVorbiscomment(bytes, wrongId, out string? vendor))
        {
            Assert.Null(refused);
            Assert.Null(vendor);
        }
    }

    /// <summary>
    /// One tag of a list is converted into the <c>key=value</c> strings a
    /// Vorbis comment carries, which is a <c>GList</c> of strings the caller
    /// owns.
    /// </summary>
    [Fact]
    public void OneTagIsConvertedIntoItsVorbisComments()
    {
        using TagList list = TagList.NewEmpty();
        list.AddString(TagMergeMode.Append, "artist", "Dave Brubeck");
        list.AddString(TagMergeMode.Append, "artist", "Paul Desmond");

        IReadOnlyList<string> comments = TagGlobal.TagToVorbisComments(list, "artist");

        Assert.Equal(2, comments.Count);
        Assert.Contains("ARTIST=Dave Brubeck", comments);
        Assert.Contains("ARTIST=Paul Desmond", comments);

        // A tag the list does not carry converts into nothing at all, which the
        // C function answers as an empty GList rather than as an error.
        Assert.Empty(TagGlobal.TagToVorbisComments(list, "title"));
    }

    /// <summary>
    /// A Vorbis comment key and value are added to a list straight, through the
    /// convenience entry point the parsers use.
    /// </summary>
    [Fact]
    public void AVorbisCommentStringIsAddedToAList()
    {
        using TagList list = TagList.NewEmpty();

        TagGlobal.VorbisTagAdd(list, "TITLE", "Take Five");

        Assert.True(list.GetString("title", out string? title));
        Assert.Equal("Take Five", title);
    }

    /// <summary>
    /// A tag list survives a round trip through an XMP packet, and the schemas
    /// the packet may be written with are enumerable.
    /// </summary>
    [Fact]
    public void ATagListSurvivesARoundTripThroughXmp()
    {
        string[]? schemas = TagGlobal.TagXmpListSchemas();

        Assert.NotNull(schemas);
        Assert.NotEmpty(schemas);

        using TagList written = TagList.NewEmpty();
        written.AddString(TagMergeMode.Replace, "title", "Take Five");

        // A null schema list is the C spelling of "every schema", which is what
        // the annotation correction of this argument is for: an empty array
        // would select no schema at all.
        using Gst.Buffer? packet = TagGlobal.TagListToXmpBuffer(written, readOnly: false, schemas: null);

        Assert.NotNull(packet);

        using TagList? read = TagGlobal.TagListFromXmpBuffer(packet);

        Assert.NotNull(read);
        Assert.True(read.GetString("title", out string? title));
        Assert.Equal("Take Five", title);
    }

    /// <summary>
    /// A tag list EXIF has a slot for serialises and parses back.
    /// </summary>
    [Fact]
    public void ATagListSurvivesARoundTripThroughExif()
    {
        using TagList written = TagList.NewEmpty();
        written.AddString(TagMergeMode.Replace, "artist", "Dave Brubeck");

        using Gst.Buffer? packet = TagGlobal.TagListToExifBufferWithTiffHeader(written);

        Assert.NotNull(packet);

        using TagList? read = TagGlobal.TagListFromExifBufferWithTiffHeader(packet);

        Assert.NotNull(read);
        Assert.True(read.GetString("artist", out string? artist));
        Assert.Equal("Dave Brubeck", artist);
    }

    /// <summary>
    /// The four EXIF conversions answer <see langword="null"/> on an ordinary
    /// input, which is what the corrected annotations of this module say and
    /// what the gir of the library does not.
    /// </summary>
    [Fact]
    public void TheExifConversionsAnswerNothingRatherThanThrowing()
    {
        // A list of no tag, and a list of a tag EXIF has no slot for, both
        // serialise into nothing: the writer answers NULL when the list carries
        // nothing this IFD can hold.
        using TagList empty = TagList.NewEmpty();
        Assert.Null(TagGlobal.TagListToExifBufferWithTiffHeader(empty));
        Assert.Null(TagGlobal.TagListToExifBuffer(empty, ByteOrderLittleEndian, 8));

        // "title" is a tag the core registers, so this fact assumes nothing
        // another one registered first, and EXIF has no slot for it: none of
        // the three tag maps of gstexiftag.c names it.
        using TagList unmapped = TagList.NewEmpty();
        unmapped.AddString(TagMergeMode.Replace, "title", "Take Five");
        Assert.Null(TagGlobal.TagListToExifBufferWithTiffHeader(unmapped));

        // A buffer that is not an EXIF IFD parses into nothing as well.
        using Gst.Buffer garbage = Gst.Buffer.NewMemdup(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
        Assert.Null(TagGlobal.TagListFromExifBufferWithTiffHeader(garbage));
    }

    /// <summary>
    /// An extended comment splits into its key, its language and its value, and
    /// the two optional halves are absent rather than empty when the string
    /// carries neither.
    /// </summary>
    [Fact]
    public void AnExtendedCommentSplitsIntoItsThreeParts()
    {
        Assert.True(TagGlobal.TagParseExtendedComment(
            "description[en]=a short one",
            out string? key,
            out string? lang,
            out string? value,
            failIfNoKey: true));
        Assert.Equal("description", key);
        Assert.Equal("en", lang);
        Assert.Equal("a short one", value);

        // No language: the key is there and the language is not.
        Assert.True(TagGlobal.TagParseExtendedComment(
            "description=a short one",
            out key,
            out lang,
            out value,
            failIfNoKey: true));
        Assert.Equal("description", key);
        Assert.Null(lang);
        Assert.Equal("a short one", value);

        // No key at all: refused when the caller insists on one, and the whole
        // string is the value when it does not. The refusal leaves every out
        // parameter at null, which is what the zero initialised slots of the
        // generated member are for: the C function writes none of them.
        Assert.False(TagGlobal.TagParseExtendedComment(
            "a short one",
            out key,
            out lang,
            out value,
            failIfNoKey: true));
        Assert.Null(key);
        Assert.Null(lang);
        Assert.Null(value);

        Assert.True(TagGlobal.TagParseExtendedComment(
            "a short one",
            out key,
            out lang,
            out value,
            failIfNoKey: false));
        Assert.Null(key);
        Assert.Null(lang);
        Assert.Equal("a short one", value);
    }

    /// <summary>
    /// Image data is typefound into a sample whose caps carry the media type,
    /// and data of no known type is nothing rather than a sample of a guess.
    /// </summary>
    [Fact]
    public void ImageDataIsTypefoundIntoASample()
    {
        using Sample? sample = TagGlobal.TagImageDataToImageSample(OnePixelPng, TagImageType.FrontCover);

        Assert.NotNull(sample);

        using Caps? caps = sample.GetCaps();
        Assert.NotNull(caps);
        Assert.Contains("image/png", caps.ToString(), StringComparison.Ordinal);

        using Gst.Buffer? payload = sample.GetBuffer();
        Assert.NotNull(payload);

        // The trailing NUL the C function appends for the URI case is cut back
        // off for an image, so the sample carries exactly the bytes it was
        // given.
        using (Gst.Buffer.MapScope map = payload.Map(MapFlags.Read))
        {
            Assert.Equal(OnePixelPng, map.Span.ToArray());
        }

        Assert.Null(TagGlobal.TagImageDataToImageSample(
            [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08],
            TagImageType.FrontCover));
    }

    /// <summary>
    /// A string of unknown encoding is converted to UTF-8, and one that already
    /// is UTF-8 comes back as it was.
    /// </summary>
    [Fact]
    public void AFreeformStringIsConvertedToUtf8()
    {
        // The environment variable list is empty rather than absent: the C loop
        // over it stops at the first null entry either way, so an empty array
        // is the same instruction as a null pointer.
        string[] none = [];

        Assert.Equal(
            "Dave Brubeck",
            TagGlobal.TagFreeformStringToUtf8(AsSignedBytes("Dave Brubeck"), none));

        // Latin-1 bytes are not valid UTF-8, so a fallback encoding converts
        // them; what matters here is that something comes back rather than an
        // exception, since which fallback runs depends on the locale.
        sbyte[] latin1 = [0x44, 0x76, unchecked((sbyte)0xE1), 0x6B];
        Assert.NotNull(TagGlobal.TagFreeformStringToUtf8(latin1, none));
    }

    /// <summary>
    /// The size of an ID3v2 tag is read out of the buffer that starts with it,
    /// and a buffer that starts with something else measures zero.
    /// </summary>
    [Fact]
    public void TheSizeOfAnId3v2TagIsReadFromItsHeader()
    {
        // "ID3", version 3.0, no flags, and a syncsafe size of one byte.
        byte[] header = [0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00];

        using Gst.Buffer tag = Gst.Buffer.NewMemdup(header);

        // Ten bytes of header plus the one byte the header declares.
        Assert.Equal(11u, TagGlobal.TagGetId3v2TagSize(tag));

        using Gst.Buffer notATag = Gst.Buffer.NewMemdup(new byte[16]);
        Assert.Equal(0u, TagGlobal.TagGetId3v2TagSize(notATag));
    }

    /// <summary>
    /// An image is attached to a tag list under the ID3 picture type it was
    /// given, and the sample that comes back out carries the image.
    /// </summary>
    [Fact]
    public void AnImageIsAddedToATagListUnderItsId3PictureType()
    {
        using TagList list = TagList.NewEmpty();

        // 3 is the ID3v2 APIC picture type of a front cover.
        Assert.True(TagGlobal.TagListAddId3Image(list, OnePixelPng, 3));

        Assert.True(list.GetSample("image", out Sample? image));
        using (image)
        {
            Assert.NotNull(image);
            using Caps? caps = image.GetCaps();
            Assert.NotNull(caps);
            Assert.Contains("image/png", caps.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Registering the MusicBrainz tags is idempotent, and the tags it adds are
    /// known to the tag system afterwards.
    /// </summary>
    [Fact]
    public void TheMusicbrainzTagsAreRegistered()
    {
        TagGlobal.TagRegisterMusicbrainzTags();
        TagGlobal.TagRegisterMusicbrainzTags();

        Assert.True(Global.TagExists("musicbrainz-trackid"));
    }

    /// <summary>
    /// Builds the 128 byte ID3v1 record the parser expects.
    /// </summary>
    /// <param name="title">The title, at most thirty bytes.</param>
    /// <param name="artist">The artist, at most thirty bytes.</param>
    /// <param name="album">The album, at most thirty bytes.</param>
    /// <returns>The record.</returns>
    private static byte[] BuildId3v1(string title, string artist, string album)
    {
        byte[] record = new byte[128];
        Encoding.ASCII.GetBytes("TAG").CopyTo(record, 0);
        Encoding.ASCII.GetBytes(title).CopyTo(record, 3);
        Encoding.ASCII.GetBytes(artist).CopyTo(record, 33);
        Encoding.ASCII.GetBytes(album).CopyTo(record, 63);
        return record;
    }

    /// <summary>
    /// Reads a string as the signed bytes the C declaration of
    /// <c>gst_tag_freeform_string_to_utf8</c> takes.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The bytes of the text, read as signed.</returns>
    private static sbyte[] AsSignedBytes(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        sbyte[] signed = new sbyte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            signed[i] = unchecked((sbyte)bytes[i]);
        }

        return signed;
    }
}
