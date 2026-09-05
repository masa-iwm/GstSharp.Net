using System.Diagnostics;
using Gst;
using Gst.GLib;
using Gst.Transcoder;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstTranscoder</c> module: the transcoder itself, the two shapes of
/// its signal adapter, and the two parses of its API bus that are written by
/// hand because the imported ones abort the process.
/// </summary>
/// <remarks>
/// <para>
/// <b>No fact shares a transcoder with another.</b> A transcoder runs once —
/// <c>gst_transcoder_run</c> connects handlers to its own stack frame and never
/// disconnects them — and a synchronous signal adapter consumes the whole API
/// bus of the instance it is attached to, for good. Every fact below therefore
/// builds its own, which is the contract <c>Gst.Transcoder.GstTranscoder</c>
/// documents.
/// </para>
/// <para>
/// Every member the module binds arrived in 1.20, so nothing here needs a
/// version gate. What the transcoding facts do need are the
/// <c>uritranscodebin</c> and <c>transcodebin</c> elements of the
/// <c>transcode</c> plugin of gst-plugins-bad, which ships separately from the
/// library the module imports from, and the audio elements that write and read
/// the ogg the facts transcode. The guard facts of the hand written parses need
/// none of them: a message with the payload of the API bus is a structure a
/// test can build itself.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class TranscoderTests
{
    /// <summary>The elements every transcoding fact needs.</summary>
    private const string Transcode = "uritranscodebin";

    /// <summary>The profile the facts transcode into.</summary>
    private const string OggVorbis = "application/ogg:audio/x-vorbis";

    /// <summary>The name every message of the API bus carries.</summary>
    private const string MessageDataName = "gst-transcoder-message-data";

    /// <summary>The field that carries the details of an error or a warning.</summary>
    private const string IssueDetailsField = "issue-details";

    /// <summary>How long a transcoding of under a second may take.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public TranscoderTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The synchronous run transcodes the file and answers <see langword="true"/>.
    /// </summary>
    [RequiresElementFact(
        Transcode,
        "transcodebin",
        "audiotestsrc",
        "audioconvert",
        "vorbisenc",
        "oggmux",
        "oggdemux",
        "vorbisdec")]
    public void RunTranscodesTheSourceIntoTheDestination()
    {
        GstTranscoder.Initialize();

        using TranscodeFiles files = TranscodeFiles.Create();

        // Not disposed through `using`: gst_transcoder_run blocks, and a run
        // that never comes back would otherwise be disposed under the thread
        // still standing in it. The transcoder is disposed on the path that
        // completed and left alone on the path that did not.
        Transcoder transcoder = Transcoder.New(files.SourceUri, files.DestinationUri, OggVorbis);

        // The synchronous run has no upper bound of its own: a pipeline that
        // never reaches its end of stream keeps the call, and with it the whole
        // test run, for good. It is therefore made on a thread of its own and
        // waited for with the patience the asynchronous fact gives the same
        // transcoding.
        //
        // Spelled out, because `Task` is Gst.Task in this file.
        System.Threading.Tasks.Task<bool> run = System.Threading.Tasks.Task.Run(transcoder.Run);

        // The wait is a WhenAny rather than Task.Wait, because Wait rethrows a
        // faulted run as an AggregateException, which would hide the exception
        // the read below is there to surface.
        System.Threading.Tasks.Task completed = System.Threading.Tasks.Task
            .WhenAny(run, System.Threading.Tasks.Task.Delay(Patience))
            .GetAwaiter()
            .GetResult();

        if (!ReferenceEquals(completed, run))
        {
            Assert.Fail(
                $"gst_transcoder_run did not come back within {Patience.TotalSeconds} seconds; the transcoder is "
                + "left undisposed, because the run that hangs still holds it");
        }

        // The result is read through the awaiter rather than through Result, so
        // that a GException the run raised arrives as itself.
        Assert.True(run.GetAwaiter().GetResult(), "gst_transcoder_run reported a failure");

        transcoder.Dispose();

        FileInfo written = new(files.DestinationPath);
        Assert.True(written.Exists, "the transcoder wrote no destination file");
        Assert.True(written.Length > 0, "the destination file is empty");
    }

    /// <summary>
    /// The asynchronous run reports its progress on the API bus: the state
    /// passes through PLAYING and the run ends with a done message.
    /// </summary>
    [RequiresElementFact(
        Transcode,
        "transcodebin",
        "audiotestsrc",
        "audioconvert",
        "vorbisenc",
        "oggmux",
        "oggdemux",
        "vorbisdec")]
    public void RunAsyncReportsStateAndDoneOnTheMessageBus()
    {
        GstTranscoder.Initialize();

        using TranscodeFiles files = TranscodeFiles.Create();

        using Transcoder transcoder = Transcoder.New(files.SourceUri, files.DestinationUri, OggVorbis);

        // The bus is an interned wrapper of a bus the transcoder owns, so it is
        // not disposed here.
        Bus bus = transcoder.GetMessageBus();

        bool sawPlaying = false;
        bool done = false;
        GException? failure = null;

        transcoder.RunAsync();

        Stopwatch elapsed = Stopwatch.StartNew();
        while (!done && failure is null && elapsed.Elapsed < Patience)
        {
            using Message? message = bus.TimedPopFiltered(
                ClockTime.FromMilliseconds(100),
                MessageType.Application);

            if (message is null)
            {
                continue;
            }

            Assert.True(Transcoder.IsTranscoderMessage(message));

            TranscoderMessage kind = TranscoderMessageExtensions.ParseType(message);
            switch (kind)
            {
                case TranscoderMessage.StateChanged:
                    TranscoderMessageExtensions.ParseState(message, out TranscoderState state);
                    _output.WriteLine($"state={TranscoderStateExtensions.GetName(state)}");
                    sawPlaying |= state == TranscoderState.Playing;
                    break;

                case TranscoderMessage.PositionUpdated:
                    TranscoderMessageExtensions.ParsePosition(message, out ClockTime position);
                    _output.WriteLine(FormattableString.Invariant($"position={position.TotalSeconds:F2}"));
                    break;

                case TranscoderMessage.Error:
                    TranscoderMessageExtensions.ParseError(message, out GException error, out Structure? details);
                    details?.Dispose();
                    failure = error;
                    break;

                default:
                    done = kind == TranscoderMessage.Done;
                    break;
            }
        }

        Assert.True(failure is null, failure?.Message);
        Assert.True(done, "the transcoder never posted a done message");
        Assert.True(sawPlaying, "the transcoder never reported the PLAYING state");
    }

    /// <summary>
    /// A profile string that names nothing is not refused by the factory: the
    /// transcoder is built and reports it on the API bus, with no details.
    /// </summary>
    /// <remarks>
    /// This is the fact the hand written parse exists for. The imported
    /// <c>gst_transcoder_message_parse_error</c> reads the <c>issue-details</c>
    /// field of a message that carries none, and its miss branch is
    /// <c>g_error()</c>, which aborts the process: a run of this fact against
    /// the generated shape would take the whole test host with it.
    /// </remarks>
    [RequiresElementFact(Transcode, "transcodebin")]
    public void AnUnusableProfileIsReportedAsAnErrorWithoutDetails()
    {
        GstTranscoder.Initialize();

        string source = FormattableString.Invariant($"file:///gstsharp-missing-{Guid.NewGuid():N}.ogg");
        string destination = FormattableString.Invariant($"file:///gstsharp-unwritten-{Guid.NewGuid():N}.ogg");

        using Transcoder transcoder = Transcoder.New(source, destination, "not-a-media-type/at-all");

        Bus bus = transcoder.GetMessageBus();

        transcoder.RunAsync();

        using Message? message = BusPump.WaitFor(bus, MessageType.Application, Patience);

        Assert.NotNull(message);
        Assert.True(Transcoder.IsTranscoderMessage(message!));
        Assert.Equal(TranscoderMessage.Error, TranscoderMessageExtensions.ParseType(message!));

        TranscoderMessageExtensions.ParseError(message!, out GException error, out Structure? details);
        using (details)
        {
            _output.WriteLine($"error={error.Message} details={details?.ToString() ?? "<null>"}");

            Assert.NotEqual(Quark.Zero, error.Domain);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));

            // The four errors the transcoder raises itself attach no details,
            // which is exactly what the imported parse aborts on.
            Assert.Null(details);
        }
    }

    /// <summary>
    /// The synchronous adapter never stores the transcoder it was made for, so
    /// its transcoder is <see langword="null"/> however alive that transcoder
    /// is.
    /// </summary>
    [RequiresElementFact(Transcode, "transcodebin")]
    public void TheSyncSignalAdapterTracksNoTranscoder()
    {
        GstTranscoder.Initialize();

        string source = FormattableString.Invariant($"file:///gstsharp-missing-{Guid.NewGuid():N}.ogg");
        string destination = FormattableString.Invariant($"file:///gstsharp-unwritten-{Guid.NewGuid():N}.ogg");

        using Transcoder transcoder = Transcoder.New(source, destination, OggVorbis);
        using TranscoderSignalAdapter adapter = transcoder.GetSyncSignalAdapter();

        Assert.Null(adapter.GetTranscoder());
        Assert.Null(adapter.Transcoder);
    }

    /// <summary>
    /// The asynchronous adapter is cached on the transcoder: two calls for the
    /// same main context answer the same adapter, and the done signal it raises
    /// reaches a handler that a main loop dispatches.
    /// </summary>
    [RequiresElementFact(
        Transcode,
        "transcodebin",
        "audiotestsrc",
        "audioconvert",
        "vorbisenc",
        "oggmux",
        "oggdemux",
        "vorbisdec")]
    public void TheSignalAdapterIsCachedAndRaisesDoneOnAMainLoop()
    {
        GstTranscoder.Initialize();

        using TranscodeFiles files = TranscodeFiles.Create();

        using Transcoder transcoder = Transcoder.New(files.SourceUri, files.DestinationUri, OggVorbis);

        // Both calls pass null, so both mean the context of this thread, and
        // the second one finds the adapter the first one attached.
        TranscoderSignalAdapter? first = transcoder.GetSignalAdapter(null);
        TranscoderSignalAdapter? second = transcoder.GetSignalAdapter(null);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(first, second);
        Assert.Equal(first!.Handle, second!.Handle);

        // The adapter holds the transcoder weakly, and the transcoder is alive
        // here, so this half of the nullable answer is the non null one. What
        // comes back is the interned wrapper of the very same object, so it is
        // not disposed: doing so would dispose the transcoder of this fact.
        Assert.Same(transcoder, first.GetTranscoder());

        bool done = false;
        using MainLoop loop = new();

        void OnDone(object? sender, EventArgs args)
        {
            done = true;
            loop.Quit();
        }

        first.Done += OnDone;
        try
        {
            transcoder.RunAsync();

            // The loop runs until the done signal quits it. The watchdog bounds
            // it, because a test that hangs reports nothing; g_main_loop_quit
            // may be called from any thread.
            using ManualResetEventSlim finished = new(initialState: false);
            Thread watchdog = new(() =>
            {
                if (!finished.Wait(Patience))
                {
                    loop.Quit();
                }
            })
            {
                IsBackground = true,
            };

            watchdog.Start();
            try
            {
                loop.Run();
            }
            finally
            {
                finished.Set();
                watchdog.Join();
            }
        }
        finally
        {
            first.Done -= OnDone;
        }

        Assert.True(done, "the signal adapter never raised its done signal");
    }

    /// <summary>
    /// The pipeline of a transcoder is the <c>uritranscodebin</c> it built, and
    /// it is there wherever that element is.
    /// </summary>
    [RequiresElementFact(Transcode, "transcodebin")]
    public void ThePipelineIsThereWhereTheElementIs()
    {
        GstTranscoder.Initialize();

        string source = FormattableString.Invariant($"file:///gstsharp-missing-{Guid.NewGuid():N}.ogg");
        string destination = FormattableString.Invariant($"file:///gstsharp-unwritten-{Guid.NewGuid():N}.ogg");

        using Transcoder transcoder = Transcoder.New(source, destination, OggVorbis);
        Element? pipeline = transcoder.GetPipeline();

        Assert.NotNull(pipeline);
        Assert.Equal("uritranscodebin", pipeline!.GetName());
    }

    /// <summary>
    /// A message that carries no structure at all, and one whose structure is
    /// not the payload of the API bus, are refused rather than parsed.
    /// </summary>
    [Fact]
    public void TheParsesRefuseAMessageThatIsNotATranscoderMessage()
    {
        GstTranscoder.Initialize();

        // IsTranscoderMessage is not asked about the message without a
        // structure: gst_transcoder_is_transcoder_message guards that case with
        // g_return_val_if_fail and logs a GLib critical for it by design. The
        // application message below answers the same false without one.
        using Message eos = Message.NewEos(null);
        Assert.Throws<ArgumentException>(() => TranscoderMessageExtensions.ParseType(eos));
        Assert.Throws<ArgumentException>(
            () => TranscoderMessageExtensions.ParseError(eos, out GException _, out Structure? _));
        Assert.Throws<ArgumentException>(
            () => TranscoderMessageExtensions.ParseWarning(eos, out GException _, out Structure? _));

        using Structure payload = Structure.NewEmpty("something-else");
        using Message application = Message.NewApplication(null, payload);

        Assert.False(Transcoder.IsTranscoderMessage(application));
        Assert.Throws<ArgumentException>(
            () => TranscoderMessageExtensions.ParseError(application, out GException _, out Structure? _));
    }

    /// <summary>
    /// A message that carries the payload of the API bus but no message type
    /// is refused as well, rather than read as the first member of the
    /// enumeration.
    /// </summary>
    [Fact]
    public void TheParsesRefuseAPayloadWithoutAMessageType()
    {
        GstTranscoder.Initialize();

        using Structure payload = Structure.NewEmpty(MessageDataName);
        using Message message = Message.NewApplication(null, payload);

        // The C function only looks at the name, so the message is one of the
        // API bus as far as it is concerned; the parses look further.
        Assert.True(Transcoder.IsTranscoderMessage(message));
        Assert.Throws<ArgumentException>(() => TranscoderMessageExtensions.ParseType(message));
        Assert.Throws<ArgumentException>(
            () => TranscoderMessageExtensions.ParseError(message, out GException _, out Structure? _));
    }

    /// <summary>
    /// A message of the API bus of another kind, and one of the right kind with
    /// no error in it, are refused by the parse that was asked for.
    /// </summary>
    [Fact]
    public void TheParsesRefuseTheWrongKindAndAMissingError()
    {
        GstTranscoder.Initialize();

        // Reading the name of a member registers the enumeration with GObject,
        // which is what lets the payload below be built from its serialization.
        Assert.False(string.IsNullOrEmpty(TranscoderMessageExtensions.GetName(TranscoderMessage.Done)));

        using Structure? donePayload = Structure.NewFromString(
            MessageDataName + ", transcoder-message-type=(GstTranscoderMessage)done;");

        Assert.NotNull(donePayload);

        using Message doneMessage = Message.NewApplication(null, donePayload!);

        Assert.Equal(TranscoderMessage.Done, TranscoderMessageExtensions.ParseType(doneMessage));
        Assert.Throws<ArgumentException>(
            () => TranscoderMessageExtensions.ParseError(doneMessage, out GException _, out Structure? _));

        using Structure? errorPayload = Structure.NewFromString(
            MessageDataName + ", transcoder-message-type=(GstTranscoderMessage)error;");

        Assert.NotNull(errorPayload);

        using Message errorMessage = Message.NewApplication(null, errorPayload!);

        Assert.Equal(TranscoderMessage.Error, TranscoderMessageExtensions.ParseType(errorMessage));

        // The kind is right and the error field is missing, which is the branch
        // the imported parse turns into an abort.
        Assert.Throws<ArgumentException>(
            () => TranscoderMessageExtensions.ParseError(errorMessage, out GException _, out Structure? _));
    }

    /// <summary>
    /// A warning of the API bus that carries details is read into the exception
    /// and into a structure of the caller's own.
    /// </summary>
    /// <remarks>
    /// This is the other half of the body both parses share. The four errors
    /// the transcoder raises itself carry no details, and every error it
    /// forwards from the bus of its own pipeline carries them, because
    /// <c>gsttranscoder.c</c> synthesises an empty structure for a message that
    /// had none; provoking the second through a real transcoding would race the
    /// detail-less post of a state change that failed at the same moment, so
    /// the payload is built here instead. It is also the one fact that reads a
    /// warning rather than an error, which is the same body with the other
    /// field name.
    /// </remarks>
    [Fact]
    public void AWarningIsParsedWithTheErrorAndTheDetailsItCarries()
    {
        GstTranscoder.Initialize();

        // Reading the name of a member registers the enumeration with GObject,
        // which is what lets the payload below be built from its serialization.
        Assert.False(string.IsNullOrEmpty(TranscoderMessageExtensions.GetName(TranscoderMessage.Warning)));

        using Structure? payload = Structure.NewFromString(
            MessageDataName + ", transcoder-message-type=(GstTranscoderMessage)warning;");

        Assert.NotNull(payload);

        // Nothing public builds a GError: gst_message_new_error copies the
        // exception into the "gerror" field of the structure of its own
        // message, and that value is what a payload of the API bus carries.
        Quark domain = Quark.FromString("gstsharp-transcoder-tests");
        GException reported = new(domain, 42, "a warning the transcoder would post");

        using (Message carrier = Message.NewError(null, reported, "debug"))
        {
            using Structure carrierData = carrier.GetStructure()
                ?? throw new InvalidOperationException("an error message carries no structure");
            using Gst.GObject.Value gerror = carrierData.GetValue("gerror");

            Assert.False(gerror.IsEmpty);
            payload!.SetValue("warning", gerror);
        }

        using (Structure carried = Structure.NewEmpty("details"))
        {
            // g_value_set_boxed copies, so the structure of the test stays the
            // structure of the test.
            using Gst.GObject.Value boxed = Gst.GObject.Value.New(carried.BoxedType);
            boxed.SetBoxed(carried.Handle);
            payload!.SetValue(IssueDetailsField, boxed);
        }

        using Message message = Message.NewApplication(null, payload!);

        Assert.True(Transcoder.IsTranscoderMessage(message));
        Assert.Equal(TranscoderMessage.Warning, TranscoderMessageExtensions.ParseType(message));

        TranscoderMessageExtensions.ParseWarning(message, out GException warning, out Structure? details);
        using (details)
        {
            Assert.Equal(domain, warning.Domain);
            Assert.Equal(42, warning.Code);
            Assert.Equal("a warning the transcoder would post", warning.Message);

            // The details are an owned copy of what the message carries, which
            // is the arm a message without them never reaches.
            Assert.NotNull(details);
            Assert.True(details!.HasName("details"));
        }

        // The kind is read before the field, so the error parse refuses the
        // very message the warning parse has just read.
        Assert.Throws<ArgumentException>(
            () => TranscoderMessageExtensions.ParseError(message, out GException _, out Structure? _));
    }

    /// <summary>
    /// The pair of files one transcoding fact works on: a small ogg the fact
    /// writes itself, and the destination the transcoder writes.
    /// </summary>
    private sealed class TranscodeFiles : IDisposable
    {
        private TranscodeFiles(string sourcePath, string destinationPath)
        {
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
        }

        /// <summary>Gets the path of the generated source file.</summary>
        internal string SourcePath { get; }

        /// <summary>Gets the path the transcoder writes to.</summary>
        internal string DestinationPath { get; }

        /// <summary>Gets the source as a URI.</summary>
        internal string SourceUri => new System.Uri(SourcePath).AbsoluteUri;

        /// <summary>Gets the destination as a URI.</summary>
        internal string DestinationUri => new System.Uri(DestinationPath).AbsoluteUri;

        /// <summary>
        /// Writes a short ogg/vorbis file and names a destination beside it.
        /// </summary>
        /// <returns>The pair, which the caller has to dispose.</returns>
        internal static TranscodeFiles Create()
        {
            string stem = FormattableString.Invariant($"gstsharp-transcode-{Guid.NewGuid():N}");
            string source = Path.Combine(Path.GetTempPath(), stem + "-in.ogg");
            string destination = Path.Combine(Path.GetTempPath(), stem + "-out.ogg");

            WriteOgg(source);
            return new TranscodeFiles(source, destination);
        }

        /// <summary>Deletes both files.</summary>
        public void Dispose()
        {
            Delete(SourcePath);
            Delete(DestinationPath);
        }

        private static void Delete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A file the pipeline still holds open is left behind rather
                // than failing a test that has already made its point.
            }
        }

        private static void WriteOgg(string path)
        {
            // The location is set as a property rather than spelled into the
            // description, so that a Windows path needs no escaping.
            if (Global.ParseLaunch(
                    "audiotestsrc num-buffers=40 ! audioconvert ! vorbisenc ! oggmux ! filesink name=sink")
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
