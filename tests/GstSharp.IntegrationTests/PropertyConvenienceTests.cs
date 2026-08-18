using Gst;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Setting and reading a property by name in one call, which is what an
/// application does against every element whose properties no <c>.gir</c> file
/// describes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Gst.GObject.Object.SetProperty(string, object?)"/> converts what
/// it is given against the type the property declares, and
/// <see cref="Gst.GObject.Object.GetProperty{T}(string)"/> reads the content
/// back as the managed type the caller names.
/// <see cref="PropertyIntrospectionTests"/> covers the <c>GValue</c> layer these
/// two are built on.
/// </para>
/// <para>
/// The elements are the ones that ship with GStreamer core, so that the suite
/// runs wherever the library does: <c>fakesink</c> alone carries a boolean, a
/// string, a 32 and a 64 bit integer, a read only boxed value and a read only
/// mini object. <c>videotestsrc</c> is the one plugin element here, and it is
/// gated: its <c>pattern</c> is an enumeration whose managed type no binding
/// declares, which is the case the number path exists for.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class PropertyConvenienceTests
{
    /// <summary>
    /// A boolean, a string, a 32 bit and a 64 bit integer round trip through the
    /// pair, which is the whole point of it.
    /// </summary>
    [Fact]
    public void TheCommonScalarsRoundTrip()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "scalars"));

        sink.SetProperty("sync", true);
        Assert.True(sink.GetProperty<bool>("sync"));

        sink.SetProperty("sync", false);
        Assert.False(sink.GetProperty<bool>("sync"));

        sink.SetProperty("name", "renamed");
        Assert.Equal("renamed", sink.GetProperty<string>("name"));

        sink.SetProperty("num-buffers", 7);
        Assert.Equal(7, sink.GetProperty<int>("num-buffers"));

        sink.SetProperty("ts-offset", 5_000_000_000L);
        Assert.Equal(5_000_000_000L, sink.GetProperty<long>("ts-offset"));

        // An unsigned property takes a plain int on the way in, because it
        // widens without loss, and comes back as what it is.
        sink.SetProperty("blocksize", 8192);
        Assert.Equal(8192u, sink.GetProperty<uint>("blocksize"));
    }

    /// <summary>
    /// A double precision property round trips as well, and an integer is
    /// accepted on the way in because it widens into one.
    /// </summary>
    [Fact]
    public void ADoubleRoundTrips()
    {
        using Element queue = Assert.IsAssignableFrom<Element>(ElementFactory.Make("multiqueue", "watermarks"));

        queue.SetProperty("high-watermark", 0.75);
        Assert.Equal(0.75, queue.GetProperty<double>("high-watermark"));

        queue.SetProperty("low-watermark", 0);
        Assert.Equal(0.0, queue.GetProperty<double>("low-watermark"));
    }

    /// <summary>
    /// The type a property declares is what decides which managed types it can
    /// be read as, and the number it happens to hold never is.
    /// </summary>
    /// <remarks>
    /// A read that succeeds for the values seen in testing and throws for the
    /// one seen in production is what the rule rules out, so the assertions
    /// below are made with numbers that would fit either way.
    /// </remarks>
    [Fact]
    public void NumbersWidenByDeclaredTypeAndNeverNarrow()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "widening"));

        sink.SetProperty("num-buffers", 12);

        // A gint property is itself and widens into a long.
        Assert.Equal(12, sink.GetProperty<int>("num-buffers"));
        Assert.Equal(12L, sink.GetProperty<long>("num-buffers"));

        // It never reads as an unsigned type, although 12 would fit in one.
        Assert.Throws<InvalidCastException>(() => sink.GetProperty<uint>("num-buffers"));
        Assert.Throws<InvalidCastException>(() => sink.GetProperty<ulong>("num-buffers"));

        // A guint property is the mirror image: unsigned and wider, never an
        // int, although 4096 would fit in one.
        sink.SetProperty("blocksize", 4096);
        Assert.Equal(4096u, sink.GetProperty<uint>("blocksize"));
        Assert.Equal(4096ul, sink.GetProperty<ulong>("blocksize"));
        Assert.Throws<InvalidCastException>(() => sink.GetProperty<int>("blocksize"));

        // A gint64 property is not narrowed into an int either.
        sink.SetProperty("ts-offset", 1L);
        Assert.Equal(1L, sink.GetProperty<long>("ts-offset"));
        Assert.Throws<InvalidCastException>(() => sink.GetProperty<int>("ts-offset"));

        // And an integer property is not read as a floating point number.
        Assert.Throws<InvalidCastException>(() => sink.GetProperty<double>("ts-offset"));

        // The other way round holds as well: a gdouble property is not narrowed
        // into a float, and is not an integer.
        using Element queue = Assert.IsAssignableFrom<Element>(ElementFactory.Make("multiqueue", "narrowing"));
        Assert.Throws<InvalidCastException>(() => queue.GetProperty<float>("high-watermark"));
        Assert.Throws<InvalidCastException>(() => queue.GetProperty<long>("high-watermark"));
    }

    /// <summary>
    /// An enumeration property whose managed type a binding does declare reads
    /// as that type.
    /// </summary>
    /// <remarks>
    /// A pad is the object that has one: <c>direction</c> is a
    /// <c>GstPadDirection</c>, which the generated bindings spell
    /// <see cref="PadDirection"/>.
    /// </remarks>
    [Fact]
    public void AnEnumerationPropertyReadsAsItsManagedType()
    {
        using Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "directed"));

        // The pad is an interned GObject wrapper and is not disposed here.
        Pad source = Assert.IsAssignableFrom<Pad>(filter.GetStaticPad("src"));
        Pad sink = Assert.IsAssignableFrom<Pad>(filter.GetStaticPad("sink"));

        Assert.Equal(PadDirection.Src, source.GetProperty<PadDirection>("direction"));
        Assert.Equal(PadDirection.Sink, sink.GetProperty<PadDirection>("direction"));

        // The same member as the number it is, which is the path an enumeration
        // that no binding declares takes.
        Assert.Equal(1, source.GetProperty<int>("direction"));
    }

    /// <summary>
    /// The enumeration of a plugin, whose managed type no binding declares, is
    /// written and read as its number.
    /// </summary>
    /// <remarks>
    /// <c>videotestsrc</c> is not in any <c>.gir</c> file, so
    /// <c>GstVideoTestSrcPattern</c> has no managed type at all: 18 is
    /// <c>ball</c>, and the number is what travels.
    /// </remarks>
    [RequiresElementFact("videotestsrc")]
    public void APluginEnumerationTravelsAsANumber()
    {
        using Element source = Assert.IsAssignableFrom<Element>(ElementFactory.Make("videotestsrc", "patterned"));

        Assert.Equal(0, source.GetProperty<int>("pattern"));

        source.SetProperty("pattern", 18);
        Assert.Equal(18, source.GetProperty<int>("pattern"));
    }

    /// <summary>
    /// A mini object property round trips, and what comes back is an owner of
    /// its own rather than a window into the element.
    /// </summary>
    [Fact]
    public void AMiniObjectPropertyRoundTrips()
    {
        const string CapsText = "video/x-raw, format=(string)I420, width=(int)320, height=(int)240";

        using Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "boxed"));

        using (Caps written = Assert.IsType<Caps>(Caps.FromString(CapsText)))
        {
            filter.SetProperty("caps", written);

            // The write took a reference of its own, so the caller still owns
            // what it owned.
            Assert.False(written.IsDisposed);
        }

        using Caps? read = filter.GetProperty<Caps>("caps");
        Assert.NotNull(read);
        Assert.Equal(CapsText, read.ToString());
    }

    /// <summary>
    /// A boxed property reads as its wrapper, and one that holds nothing reads
    /// as <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <c>stats</c> of a <c>fakesink</c> is a <c>GstStructure</c>, which is a
    /// plain boxed value, and it always holds one. <c>last-sample</c> is a
    /// <c>GstSample</c>, which is a mini object, and it holds nothing until data
    /// has flowed — so the two together cover both object models and both
    /// answers.
    /// </remarks>
    [Fact]
    public void ABoxedPropertyReadsAsItsWrapperAndAnEmptyOneAsNull()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "boxed-read"));

        using Structure? stats = sink.GetProperty<Structure>("stats");
        Assert.NotNull(stats);
        Assert.NotEmpty(stats.GetName());
        Assert.Contains("rendered", stats.ToString(), StringComparison.Ordinal);

        // The wrapper is a copy of its own and outlives a second read.
        using Structure? again = sink.GetProperty<Structure>("stats");
        Assert.NotNull(again);
        Assert.NotEqual(stats.Handle, again.Handle);

        using Sample? none = sink.GetProperty<Sample>("last-sample");
        Assert.Null(none);
    }

    /// <summary>
    /// An object property reads as the interned wrapper of the object it holds,
    /// and as <see langword="null"/> when it holds none.
    /// </summary>
    [Fact]
    public void AnObjectPropertyReadsAsTheInternedWrapper()
    {
        using Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "parented"));

        // An element that belongs to nothing has no parent.
        Assert.Null(filter.GetProperty<Gst.Object>("parent"));

        // The parent of a static pad is the element it belongs to, and what
        // comes back is the wrapper the application already has rather than a
        // second one.
        Pad source = Assert.IsAssignableFrom<Pad>(filter.GetStaticPad("src"));
        Assert.Same(filter, source.GetProperty<Element>("parent"));
    }

    /// <summary>
    /// A name that is not a property of the object is an argument error from
    /// both calls, and the message says whose property it is not.
    /// </summary>
    [Fact]
    public void AnUnknownPropertyIsRefusedByBoth()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "unknown"));

        ArgumentException written = Assert.Throws<ArgumentException>(
            () => sink.SetProperty("no-such-property", 1));
        Assert.Contains("no-such-property", written.Message, StringComparison.Ordinal);
        Assert.Contains("GstFakeSink", written.Message, StringComparison.Ordinal);

        ArgumentException read = Assert.Throws<ArgumentException>(
            () => sink.GetProperty<int>("no-such-property"));
        Assert.Contains("no-such-property", read.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A type argument the property cannot supply is an
    /// <see cref="InvalidCastException"/> that names both the type of the
    /// property and the one that was asked for.
    /// </summary>
    [Fact]
    public void ATypeThePropertyCannotSupplyNamesBothSides()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "mistyped"));

        InvalidCastException mismatch = Assert.Throws<InvalidCastException>(
            () => sink.GetProperty<int>("name"));

        Assert.Contains("gchararray", mismatch.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(int).ToString(), mismatch.Message, StringComparison.Ordinal);

        // A wrapper type the property does not hold is the same answer.
        InvalidCastException wrongWrapper = Assert.Throws<InvalidCastException>(
            () => sink.GetProperty<Caps>("stats"));

        Assert.Contains("GstStructure", wrongWrapper.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Caps).ToString(), wrongWrapper.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A property that cannot be written is refused before anything reaches
    /// GLib, which would answer it with a warning on the console and a write
    /// that never happens.
    /// </summary>
    [Fact]
    public void APropertyThatCannotBeWrittenIsRefused()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "read-only"));

        // "last-sample" is readable and nothing else.
        ArgumentException readOnly = Assert.Throws<ArgumentException>(
            () => sink.SetProperty("last-sample", null));
        Assert.Contains("cannot be written", readOnly.Message, StringComparison.Ordinal);

        // "direction" of a pad is writable, but only while the pad is built.
        Pad pad = Assert.IsAssignableFrom<Pad>(sink.GetStaticPad("sink"));
        ArgumentException constructOnly = Assert.Throws<ArgumentException>(
            () => pad.SetProperty("direction", PadDirection.Src));
        Assert.Contains("constructor", constructOnly.Message, StringComparison.Ordinal);

        // The refusal changed nothing.
        Assert.Equal(PadDirection.Sink, pad.GetProperty<PadDirection>("direction"));
    }

    /// <summary>
    /// The readability guard turns nothing readable away: every property a class
    /// lists as readable is still readable through the call.
    /// </summary>
    /// <remarks>
    /// The refusal itself is the mirror of the one
    /// <see cref="APropertyThatCannotBeWrittenIsRefused"/> pins down, and it
    /// cannot be provoked from here: GStreamer ships no write only property, so
    /// what is asserted is the half that a wrong guard would break.
    /// </remarks>
    [Fact]
    public void EveryReadablePropertyStaysReadable()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "readable"));

        ParamSpec[] properties = sink.ListProperties();

        try
        {
            foreach (ParamSpec property in properties)
            {
                if ((property.Flags & ParamFlags.Readable) != 0)
                {
                    using Value value = sink.GetProperty(property.Name);
                    Assert.Equal(property.ValueType, value.Type);
                }
            }
        }
        finally
        {
            foreach (ParamSpec property in properties)
            {
                property.Dispose();
            }
        }
    }

    /// <summary>
    /// The round trip an application really writes: one call to set, one call to
    /// get, and what comes back is what went in.
    /// </summary>
    [Fact]
    public void WhatWasWrittenIsWhatIsRead()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "round-trip"));

        sink.SetProperty("sync", true);
        sink.SetProperty("max-lateness", 20_000_000L);
        sink.SetProperty("qos", true);
        sink.SetProperty("name", "the-sink");

        Assert.True(sink.GetProperty<bool>("sync"));
        Assert.Equal(20_000_000L, sink.GetProperty<long>("max-lateness"));
        Assert.True(sink.GetProperty<bool>("qos"));
        Assert.Equal("the-sink", sink.GetProperty<string>("name"));

        // And the untyped route agrees with the typed one, so the convenience
        // lane is the same lookup and not a second one.
        using Value raw = sink.GetProperty("max-lateness");
        Assert.Equal(20_000_000L, raw.GetInt64());
    }

    /// <summary>
    /// The description of a property answers in advance everything the by-name
    /// calls enforce: the flags, the declared type, and construct-only.
    /// </summary>
    [Fact]
    public void FindPropertyDescribesWhatTheByNameCallsEnforce()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "described"));

        using (ParamSpec? sync = sink.FindProperty("sync"))
        {
            Assert.NotNull(sync);
            Assert.Equal("sync", sync.Name);
            Assert.Equal(GType.Boolean, sync.ValueType);
            Assert.NotEqual(ParamFlags.None, sync.Flags & ParamFlags.Readable);
            Assert.NotEqual(ParamFlags.None, sync.Flags & ParamFlags.Writable);
        }

        // The read-only mini object: the refusal SetProperty answers for it is
        // visible here before any call is made.
        using (ParamSpec? lastSample = sink.FindProperty("last-sample"))
        {
            Assert.NotNull(lastSample);
            Assert.NotEqual(ParamFlags.None, lastSample.Flags & ParamFlags.Readable);
            Assert.Equal(ParamFlags.None, lastSample.Flags & ParamFlags.Writable);
        }

        // And the construct-only case, on the one reliably bound example: the
        // direction of a pad is given to its constructor and never again.
        using Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "flagged"));
        Pad pad = Assert.IsAssignableFrom<Pad>(filter.GetStaticPad("sink"));

        using (ParamSpec? direction = pad.FindProperty("direction"))
        {
            Assert.NotNull(direction);
            Assert.NotEqual(ParamFlags.None, direction.Flags & ParamFlags.ConstructOnly);
        }
    }

    /// <summary>
    /// Absence is an answer for the lookup and an exception for the calls built
    /// on it, which is the difference between asking and doing.
    /// </summary>
    [Fact]
    public void FindPropertyAnswersNullWhereTheOtherCallsThrow()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "absent"));

        Assert.Null(sink.FindProperty("no-such-property"));
        Assert.Throws<ArgumentException>(() => sink.GetProperty<bool>("no-such-property"));
        Assert.Throws<ArgumentNullException>(() => sink.FindProperty(null!));
    }

    /// <summary>
    /// The specification holds a reference of its own, so it outlives the
    /// object that answered the lookup.
    /// </summary>
    [Fact]
    public void TheSpecificationOutlivesTheObjectThatAnsweredIt()
    {
        ParamSpec? sync;

        using (Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "short-lived")))
        {
            sync = sink.FindProperty("sync");
        }

        using (sync)
        {
            Assert.NotNull(sync);
            Assert.Equal("sync", sync.Name);
            Assert.Equal(GType.Boolean, sync.ValueType);
        }
    }
}
