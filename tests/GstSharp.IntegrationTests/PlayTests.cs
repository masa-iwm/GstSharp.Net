using System.Diagnostics;
using Gst;
using Gst.GLib;
using Gst.Play;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstPlay</c> module: the play itself, the descriptors of the
/// visualizations, the overlay video renderer, the signal adapter and the four
/// parses of the API bus that are written by hand.
/// </summary>
/// <remarks>
/// <para>
/// Every fact builds its own play. A play owns an internal thread and an API
/// bus whose messages hold the play, so sharing one between facts would let the
/// unread messages of the first decide what the second sees.
/// </para>
/// <para>
/// The facts that really play something need <c>playbin3</c>, which is what
/// <c>gst_play_new</c> builds and whose absence is a fatal <c>g_error</c> in
/// the C library rather than an error the binding could report, and the audio
/// elements that write and read the ogg they play. The audio sink is replaced
/// by a <c>fakesink</c> on the pipeline before the play starts, so nothing here
/// needs a sound card.
/// </para>
/// <para>
/// The guard facts of the hand written parses need none of them: a message with
/// the payload of the API bus is a structure a test can build itself, and the
/// checks they exercise happen before any call into the library.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class PlayTests
{
    /// <summary>The element every playing fact needs.</summary>
    private const string Playbin = "playbin3";

    /// <summary>The name every message of the API bus carries.</summary>
    private const string MessageDataName = "gst-play-message-data";

    /// <summary>The field that says which kind of message it is.</summary>
    private const string MessageTypeField = "play-message-type";

    /// <summary>The message of the error the synthetic messages carry.</summary>
    private const string ErrorText = "a synthetic play error";

    /// <summary>The uri the synthetic details structure carries.</summary>
    private const string DetailsUri = "file:///gstsharp-play-details.ogg";

    /// <summary>How long a play of well under a second may take.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public PlayTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A play built without a renderer reaches PLAYING, reports the media it
    /// loaded, and answers the three stream lists of that media.
    /// </summary>
    [RequiresElementFact(
        Playbin,
        "audiotestsrc",
        "audioconvert",
        "vorbisenc",
        "oggmux",
        "oggdemux",
        "vorbisdec",
        "fakesink")]
    public void APlayReachesPlayingAndReportsTheMediaItLoaded()
    {
        GstPlay.Initialize();

        using PlayMedia media = PlayMedia.Create();
        using Play play = new();

        Silence(play);
        play.Uri = media.Uri;
        play.Start();

        using Bus bus = play.GetMessageBus();
        Assert.True(
            WaitForState(bus, PlayState.Playing),
            "the play never reported the PLAYING state on its API bus");

        // The media info is a snapshot the play takes once the URI has
        // prerolled; it is null before that and never changes afterwards.
        using PlayMediaInfo info = WaitForMediaInfo(play)
            ?? throw new InvalidOperationException("the play reported no media info");

        Assert.Equal(media.Uri, info.GetUri());
        Assert.True(info.GetDuration() > ClockTime.Zero, "the media info reports no duration");
        Assert.NotEmpty(info.GetAudioStreams());
        Assert.Empty(info.GetVideoStreams());
        Assert.Empty(info.GetSubtitleStreams());
        Assert.Equal(info.GetAudioStreams().Count, info.GetStreamList().Count);

        play.Pause();
        play.Stop();
    }

    /// <summary>
    /// The visualization descriptors read their two fields, and every one of
    /// them names an element.
    /// </summary>
    [Fact]
    public void TheVisualizationsAnswerTheirNameAndDescription()
    {
        GstPlay.Initialize();

        IReadOnlyList<PlayVisualization> visualizations = Play.GetVisualizations();
        _output.WriteLine(FormattableString.Invariant($"{visualizations.Count} visualization(s)"));

        try
        {
            foreach (PlayVisualization visualization in visualizations)
            {
                Assert.NotEmpty(visualization.Name);
                Assert.NotEmpty(visualization.Description);
                _output.WriteLine(visualization.Name + ": " + visualization.Description);
            }
        }
        finally
        {
            foreach (PlayVisualization visualization in visualizations)
            {
                visualization.Dispose();
            }
        }
    }

    /// <summary>
    /// A stopped play accepts a configuration and answers it back; a playing
    /// one refuses it and leaves the caller's structure untouched, which is the
    /// path where the binding frees the copy it minted.
    /// </summary>
    [RequiresElementFact(
        Playbin,
        "audiotestsrc",
        "audioconvert",
        "vorbisenc",
        "oggmux",
        "oggdemux",
        "vorbisdec",
        "fakesink")]
    public void SetConfigIsAcceptedWhileStoppedAndRefusedWhilePlaying()
    {
        GstPlay.Initialize();

        using PlayMedia media = PlayMedia.Create();
        using Play play = new();

        Silence(play);

        using (Structure config = play.GetConfig())
        {
            Play.ConfigSetUserAgent(config, "GstSharp.Net integration test");
            Play.ConfigSetPositionUpdateInterval(config, 250);
            Assert.True(play.SetConfig(config), "a stopped play refused a configuration");

            // The argument stays the caller's: the call was handed a copy.
            Assert.False(config.IsDisposed);
        }

        using (Structure written = play.GetConfig())
        {
            Assert.Equal("GstSharp.Net integration test", Play.ConfigGetUserAgent(written));
            Assert.Equal(250u, Play.ConfigGetPositionUpdateInterval(written));
        }

        play.Uri = media.Uri;
        play.Start();

        using Bus bus = play.GetMessageBus();
        Assert.True(
            WaitForState(bus, PlayState.Playing),
            "the play never reported the PLAYING state on its API bus");

        using (Structure refused = play.GetConfig())
        {
            Play.ConfigSetUserAgent(refused, "never installed");
            Assert.False(play.SetConfig(refused), "a playing play accepted a configuration");
            Assert.False(refused.IsDisposed);
        }

        using (Structure unchanged = play.GetConfig())
        {
            Assert.Equal("GstSharp.Net integration test", Play.ConfigGetUserAgent(unchanged));
        }

        play.Stop();
    }

    /// <summary>
    /// A URI nothing can open raises an error message, and the hand written
    /// parse reads it.
    /// </summary>
    [RequiresElementFact(Playbin, "fakesink")]
    public void ParseErrorReadsTheErrorOfAUriThatCannotBeOpened()
    {
        GstPlay.Initialize();

        using Play play = new();

        Silence(play);
        play.Uri = "file:///gstsharp-no-such-file.ogg";
        play.Start();

        using Bus bus = play.GetMessageBus();
        using Message message = WaitForKind(bus, PlayMessage.Error)
            ?? throw new InvalidOperationException("the play reported no error for an unopenable URI");

        PlayMessageExtensions.ParseError(message, out GException error, out Structure? details);
        _output.WriteLine(error.Message);

        Assert.NotEqual(0u, error.Domain.Value);
        Assert.NotEmpty(error.Message);

        // The details are absent on 1.24 for an error whose element attached
        // none, and carry the URI from 1.26 on.
        if (NativeAvailability.Has126)
        {
            Assert.NotNull(details);
            Assert.Equal(play.Uri, details!.GetString("uri"));
        }

        details?.Dispose();
        play.Stop();
    }

    /// <summary>
    /// Both issue parses read the error, and answer the details as a structure
    /// of the caller own when the message carries one and
    /// <see langword="null"/> when it does not. Both branches run on every
    /// version, because the message is built here rather than waited for:
    /// GStreamer 1.24 omits the details field for an issue that came without
    /// them and 1.26 always attaches it, and the binding has to answer both.
    /// </summary>
    /// <param name="withDetails">Whether the message carries a details structure.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseErrorAndParseWarningReadTheErrorAndTheOptionalDetails(bool withDetails)
    {
        GstPlay.Initialize();

        using Element source = ElementFactory.Make("fakesrc", null)
            ?? throw new InvalidOperationException("fakesrc is missing");

        using (Message error = NewPlayMessage(source, PlayMessage.Error, "error", "error-details", withDetails))
        {
            PlayMessageExtensions.ParseError(error, out GException raised, out Structure? details);

            Assert.Equal(ErrorText, raised.Message);
            Assert.NotEqual(0u, raised.Domain.Value);
            AssertDetails(details, withDetails);
        }

        using (Message warning =
            NewPlayMessage(source, PlayMessage.Warning, "warning", "warning-details", withDetails))
        {
            PlayMessageExtensions.ParseWarning(warning, out GException raised, out Structure? details);

            Assert.Equal(ErrorText, raised.Message);
            Assert.NotEqual(0u, raised.Domain.Value);
            AssertDetails(details, withDetails);
        }

        static void AssertDetails(Structure? details, bool expected)
        {
            if (!expected)
            {
                Assert.Null(details);
                return;
            }

            using Structure carried = Assert.IsType<Structure>(details);
            Assert.Equal(DetailsUri, carried.GetString("uri"));
        }
    }

    /// <summary>
    /// A message of the right kind that carries no error at all is an
    /// <see cref="ArgumentException"/> rather than a null reference.
    /// </summary>
    [Fact]
    public void ParseErrorRefusesAMessageWithoutAnErrorField()
    {
        GstPlay.Initialize();

        using Element source = ElementFactory.Make("fakesrc", null)
            ?? throw new InvalidOperationException("fakesrc is missing");
        using Message empty = NewPlayMessage(source, PlayMessage.Error);

        Assert.Throws<ArgumentException>(
            () => PlayMessageExtensions.ParseError(empty, out _, out _));
    }

    /// <summary>
    /// Both hand written issue parses refuse a message that is not one of a
    /// play API bus, rather than reading a field that is not there.
    /// </summary>
    [Fact]
    public void ParseErrorAndParseWarningRefuseAForeignMessage()
    {
        GstPlay.Initialize();

        using Element source = ElementFactory.Make("fakesrc", null)
            ?? throw new InvalidOperationException("fakesrc is missing");
        using Message foreign = Message.NewEos(source);

        Assert.Throws<ArgumentException>(
            () => PlayMessageExtensions.ParseError(foreign, out _, out _));
        Assert.Throws<ArgumentException>(
            () => PlayMessageExtensions.ParseWarning(foreign, out _, out _));

        // A message that carries the payload of the API bus but another kind is
        // refused as well.
        using Message wrongKind = NewPlayMessage(source, PlayMessage.StateChanged);
        Assert.Throws<ArgumentException>(
            () => PlayMessageExtensions.ParseError(wrongKind, out _, out _));
    }

    /// <summary>
    /// The two missing plugin parses check the message themselves before they
    /// call, because the C half compares an uninitialised message kind. The
    /// guard runs on every version, including the 1.24 floor where the entry
    /// point does not exist at all.
    /// </summary>
    [Fact]
    public void TheMissingPluginParsesRefuseAForeignMessageBeforeTheyCall()
    {
        GstPlay.Initialize();

        using Element source = ElementFactory.Make("fakesrc", null)
            ?? throw new InvalidOperationException("fakesrc is missing");
        using Message foreign = Message.NewEos(source);

        Assert.Throws<ArgumentException>(
            () => PlayMessageExtensions.ParseErrorMissingPlugin(foreign, out _, out _));
        Assert.Throws<ArgumentException>(
            () => PlayMessageExtensions.ParseWarningMissingPlugin(foreign, out _, out _));

        using Message wrongKind = NewPlayMessage(source, PlayMessage.Warning);
        Assert.Throws<ArgumentException>(
            () => PlayMessageExtensions.ParseErrorMissingPlugin(wrongKind, out _, out _));
    }

    /// <summary>
    /// An error that is not about a missing plugin answers false and two null
    /// arrays. The entry point arrived in 1.26.
    /// </summary>
    [RequiresElementFact(Playbin, "fakesink")]
    public void ParseErrorMissingPluginAnswersFalseForAnOrdinaryError()
    {
        GstPlay.Initialize();

        if (!NativeAvailability.Has126)
        {
            // gst_play_message_parse_error_missing_plugin does not exist on the
            // 1.24 floor, and a call into it is an EntryPointNotFoundException
            // by design.
            return;
        }

        using Play play = new();

        Silence(play);
        play.Uri = "file:///gstsharp-no-such-file.ogg";
        play.Start();

        using Bus bus = play.GetMessageBus();
        using Message message = WaitForKind(bus, PlayMessage.Error)
            ?? throw new InvalidOperationException("the play reported no error for an unopenable URI");

        Assert.False(
            PlayMessageExtensions.ParseErrorMissingPlugin(
                message,
                out string[]? descriptions,
                out string[]? installerDetails));
        Assert.Null(descriptions);
        Assert.Null(installerDetails);

        play.Stop();
    }

    /// <summary>
    /// The synchronous adapter emits its signals, and the play it hands back is
    /// the wrapper it was built with.
    /// </summary>
    [RequiresElementFact(
        Playbin,
        "audiotestsrc",
        "audioconvert",
        "vorbisenc",
        "oggmux",
        "oggdemux",
        "vorbisdec",
        "fakesink")]
    public void TheSyncEmitAdapterEmitsStateChangedAndKeepsItsPlay()
    {
        GstPlay.Initialize();

        using PlayMedia media = PlayMedia.Create();
        using Play play = new();

        Silence(play);

        using PlaySignalAdapter adapter = PlaySignalAdapter.NewSyncEmit(play);
        Assert.Same(play, adapter.GetPlay());

        using ManualResetEventSlim playing = new(initialState: false);
        adapter.StateChanged += OnStateChanged;
        try
        {
            play.Uri = media.Uri;
            play.Start();

            Assert.True(
                playing.Wait(Patience),
                "the synchronous adapter never emitted a PLAYING state change");
        }
        finally
        {
            adapter.StateChanged -= OnStateChanged;
            play.Stop();
        }

        void OnStateChanged(object? sender, PlaySignalAdapter.StateChangedSignalArgs args)
        {
            if (args.Object == PlayState.Playing)
            {
                playing.Set();
            }
        }
    }

    /// <summary>
    /// The two asynchronous factories build an adapter that keeps its play, and
    /// a null context is the thread-default one rather than a refusal.
    /// </summary>
    /// <remarks>
    /// Nothing is asserted about a signal here: both adapters fire from a main
    /// context that this test does not iterate, which is exactly the trap their
    /// documentation names, and a test that waited for one would wait forever.
    /// </remarks>
    [RequiresElementFact(Playbin, "fakesink")]
    public void TheAsynchronousAdaptersKeepThePlayTheyWereBuiltWith()
    {
        GstPlay.Initialize();

        using Play play = new();

        Silence(play);

        using (PlaySignalAdapter adapter = PlaySignalAdapter.New(play))
        {
            Assert.Same(play, adapter.GetPlay());
        }

        using (PlaySignalAdapter withContext = PlaySignalAdapter.NewWithMainContext(play, MainContext.Default))
        {
            Assert.Same(play, withContext.GetPlay());
        }

        // A null context is the thread-default one, which is what
        // PlaySignalAdapter.New uses; the C function refuses null itself.
        using (PlaySignalAdapter defaulted = PlaySignalAdapter.NewWithMainContext(play, null))
        {
            Assert.Same(play, defaulted.GetPlay());
        }

        // The C adapter never referenced the play, so the field the imported
        // getter reads dangles once the play is gone. The binding answers from
        // the wrapper it kept, and a disposed adapter has none.
        PlaySignalAdapter disposed = PlaySignalAdapter.New(play);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.GetPlay());
    }

    /// <summary>
    /// The overlay renderer round trips its window handle and its sink, and the
    /// play it is given keeps it alive rather than consuming it.
    /// </summary>
    [RequiresElementFact(Playbin, "fakesink")]
    public void TheOverlayRendererSurvivesTheConstructionOfThePlay()
    {
        GstPlay.Initialize();

        using Element sink = ElementFactory.Make("fakesink", "overlay-sink")
            ?? throw new InvalidOperationException("fakesink is missing");

        using PlayVideoOverlayVideoRenderer renderer = new(nint.Zero, sink);

        Assert.Equal(nint.Zero, renderer.GetWindowHandle());
        Assert.Same(sink, renderer.VideoSink);

        using (Play play = new(renderer))
        {
            // gst_play_new drops the reference of its caller. The renderer is
            // still usable here, which is what the extra reference of the
            // constructor buys.
            renderer.SetWindowHandle(42);
            Assert.Equal(42, renderer.GetWindowHandle());
            renderer.SetRenderRectangle(0, 0, 320, 240);
            renderer.GetRenderRectangle(out int x, out int y, out int width, out int height);
            Assert.Equal(0, x);
            Assert.Equal(0, y);
            Assert.Equal(320, width);
            Assert.Equal(240, height);
        }

        Assert.Equal(42, renderer.GetWindowHandle());
        Assert.Same(sink, renderer.VideoSink);

        // A renderer built without a sink answers null: the video-sink
        // property is only ever written by the constructor. The two argument
        // constructor with a null sink is the same call, and it must not hand
        // the null to gst_object_ref_sink, which has no null check.
        using PlayVideoOverlayVideoRenderer bare = new(nint.Zero);
        Assert.Null(bare.VideoSink);

        using PlayVideoOverlayVideoRenderer bareWithNull = new(nint.Zero, null);
        Assert.Null(bareWithNull.VideoSink);
    }

    /// <summary>
    /// Disposing a play sets its API bus flushing, so the messages nobody read
    /// are gone rather than holding the play alive.
    /// </summary>
    [RequiresElementFact(Playbin, "fakesink")]
    public void DisposingThePlaySetsItsApiBusFlushing()
    {
        GstPlay.Initialize();

        Bus bus;
        using (Play play = new())
        {
            Silence(play);
            bus = play.GetMessageBus();

            play.Uri = "file:///gstsharp-no-such-file.ogg";
            play.Start();

            // Let the play post whatever it wants to; nothing reads the bus.
            Stopwatch elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(5) && !bus.HavePending())
            {
                Thread.Sleep(50);
            }

            Assert.True(bus.HavePending(), "the play posted nothing on its API bus");
        }

        using (bus)
        {
            Assert.False(bus.HavePending(), "the API bus still holds messages after the play was disposed");
            Assert.Null(bus.Pop());
        }
    }

    /// <summary>
    /// The index based track selection is the only one the 1.24 floor has, and
    /// it stays bound although 1.26 deprecated it.
    /// </summary>
    [RequiresElementFact(
        Playbin,
        "audiotestsrc",
        "audioconvert",
        "vorbisenc",
        "oggmux",
        "oggdemux",
        "vorbisdec",
        "fakesink")]
    public void TheTrackSelectionOfBothGenerationsSelectsTheOneAudioStream()
    {
        GstPlay.Initialize();

        using PlayMedia media = PlayMedia.Create();
        using Play play = new();

        Silence(play);
        play.Uri = media.Uri;
        play.Start();

        using Bus bus = play.GetMessageBus();
        Assert.True(
            WaitForState(bus, PlayState.Playing),
            "the play never reported the PLAYING state on its API bus");

        using PlayMediaInfo info = WaitForMediaInfo(play)
            ?? throw new InvalidOperationException("the play reported no media info");

        PlayAudioInfo audio = Assert.Single(info.GetAudioStreams());

#pragma warning disable CS0618 // The index based API is the only one available on the 1.24 floor.
        // Selecting the stream that is already selected is answered with
        // false: gst_play_select_streams reports a change rather than a
        // success, and there is nothing to change here. An index no stream
        // carries is false as well, and that is the answer this asserts.
        Assert.False(play.SetAudioTrack(9999), "an index no stream carries was accepted");
        play.SetAudioTrack(audio.GetIndex());
#pragma warning restore CS0618

        using PlayAudioInfo current = play.GetCurrentAudioTrack()
            ?? throw new InvalidOperationException("the play has no current audio track");
#pragma warning disable CS0618 // Reading the index is how the floor identifies a stream.
        Assert.Equal(audio.GetIndex(), current.GetIndex());
#pragma warning restore CS0618

        if (NativeAvailability.Has126)
        {
            Assert.Equal(audio.GetStreamId(), current.GetStreamId());
            play.SetAudioTrackId(audio.GetStreamId());
        }

        play.Stop();
    }

    /// <summary>
    /// The loop configuration arrived in 1.28 and round trips through the
    /// configuration structure.
    /// </summary>
    [Fact]
    public void TheLoopConfigurationRoundTripsFrom128()
    {
        GstPlay.Initialize();

        if (!NativeAvailability.Has128)
        {
            // gst_play_config_set_loop does not exist before 1.28.
            return;
        }

        using Structure config = Structure.NewEmpty("gstsharp-play-config");

        Play.ConfigSetLoop(config, PlayLoop.Track);
        Assert.Equal(PlayLoop.Track, Play.ConfigGetLoop(config));
        Assert.Equal("GST_PLAY_LOOP_TRACK", PlayLoopExtensions.GetName(PlayLoop.Track));
    }

    /// <summary>
    /// Replaces the audio sink of the pipeline with a <c>fakesink</c>, so that
    /// a play in a test writes to nothing.
    /// </summary>
    /// <param name="play">The play to silence, before it is started.</param>
    private static void Silence(Play play)
    {
        using Element pipeline = play.GetPipeline();
        using Element sink = ElementFactory.Make("fakesink", null)
            ?? throw new InvalidOperationException("fakesink is missing");

        sink.SetProperty("sync", true);
        pipeline.SetProperty("audio-sink", sink);
    }

    /// <summary>
    /// Polls the API bus until the play reports the state that was asked for.
    /// </summary>
    /// <param name="bus">The API bus of the play.</param>
    /// <param name="state">The state to wait for.</param>
    /// <returns><see langword="true"/> when it arrived in time.</returns>
    private static bool WaitForState(Bus bus, PlayState state)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < Patience)
        {
            using Message? message = BusPump.WaitFor(bus, MessageType.Application, TimeSpan.FromSeconds(1));
            if (message is null || !Play.IsPlayMessage(message))
            {
                continue;
            }

            PlayMessageExtensions.ParseType(message, out PlayMessage kind);
            if (kind != PlayMessage.StateChanged)
            {
                continue;
            }

            PlayMessageExtensions.ParseStateChanged(message, out PlayState reported);
            if (reported == state)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Polls the API bus until a message of the kind that was asked for
    /// arrives.
    /// </summary>
    /// <param name="bus">The API bus of the play.</param>
    /// <param name="kind">The kind of message to wait for.</param>
    /// <returns>The message, which the caller has to dispose, or null.</returns>
    private static Message? WaitForKind(Bus bus, PlayMessage kind)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < Patience)
        {
            Message? message = BusPump.WaitFor(bus, MessageType.Application, TimeSpan.FromSeconds(1));
            if (message is null)
            {
                continue;
            }

            if (Play.IsPlayMessage(message))
            {
                PlayMessageExtensions.ParseType(message, out PlayMessage reported);
                if (reported == kind)
                {
                    return message;
                }
            }

            message.Dispose();
        }

        return null;
    }

    /// <summary>
    /// Waits for the snapshot the play takes once the URI has prerolled.
    /// </summary>
    /// <param name="play">The play to ask.</param>
    /// <returns>The media info, which the caller has to dispose, or null.</returns>
    private static PlayMediaInfo? WaitForMediaInfo(Play play)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < Patience)
        {
            if (play.GetMediaInfo() is { } info)
            {
                return info;
            }

            Thread.Sleep(50);
        }

        return null;
    }

    /// <summary>
    /// Builds a message that carries the payload of the API bus, for the guard
    /// facts of the hand written parses.
    /// </summary>
    /// <param name="source">The element the message names as its source.</param>
    /// <param name="kind">The kind to write into the payload.</param>
    /// <returns>The message, which the caller has to dispose.</returns>
    private static Message NewPlayMessage(
        Element source,
        PlayMessage kind,
        string? errorField = null,
        string? detailsField = null,
        bool withDetails = false)
    {
        // Asking for the name of a member registers the enumeration with
        // GObject, which is what makes the lookup below answer its type. The
        // payload of a real message of the API bus holds exactly that type.
        _ = PlayMessageExtensions.GetName(PlayMessage.Error);

        using Structure data = Structure.NewEmpty(MessageDataName);
        using Gst.GObject.Value kindValue = Gst.GObject.Value.New(Gst.GObject.GType.FromName("GstPlayMessage"));
        kindValue.SetEnum((int)kind);
        data.SetValue(MessageTypeField, in kindValue);

        if (errorField is not null)
        {
            // A boxed GError of the right type is what the play attaches, and
            // the one gst_message_new_error builds is the way a test reaches
            // one: nothing in the binding hands out a raw GError.
            using Message carrier = Message.NewError(
                source,
                new GException(CoreErrorExtensions.Quark(), (int)CoreError.Failed, ErrorText),
                "debug");
            using Structure carried = carrier.GetStructure()
                ?? throw new InvalidOperationException("an error message carries no structure");
            using Gst.GObject.Value error = carried.GetValue("gerror");
            data.SetValue(errorField, in error);
        }

        if (detailsField is not null && withDetails)
        {
            using Structure details = Structure.NewEmpty("gstsharp-play-details");
            using (Gst.GObject.Value uri = Gst.GObject.Value.New(Gst.GObject.GType.String))
            {
                uri.SetString(DetailsUri);
                details.SetValue("uri", uri);
            }

            using Gst.GObject.Value detailsValue = Gst.GObject.Value.New(details.BoxedType);
            detailsValue.SetBoxed(details);
            data.SetValue(detailsField, in detailsValue);
        }

        return Message.NewApplication(source, data);
    }

    /// <summary>A generated ogg file the plays of this class read.</summary>
    private sealed class PlayMedia : IDisposable
    {
        private PlayMedia(string path) => Path = path;

        /// <summary>Gets the path of the generated file.</summary>
        internal string Path { get; }

        /// <summary>Gets the file as a URI.</summary>
        internal string Uri => new System.Uri(Path).AbsoluteUri;

        /// <summary>Writes a short ogg/vorbis file.</summary>
        /// <returns>The file, which the caller has to dispose.</returns>
        internal static PlayMedia Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                FormattableString.Invariant($"gstsharp-play-{Guid.NewGuid():N}.ogg"));

            Write(path);
            return new PlayMedia(path);
        }

        /// <summary>Deletes the file.</summary>
        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // A file a pipeline still holds open is left behind rather than
                // failing a test that has already made its point.
            }
        }

        private static void Write(string path)
        {
            // The location is set as a property rather than spelled into the
            // description, so that a Windows path needs no escaping.
            if (Global.ParseLaunch(
                    "audiotestsrc num-buffers=100 ! audioconvert ! vorbisenc ! oggmux ! filesink name=sink")
                is not Pipeline pipeline)
            {
                throw new InvalidOperationException("the source description did not produce a pipeline");
            }

            using (pipeline)
            {
                using (Element sink = pipeline.GetByName("sink")
                    ?? throw new InvalidOperationException("the source pipeline has no filesink"))
                {
                    sink.SetProperty("location", path);
                }

                Bus bus = pipeline.GetBus();

                Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));
                try
                {
                    using Message? message = BusPump.WaitFor(
                        bus,
                        MessageType.Eos | MessageType.Error,
                        Patience);

                    Assert.NotNull(message);
                    Assert.Equal(MessageType.Eos, message!.Type);
                }
                finally
                {
                    pipeline.SetState(State.Null);
                }
            }
        }
    }
}
