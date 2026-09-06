using System.Diagnostics;
using Gst;
using Gst.App;
using Gst.GObject;
using Gst.Interop;
using Gst.Video;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Authoring a metadata implementation against the running library:
/// <see cref="Gst.Meta.Register{T}"/> writes the six function fields of a
/// <c>GstMetaInfo</c> and <see cref="Gst.Meta.Payload{T}"/> reaches the bytes
/// that follow the header of one item.
/// </summary>
/// <remarks>
/// <para>
/// A registration is permanent within the process: the library keeps every
/// implementation it registered in a table it only empties in
/// <c>gst_deinit</c>, and it refuses a name that is already a GType name. Each
/// test therefore owns a name of its own, and the helper below registers a name
/// at most once per process and hands the same probe back if it is asked again.
/// </para>
/// <para>
/// Everything measured here is on the 1.24 floor: <c>gst_meta_info_new</c>,
/// <c>gst_meta_info_register</c> and the serialisation half of the callback set
/// all arrived in that release.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MetaAuthorTests
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.Ordinal);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public MetaAuthorTests(ITestOutputHelper output)
    {
        _output = output;

        // The pipeline test casts to AppSrc and AppSink, which the type
        // registry only knows about once the module initialiser has run.
        GstApp.Initialize();
    }

    /// <summary>
    /// The whole round trip: the implementation is accepted, the item is
    /// attached, the initialisation saw what the attachment was given, and the
    /// payload survives.
    /// </summary>
    [Fact]
    public void AnItemCarriesThePayloadItWasRegisteredWith()
    {
        Registration registration = Register("GstSharpTestMetaA");

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        nint parameters = 0x1234;

        Meta item = Assert.IsType<Meta>(buffer.AddMeta(registration.Info, parameters));

        // The payload is zero filled before the initialisation runs, whatever
        // the library allocated the item with.
        Assert.Equal(0, item.Payload<Pair>().First);
        Assert.Equal(0, item.Payload<Pair>().Second);

        Assert.Equal(1, registration.Probe.InitCalls);
        Assert.Equal(parameters, registration.Probe.LastParams);
        Assert.Equal(buffer.Handle, registration.Probe.LastBuffer);

        item.Payload<Pair>().First = 11;
        item.Payload<Pair>().Second = 22;

        Meta found = Assert.IsType<Meta>(buffer.GetMeta(registration.Api));
        Assert.Equal(11, found.Payload<Pair>().First);
        Assert.Equal(22, found.Payload<Pair>().Second);
        Assert.Equal(registration.Info.Type, found.Info.Type);
    }

    /// <summary>
    /// An initialisation that refuses the item is the documented way of failing
    /// an attachment: the attachment answers nothing and the release delegate
    /// never runs, because the library frees the item without calling it.
    /// </summary>
    [Fact]
    public void AnInitialisationThatRefusesLeavesNoItemAndRunsNoRelease()
    {
        Registration registration = Register("GstSharpTestMetaB", free: true);
        registration.Probe.RefuseInit = true;

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        Assert.Null(buffer.AddMeta(registration.Info, 0));

        Assert.Equal(1, registration.Probe.InitCalls);
        Assert.Equal(0, registration.Probe.FreeCalls);
        Assert.Null(buffer.GetMeta(registration.Api));
    }

    /// <summary>
    /// A copy runs the transformation on the item of the SOURCE buffer, and the
    /// delegate is the one that puts an item on the destination; a registration
    /// without a transformation is simply not carried.
    /// </summary>
    /// <remarks>
    /// The transformation data of a copy is the address of a
    /// <c>GstMetaTransformCopy</c>, which the delegate reads by casting the
    /// pointer it is handed - the projection is a plain structure, so reading
    /// it is a copy of three words and nothing is owned by either side. The
    /// kind of transformation is the quark of <c>"gst-copy"</c>.
    /// </remarks>
    [Fact]
    public unsafe void ACopyCarriesAnItemOnlyThroughATransformation()
    {
        Registration carried = Register("GstSharpTestMetaC", transform: true);
        Registration dropped = Register("GstSharpTestMetaD");

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        Meta item = Assert.IsType<Meta>(buffer.AddMeta(carried.Info, 0));
        item.Payload<Pair>().First = 5;
        item.Payload<Pair>().Second = 6;

        Meta other = Assert.IsType<Meta>(buffer.AddMeta(dropped.Info, 0));
        other.Payload<Pair>().First = 7;

        using Buffer copy = Assert.IsType<Buffer>(buffer.Copy());

        Assert.Equal(1, carried.Probe.TransformCalls);
        Assert.Equal(Gst.GLib.Quark.FromString("gst-copy"), carried.Probe.LastTransformType);
        Assert.False(carried.Probe.LastTransformRegion);

        // The source item is the one the delegate was handed: it is still on
        // the buffer it came from. The field is one of its own, because the
        // attachment the delegate itself does runs the initialisation on the
        // destination buffer a moment later.
        Assert.Equal(buffer.Handle, carried.Probe.LastTransformSource);

        Meta copied = Assert.IsType<Meta>(copy.GetMeta(carried.Api));
        Assert.Equal(5, copied.Payload<Pair>().First);
        Assert.Equal(6, copied.Payload<Pair>().Second);

        // The other registration has no transformation, which is what a null
        // transform_func means in C: the item is not carried at all.
        Assert.Equal(0, dropped.Probe.TransformCalls);
        Assert.Null(copy.GetMeta(dropped.Api));
    }

    /// <summary>
    /// The release delegate runs on an explicit removal and on the disposal of
    /// the buffer, and the wrapper it was handed is dead as soon as it returns.
    /// </summary>
    [Fact]
    public void TheReleaseRunsOnRemovalAndOnDisposalAndLeavesADeadWrapper()
    {
        Registration registration = Register("GstSharpTestMetaE", free: true);

        using (Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null)))
        {
            Meta item = Assert.IsType<Meta>(buffer.AddMeta(registration.Info, 0));

            Assert.True(buffer.RemoveMeta(item));
            Assert.Equal(1, registration.Probe.FreeCalls);

            Meta released = Assert.IsType<Meta>(registration.Probe.LastMeta);
            Assert.Throws<ObjectDisposedException>(() => released.Payload<Pair>());
        }

        Buffer second = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        Assert.NotNull(second.AddMeta(registration.Info, 0));
        second.Dispose();

        Assert.Equal(2, registration.Probe.FreeCalls);
        Assert.Throws<ObjectDisposedException>(() => registration.Probe.LastMeta!.Payload<Pair>());
    }

    /// <summary>
    /// The payload accessor answers only for the implementations this process
    /// registered, and only for the type they were registered with.
    /// </summary>
    [Fact]
    public void ThePayloadIsRefusedForAnotherImplementationAndForAnotherType()
    {
        Registration registration = Register("GstSharpTestMetaF");

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        Meta item = Assert.IsType<Meta>(buffer.AddMeta(registration.Info, 0));
        Assert.Throws<InvalidCastException>(() => item.Payload<long>());

        // A metadata implementation of the library itself: its item has no
        // managed payload at all, whatever type is asked for.
        using Buffer frame = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16 * 16 * 3, null));
        Assert.NotNull(VideoGlobal.BufferAddVideoMeta(frame, VideoFrameFlags.None, VideoFormat.Rgb, 16, 16));

        Meta native = Assert.IsType<Meta>(frame.GetMeta(VideoGlobal.VideoMetaApiGetType()));
        Assert.Throws<InvalidCastException>(() => native.Payload<Pair>());
    }

    /// <summary>
    /// A name that is already a GType name is refused, and the refusal leaves
    /// the registration that owns the name working.
    /// </summary>
    [Fact]
    public void ADuplicateImplementationNameIsRefused()
    {
        Registration registration = Register("GstSharpTestMetaG");

        GType api = Meta.ApiTypeRegister("GstSharpTestMetaGSecondApi", []);
        Assert.NotEqual(0UL, (ulong)api.Value);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => Meta.Register<Pair>(api, "GstSharpTestMetaG"));
        _output.WriteLine(refused.Message);

        // The registration that owns the name is untouched by the refusal.
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        Meta item = Assert.IsType<Meta>(buffer.AddMeta(registration.Info, 0));
        item.Payload<Pair>().First = 3;
        Assert.Equal(3, Assert.IsType<Meta>(buffer.GetMeta(registration.Api)).Payload<Pair>().First);
    }

    /// <summary>
    /// The serialisation and the deserialisation are the two halves of one wire
    /// format, and the version one writes is the version the other is handed.
    /// </summary>
    [Fact]
    public void ThePayloadSurvivesASerialisationRoundTrip()
    {
        Registration registration = Register("GstSharpTestMetaH", serialize: true, deserialize: true);

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        Meta item = Assert.IsType<Meta>(buffer.AddMeta(registration.Info, 0));
        item.Payload<Pair>().First = 101;
        item.Payload<Pair>().Second = -7;

        byte[] bytes = Assert.IsType<byte[]>(item.Serialize());
        _output.WriteLine($"serialised {bytes.Length} bytes");

        using Buffer target = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        Meta read = Assert.IsType<Meta>(Meta.Deserialize(target, bytes, out uint consumed));

        Assert.Equal((uint)bytes.Length, consumed);
        Assert.Equal(Probe.Version, registration.Probe.LastVersion);
        Assert.Equal(101, read.Payload<Pair>().First);
        Assert.Equal(-7, read.Payload<Pair>().Second);

        // The item the delegate answered is the one that is on the buffer now.
        Assert.Equal(101, Assert.IsType<Meta>(target.GetMeta(registration.Api)).Payload<Pair>().First);
    }

    /// <summary>
    /// A delegate that throws is caught on the boundary: the copy that was
    /// running completes, the item is not carried, and the trap saw the
    /// exception.
    /// </summary>
    [Fact]
    public void AThrowingDelegateIsTrappedAndTheCopyGoesOn()
    {
        Registration registration = Register("GstSharpTestMetaI", transform: true);
        registration.Probe.ThrowOnTransform = true;

        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
            Assert.NotNull(buffer.AddMeta(registration.Info, 0));

            using Buffer copy = Assert.IsType<Buffer>(buffer.Copy());

            // The copy itself is a buffer like any other; only the item is
            // missing, because the delegate never got as far as adding one.
            Assert.Equal(16UL, copy.GetSize());
            Assert.Null(copy.GetMeta(registration.Api));
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        lock (failures)
        {
            Exception trapped = Assert.Single(failures);
            Assert.IsType<InvalidOperationException>(trapped);
            Assert.Contains("GstSharpTestMetaI", trapped.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The transformation runs from a streaming thread with no managed code
    /// anywhere near the item first, which is the case the trampolines exist
    /// for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The carrier is <c>videoconvert</c>, and the choice is forced: the
    /// default <c>transform_meta</c> of <c>GstBaseTransform</c> carries an item
    /// only when its API type has no tags at all, and only a transformation
    /// that is not in passthrough copies metadata in the first place. So the
    /// API of this test is registered with an empty tag list and the pipeline
    /// converts between two real formats.
    /// </para>
    /// <para>
    /// The assertion that matters is the thread: the delegate runs on the
    /// streaming thread of the conversion, not on the thread that pushed the
    /// buffer in.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATransformationRunsOnTheStreamingThreadOfAPipeline()
    {
        Registration registration = Register("GstSharpTestMetaJ", transform: true);

        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "appsrc name=src format=time caps=video/x-raw,format=I420,width=16,height=16,framerate=30/1 ! "
            + "videoconvert ! video/x-raw,format=RGB ! appsink name=sink sync=false"));

        using Element? sourceElement = pipeline.GetByName("src");
        AppSrc source = Assert.IsType<AppSrc>(sourceElement);
        using Element? sinkElement = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(sinkElement);

        Buffer frame = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16 * 16 * 3 / 2, null));
        Meta item = Assert.IsType<Meta>(frame.AddMeta(registration.Info, 0));
        item.Payload<Pair>().First = 42;
        item.Payload<Pair>().Second = 43;

        int caller = Environment.CurrentManagedThreadId;

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));
            Assert.Equal(FlowReturn.Ok, source.PushBuffer(frame));
            Assert.Equal(FlowReturn.Ok, source.EndOfStream());

            Sample? sample = null;
            Stopwatch elapsed = Stopwatch.StartNew();
            while (sample is null && elapsed.Elapsed < RunTimeout)
            {
                sample = sink.TryPullSample(ClockTime.FromMilliseconds(100));
                if (sample is null && sink.IsEos())
                {
                    break;
                }
            }

            Assert.NotNull(sample);
            using (sample)
            {
                using Buffer? converted = sample.GetBuffer();
                Assert.NotNull(converted);

                Meta carried = Assert.IsType<Meta>(converted.GetMeta(registration.Api));
                Assert.Equal(42, carried.Payload<Pair>().First);
                Assert.Equal(43, carried.Payload<Pair>().Second);
            }

            _output.WriteLine(
                $"transformed on thread {registration.Probe.LastTransformThread}, pushed from {caller}");

            Assert.Equal(1, registration.Probe.TransformCalls);
            Assert.NotEqual(caller, registration.Probe.LastTransformThread);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// The cheaper half of the same statement: a copy from a worker thread runs
    /// the delegate on that worker.
    /// </summary>
    [Fact]
    public void ACopyFromAWorkerRunsTheTransformationOnTheWorker()
    {
        Registration registration = Register("GstSharpTestMetaK", transform: true);

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        Meta item = Assert.IsType<Meta>(buffer.AddMeta(registration.Info, 0));
        item.Payload<Pair>().First = 9;

        int caller = Environment.CurrentManagedThreadId;

        Buffer? copied = null;
        Thread worker = new(() => copied = Assert.IsType<Buffer>(buffer.Copy()));
        worker.Start();
        Assert.True(worker.Join(RunTimeout), "the worker did not finish the copy.");

        using Buffer copy = Assert.IsType<Buffer>(copied);

        Assert.Equal(9, Assert.IsType<Meta>(copy.GetMeta(registration.Api)).Payload<Pair>().First);
        Assert.Equal(1, registration.Probe.TransformCalls);
        Assert.NotEqual(caller, registration.Probe.LastTransformThread);
    }

    /// <summary>
    /// Registers one implementation, or hands back the one this process already
    /// registered under the same name.
    /// </summary>
    /// <param name="name">The implementation name, unique per test.</param>
    /// <param name="free">Whether a release delegate is installed.</param>
    /// <param name="transform">Whether a transformation delegate is installed.</param>
    /// <param name="serialize">Whether a serialisation delegate is installed.</param>
    /// <param name="deserialize">Whether a deserialisation delegate is installed.</param>
    /// <returns>The API type, the implementation and the probe behind them.</returns>
    private static Registration Register(
        string name,
        bool free = false,
        bool transform = false,
        bool serialize = false,
        bool deserialize = false)
    {
        lock (Registrations)
        {
            if (Registrations.TryGetValue(name, out Registration? existing))
            {
                existing.Probe.Reset();
                return existing;
            }

            // The tag list is empty on purpose: GstBaseTransform carries an
            // item across a conversion only when its API type has no tags.
            GType api = Meta.ApiTypeRegister(name + "Api", []);
            Assert.NotEqual(0UL, (ulong)api.Value);

            Probe probe = new(name);
            MetaInfo info = Meta.Register<Pair>(
                api,
                name,
                probe.Init,
                free ? probe.Free : null,
                transform ? probe.Transform : null,
                serialize ? probe.Serialize : null,
                deserialize ? probe.Deserialize : null);

            Registration registration = new(api, info, probe);
            Registrations.Add(name, registration);
            return registration;
        }
    }

    /// <summary>The payload every implementation of this suite carries.</summary>
    private struct Pair
    {
        /// <summary>The first half.</summary>
        internal int First;

        /// <summary>The second half.</summary>
        internal int Second;
    }

    /// <summary>What one registration of this suite settled.</summary>
    /// <param name="Api">The metadata API type.</param>
    /// <param name="Info">The implementation block.</param>
    /// <param name="Probe">The delegates and what they recorded.</param>
    private sealed record Registration(GType Api, MetaInfo Info, Probe Probe);

    /// <summary>
    /// The delegates of one registration and everything they saw.
    /// </summary>
    /// <param name="name">The implementation name, used in the exception a test throws on purpose.</param>
    private sealed class Probe(string name)
    {
        /// <summary>The version the serialisation writes.</summary>
        internal const byte Version = 7;

        /// <summary>How often the initialisation ran.</summary>
        internal int InitCalls;

        /// <summary>How often the release ran.</summary>
        internal int FreeCalls;

        /// <summary>How often the transformation ran.</summary>
        internal int TransformCalls;

        /// <summary>The parameters the last initialisation was handed.</summary>
        internal nint LastParams;

        /// <summary>The buffer the last callback was handed.</summary>
        internal nint LastBuffer;

        /// <summary>The item the last release was handed.</summary>
        internal Meta? LastMeta;

        /// <summary>The kind of the last transformation.</summary>
        internal Gst.GLib.Quark LastTransformType;

        /// <summary>Whether the last transformation was a region copy.</summary>
        internal bool LastTransformRegion;

        /// <summary>The thread the last transformation ran on.</summary>
        internal int LastTransformThread;

        /// <summary>The buffer the item of the last transformation was on.</summary>
        internal nint LastTransformSource;

        /// <summary>The version the last deserialisation was handed.</summary>
        internal byte LastVersion;

        /// <summary>Whether the initialisation refuses the item.</summary>
        internal bool RefuseInit;

        /// <summary>Whether the transformation throws instead of working.</summary>
        internal bool ThrowOnTransform;

        /// <summary>Forgets what a previous run of the same test recorded.</summary>
        internal void Reset()
        {
            InitCalls = 0;
            FreeCalls = 0;
            TransformCalls = 0;
            LastParams = 0;
            LastBuffer = 0;
            LastMeta = null;
            LastTransformSource = 0;
            LastVersion = 0;
            RefuseInit = false;
            ThrowOnTransform = false;
        }

        /// <summary>The initialisation delegate.</summary>
        /// <param name="meta">The item being initialised.</param>
        /// <param name="parameters">What the attachment was given.</param>
        /// <param name="buffer">The buffer the item was attached to.</param>
        /// <returns>Whether the item was accepted.</returns>
        internal bool Init(Meta meta, nint parameters, Buffer buffer)
        {
            InitCalls++;
            LastParams = parameters;
            LastBuffer = buffer.Handle;
            return !RefuseInit;
        }

        /// <summary>The release delegate.</summary>
        /// <param name="meta">The item being freed.</param>
        /// <param name="buffer">The buffer that carried it.</param>
        internal void Free(Meta meta, Buffer buffer)
        {
            FreeCalls++;
            LastMeta = meta;
            LastBuffer = buffer.Handle;
        }

        /// <summary>The transformation delegate.</summary>
        /// <param name="transbuf">The buffer to add an item to.</param>
        /// <param name="meta">The item of <paramref name="buffer"/>.</param>
        /// <param name="buffer">The buffer that carries <paramref name="meta"/>.</param>
        /// <param name="type">What is being done to the buffer.</param>
        /// <param name="data">The transformation data.</param>
        /// <returns>Whether the item was carried.</returns>
        internal unsafe bool Transform(Buffer transbuf, Meta meta, Buffer buffer, Gst.GLib.Quark type, nint data)
        {
            TransformCalls++;
            LastTransformType = type;
            LastTransformThread = Environment.CurrentManagedThreadId;
            LastTransformSource = buffer.Handle;
            LastTransformRegion = data != 0 && ((MetaTransformCopy*)data)->Region != 0;

            if (ThrowOnTransform)
            {
                throw new InvalidOperationException($"The transformation of {name} refuses to run.");
            }

            Pair payload = meta.Payload<Pair>();
            if (transbuf.AddMeta(meta.Info, 0) is not { } added)
            {
                return false;
            }

            added.Payload<Pair>() = payload;
            return true;
        }

        /// <summary>The serialisation delegate.</summary>
        /// <param name="meta">The item to serialise.</param>
        /// <param name="data">The sink to append to.</param>
        /// <param name="version">The version of the format.</param>
        /// <returns>Whether the payload was written.</returns>
        internal bool Serialize(Meta meta, ByteArrayInterface data, ref byte version)
        {
            version = Version;
            Pair payload = meta.Payload<Pair>();
            Span<byte> bytes = stackalloc byte[8];
            BitConverter.TryWriteBytes(bytes, payload.First);
            BitConverter.TryWriteBytes(bytes[4..], payload.Second);
            return data.Append(bytes);
        }

        /// <summary>The deserialisation delegate.</summary>
        /// <param name="info">The implementation the bytes belong to.</param>
        /// <param name="buffer">The buffer to add the item to.</param>
        /// <param name="data">The payload that was serialised.</param>
        /// <param name="version">The version the serialisation wrote.</param>
        /// <returns>The item that was added, or <see langword="null"/>.</returns>
        internal Meta? Deserialize(MetaInfo info, Buffer buffer, ReadOnlySpan<byte> data, byte version)
        {
            LastVersion = version;
            if (data.Length < 8 || buffer.AddMeta(info, 0) is not { } added)
            {
                return null;
            }

            added.Payload<Pair>() = new Pair
            {
                First = BitConverter.ToInt32(data),
                Second = BitConverter.ToInt32(data[4..]),
            };
            return added;
        }
    }
}
