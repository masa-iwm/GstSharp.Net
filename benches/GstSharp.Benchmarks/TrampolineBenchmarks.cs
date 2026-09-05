using BenchmarkDotNet.Attributes;
using Gst;

namespace GstSharp.Benchmarks;

/// <summary>
/// What a managed vfunc costs against the native element it replaces.
/// </summary>
/// <remarks>
/// <para>
/// Both pipelines are <c>fakesrc num-buffers=N sizetype=fixed sizemax=64 ! X !
/// fakesink sync=false</c> and differ only in <c>X</c>: a native
/// <c>identity</c> in the baseline, a <see cref="ManagedIdentityTransform"/> in
/// the variant. One operation is one run of the pipeline from
/// <see cref="State.Null"/> to the end of the stream, so the difference between
/// the two rows is <see cref="Buffers"/> crossings of the <c>transform_ip</c>
/// trampoline plus the state cycle both share.
/// </para>
/// <para>
/// The source paints nothing and the buffers are 64 bytes, because anything a
/// source has to produce is work both rows do and the trampoline does not:
/// under <c>videotestsrc</c> at 320x240 the per buffer dispatch disappears
/// below the rendering. <c>signal-handoffs</c> is turned off on the native
/// <c>identity</c>, where it is on by default, so that the baseline does not
/// emit a signal per buffer that the managed filter has no counterpart for.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TrampolineBenchmarks
{
    /// <summary>How many buffers one run of a pipeline carries.</summary>
    public const int Buffers = 20000;

    private static readonly ClockTime BusTimeout = ClockTime.FromSeconds(30);

    // Assigned in GlobalSetup, which BenchmarkDotNet runs before any benchmark
    // of the instance; null! rather than a nullable field so that the
    // benchmark bodies stay free of checks that would be measured.
    private Pipeline nativePipeline = null!;

    private Pipeline managedPipeline = null!;

    private Element managedFilter = null!;

    private Element nativeFilter = null!;

    /// <summary>Builds both pipelines, so that only the runs are measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        GstRuntime.EnsureInitialised();

        this.nativeFilter = GstRuntime.NewElement("identity", "filter");

        // On by default: identity emits a handoff signal per buffer, which the
        // managed filter has nothing equivalent to. Off, the baseline is the
        // native transform_ip and nothing else.
        this.nativeFilter.SetProperty("signal-handoffs", false);
        this.nativePipeline = BuildPipeline("native-identity", this.nativeFilter);

        this.managedFilter = new ManagedIdentityTransform();
        this.managedPipeline = BuildPipeline("managed-identity", this.managedFilter);
    }

    /// <summary>Tears both pipelines down in the order they were built.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _ = this.nativePipeline.SetState(State.Null);
        _ = this.managedPipeline.SetState(State.Null);

        this.nativePipeline.Dispose();
        this.managedPipeline.Dispose();

        this.nativeFilter.Dispose();
        this.managedFilter.Dispose();
    }

    /// <summary>Runs the pipeline whose filter is the native element.</summary>
    /// <returns>How many buffers the run carried.</returns>
    [Benchmark(Baseline = true)]
    public int NativeIdentity() => RunToEos(this.nativePipeline);

    /// <summary>Runs the pipeline whose filter is a managed subclass.</summary>
    /// <returns>How many buffers the run carried.</returns>
    [Benchmark]
    public int ManagedIdentity() => RunToEos(this.managedPipeline);

    private static Pipeline BuildPipeline(string name, Element filter)
    {
        Pipeline pipeline = Pipeline.New(name);
        Element source = GstRuntime.NewElement("fakesrc", "source");
        Element sink = GstRuntime.NewElement("fakesink", "sink");

        // num-buffers bounds the run; sync=false lets it run as fast as the
        // machine can, so the number below is dispatch and not the clock.
        source.SetProperty("num-buffers", Buffers);

        // 2 is "fixed" in GstFakeSrcSizeType: every buffer carries sizemax
        // bytes. The default, "empty", pushes buffers with no memory at all;
        // 64 bytes is small enough that the source is not what is measured and
        // real enough that the buffers carry something.
        source.SetProperty("sizetype", 2);
        source.SetProperty("sizemax", 64);

        sink.SetProperty("sync", false);

        if (!pipeline.AddMany(source, filter, sink) || !source.Link(filter) || !filter.Link(sink))
        {
            throw new InvalidOperationException($"The {name} pipeline could not be built.");
        }

        // The source and the sink now belong to the pipeline, which disposes
        // them with itself. The filter is owned by the caller, which built it
        // and keeps it past the pipeline.
        source.Dispose();
        sink.Dispose();

        return pipeline;
    }

    private static int RunToEos(Pipeline pipeline)
    {
        Bus bus = pipeline.GetBus();

        if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
        {
            throw new InvalidOperationException("The pipeline refused to play.");
        }

        try
        {
            using Message? message = bus.TimedPopFiltered(BusTimeout, MessageType.Eos | MessageType.Error);

            if (message is null || message.Type != MessageType.Eos)
            {
                throw new InvalidOperationException(
                    $"The pipeline ended with {message?.Type.ToString() ?? "a timeout"} instead of the end of the stream.");
            }
        }
        finally
        {
            _ = pipeline.SetState(State.Null);
        }

        return Buffers;
    }
}
