extern alias gstsharp;

using GES;
using Gst;
using Gst.App;
using Gst.Audio;
using Gst.Base;
using Gst.Controller;
using Gst.Pbutils;
using Xunit;
using Structure = Gst.Structure;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated properties that no C accessor backs, against the library that
/// is installed. Each of them reads a <c>GValue</c> that
/// <c>g_object_get_property</c> filled in and writes one that
/// <c>g_object_set_property</c> takes, so what these tests check is that the
/// value kind the generator picked is the one the property really holds — a
/// mismatch is a GLib warning and a value that was never written, which no
/// compile time check can catch.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class ValueBackedPropertyTests
{
    /// <summary>A boolean property is written and read back.</summary>
    [Fact]
    public void ABooleanPropertyRoundTrips()
    {
        GstApp.Initialize();
        using AppSrc source = Assert.IsAssignableFrom<AppSrc>(ElementFactory.Make("appsrc", "boolean"));

        Assert.False(source.Block);

        source.Block = true;
        Assert.True(source.Block);

        source.Block = false;
        Assert.False(source.Block);
    }

    /// <summary>An <c>gint</c> property is written and read back.</summary>
    [Fact]
    public void AnIntegerPropertyRoundTrips()
    {
        GstBase.Initialize();
        using BaseSrc source = Assert.IsAssignableFrom<BaseSrc>(ElementFactory.Make("fakesrc", "integer"));

        // The default of num-buffers is "no limit", which the C side spells -1.
        Assert.Equal(-1, source.NumBuffers);

        source.NumBuffers = 12;
        Assert.Equal(12, source.NumBuffers);
    }

    /// <summary>An unsigned property is written and read back.</summary>
    [Fact]
    public void AnUnsignedPropertyRoundTrips()
    {
        GstApp.Initialize();
        using AppSrc source = Assert.IsAssignableFrom<AppSrc>(ElementFactory.Make("appsrc", "unsigned"));

        source.MinPercent = 40;
        Assert.Equal(40u, source.MinPercent);
    }

    /// <summary>A 64 bit signed property is written and read back.</summary>
    [Fact]
    public void ASignedLongPropertyRoundTrips()
    {
        GstApp.Initialize();
        using AppSrc source = Assert.IsAssignableFrom<AppSrc>(ElementFactory.Make("appsrc", "long"));

        // max-latency is a gint64 whose "unset" value is -1, which is what says
        // that the property really is signed: read as a guint64 it would be
        // 18446744073709551615.
        Assert.Equal(-1L, source.MaxLatency);

        source.MaxLatency = 500_000_000L;
        Assert.Equal(500_000_000L, source.MaxLatency);
    }

    /// <summary>A 64 bit unsigned property is written and read back.</summary>
    [Fact]
    public void AnUnsignedLongPropertyRoundTrips()
    {
        GstPbutils.Initialize();
        using Discoverer discoverer = Discoverer.New(ClockTime.FromSeconds(5));

        Assert.Equal(ClockTime.FromSeconds(5).Nanoseconds, discoverer.Timeout);

        discoverer.Timeout = ClockTime.FromSeconds(2).Nanoseconds;
        Assert.Equal(ClockTime.FromSeconds(2).Nanoseconds, discoverer.Timeout);
    }

    /// <summary>
    /// An enumeration property is written and read back as the generated
    /// enumeration, which is what says that the value holds an enum rather than
    /// the plain int a wrong mapping would have written.
    /// </summary>
    [Fact]
    public void AnEnumerationPropertyRoundTrips()
    {
        GstApp.Initialize();
        using AppSrc source = Assert.IsAssignableFrom<AppSrc>(ElementFactory.Make("appsrc", "enumeration"));

        Assert.Equal(Format.Bytes, source.Format);

        source.Format = Format.Time;
        Assert.Equal(Format.Time, source.Format);
    }

    /// <summary>The clock of the process reports which clock it reads.</summary>
    [Fact]
    public void AnEnumerationPropertyIsRead()
    {
        // The system clock is a process singleton, so its type is read and not
        // written: changing it would change the clock every other test uses.
        using SystemClock clock = Assert.IsAssignableFrom<SystemClock>(SystemClock.Obtain());

        Assert.Contains(
            clock.ClockType,
            new[] { ClockType.Monotonic, ClockType.Realtime, ClockType.Other, ClockType.Tai });
    }

    /// <summary>
    /// A set of flags is read back as the generated bitfield. The track type is
    /// construct-only, so what it reports is what the constructor was given.
    /// </summary>
    [Fact]
    public void AFlagsPropertyIsRead()
    {
        GstGES.Initialize();
        using Caps? format = Caps.FromString("audio/x-raw");
        Assert.NotNull(format);

        using Track track = Track.New(TrackType.Audio, format);

        Assert.Equal(TrackType.Audio, track.TrackType);
    }

    /// <summary>A string property is read.</summary>
    [Fact]
    public void AStringPropertyIsRead()
    {
        using Caps? caps = Caps.FromString("audio/x-raw");
        Assert.NotNull(caps);

        using PadTemplate? template = PadTemplate.New("sink_%u", PadDirection.Sink, PadPresence.Request, caps);
        Assert.NotNull(template);

        Assert.Equal("sink_%u", template!.NameTemplate);
    }

    /// <summary>A string property is written and read back.</summary>
    [Fact]
    public void AStringPropertyRoundTrips()
    {
        GstGES.Initialize();
        using Caps? format = Caps.FromString("audio/x-raw");
        Assert.NotNull(format);

        using Track track = Track.New(TrackType.Audio, format);

        track.Id = "gstsharp-track";
        Assert.Equal("gstsharp-track", track.Id);
    }

    /// <summary>
    /// A <c>GType</c> valued property is read as the runtime <c>GType</c>, not
    /// as the integer behind it.
    /// </summary>
    [Fact]
    public void AGTypePropertyIsRead()
    {
        using Caps? caps = Caps.FromString("audio/x-raw");
        Assert.NotNull(caps);

        Gst.GObject.GType padType = Gst.GObject.GType.FromName("GstPad");
        Assert.True(padType.IsValid);

        using PadTemplate? template = PadTemplate.NewWithGtype(
            "src",
            PadDirection.Src,
            PadPresence.Always,
            caps,
            padType);
        Assert.NotNull(template);

        // gst_pad_template_new leaves the type unset, so the template that
        // names one is what says the property really carries a GType rather
        // than the integer behind it.
        Assert.Equal(padType, template!.Gtype);
        Assert.Equal("GstPad", template.Gtype.Name);
    }

    /// <summary>
    /// A construct-only property is emitted without a setter, and reports what
    /// the constructor was given.
    /// </summary>
    [Fact]
    public void AConstructOnlyPropertyIsRead()
    {
        using Caps? caps = Caps.FromString("audio/x-raw");
        Assert.NotNull(caps);

        using PadTemplate? template = PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps);
        Assert.NotNull(template);

        Assert.Equal(PadDirection.Sink, template!.Direction);
        Assert.Equal(PadPresence.Always, template.Presence);
    }

    /// <summary>
    /// An object valued property hands back the interned wrapper of the
    /// instance, which is the same object the caller already holds rather than
    /// a second wrapper around the same pointer.
    /// </summary>
    [Fact]
    public void AnObjectPropertyIsTheInternedWrapper()
    {
        using Caps? caps = Caps.FromString("audio/x-raw");
        Assert.NotNull(caps);

        using PadTemplate? template = PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps);
        Assert.NotNull(template);

        using Pad pad = Pad.NewFromTemplate(template!, "src");

        Assert.Same(template, pad.Template);
    }

    /// <summary>
    /// A mini object valued property is written and read back, and the wrapper
    /// that comes out owns a reference of its own: disposing it leaves the
    /// property holding what it was given.
    /// </summary>
    [Fact]
    public void AMiniObjectPropertyRoundTrips()
    {
        GstPbutils.Initialize();
        using Caps? format = Caps.FromString("audio/x-vorbis");
        using Caps? restriction = Caps.FromString("audio/x-raw, channels=2");
        Assert.NotNull(format);
        Assert.NotNull(restriction);

        using EncodingAudioProfile profile = EncodingAudioProfile.New(format, null, null, 0);

        Assert.Null(profile.RestrictionCaps);

        profile.RestrictionCaps = restriction;

        using (Caps? read = profile.RestrictionCaps)
        {
            Assert.NotNull(read);
            Assert.True(read!.IsEqual(restriction!));
        }

        // The read above disposed its wrapper. The property still holds the
        // caps, which is what says that the reader owned a reference of its own
        // rather than the one the property keeps.
        using Caps? again = profile.RestrictionCaps;
        Assert.NotNull(again);
        Assert.True(again!.IsEqual(restriction!));
    }

    /// <summary>
    /// A boxed valued property is written and read back, and what comes out is
    /// a copy: it survives the wrapper that was written being disposed.
    /// </summary>
    [RequiresElementFact("audiomixer")]
    public void ABoxedPropertyRoundTrips()
    {
        GstAudio.Initialize();
        using Element mixer = Assert.IsAssignableFrom<Element>(ElementFactory.Make("audiomixer", "boxed"));
        using Pad? pad = mixer.RequestPadSimple("sink_%u");
        Assert.NotNull(pad);

        AudioAggregatorConvertPad convert = Assert.IsAssignableFrom<AudioAggregatorConvertPad>(pad);

        Assert.Null(convert.ConverterConfig);

        using (Structure written = Structure.NewEmpty("GstAudioConverterConfig"))
        using (Gst.GObject.Value quality = Gst.GObject.Value.New(Gst.GObject.GType.Int))
        {
            quality.SetInt(8);
            written.SetValue("GstAudioResampler.quality", quality);
            convert.ConverterConfig = written;
        }

        // The wrapper that was written is disposed by now; the property holds a
        // copy of the structure and hands out another one here.
        using Structure? read = convert.ConverterConfig;
        Assert.NotNull(read);
        Assert.Equal("GstAudioConverterConfig", read!.GetName());
        Assert.True(read.GetInt("GstAudioResampler.quality", out int stored));
        Assert.Equal(8, stored);
    }

    /// <summary>
    /// The <c>name</c> property of a control binding is emitted as
    /// <c>PropertyName</c>, because it holds the name of the controlled
    /// property and not the name of the binding, which
    /// <see cref="Gst.Object.Name"/> already carries.
    /// </summary>
    [Fact]
    public void TheRenamedPropertyIsTheControlledPropertyName()
    {
        GstController.Initialize();
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "renamed"));

        InterpolationControlSource source = InterpolationControlSource.New();
        Assert.True(source.Set(ClockTime.Zero, 0.5));

        Gst.ControlBinding binding = DirectControlBinding.New(element, "volume", source);
        Assert.True(element.AddControlBinding(binding));

        // The controlled property, which is not the name of the binding object.
        Assert.Equal("volume", binding.PropertyName);
        Assert.NotEqual("volume", binding.Name);
    }

    /// <summary>
    /// A property the installed GStreamer does not declare is an
    /// <see cref="ArgumentException"/> rather than a silent zero. The
    /// <c>dropped</c> counter of <c>appsink</c> arrived in 1.28, so which of
    /// the two happens depends on what is installed — and both are the
    /// documented contract.
    /// </summary>
    [Fact]
    public void AMissingPropertyIsRefusedByName()
    {
        GstApp.Initialize();
        using AppSink sink = Assert.IsAssignableFrom<AppSink>(ElementFactory.Make("appsink", "missing"));

        Gst.Version version = gstsharp::GstSharp.NativeVersion;
        if (version.Major > 1 || (version.Major == 1 && version.Minor >= 28))
        {
            Assert.Equal(0UL, sink.Dropped);
            return;
        }

        ArgumentException failure = Assert.Throws<ArgumentException>(() => _ = sink.Dropped);
        Assert.Contains("dropped", failure.Message, StringComparison.Ordinal);
    }
}
