using System.Diagnostics;
using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Covers the property watch of the bus: a deep notify watch on a pipeline
/// turns a property that changed anywhere inside it into a
/// <see cref="MessageType.PropertyNotify"/> message, and reading that message
/// hands over the object, the name and a value of the caller's own.
/// </summary>
/// <remarks>
/// <para>
/// This is the surface behind <c>gst-launch-1.0 -v</c>. The watch is what moves
/// the notification off the thread that wrote the property and onto the bus,
/// which is the only shape an application without a main loop can use.
/// </para>
/// <para>
/// The bus is polled unfiltered here on purpose.
/// <see cref="MessageType.PropertyNotify"/> is one of the extended message
/// types — its value is <c>0x80000003</c> rather than a single bit — so the
/// bitwise filter of <c>gst_bus_timed_pop_filtered</c> would also match
/// <see cref="MessageType.Eos"/> and <see cref="MessageType.Error"/>, whose
/// bits it shares.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MessagePropertyNotifyTests
{
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(15);

    private static readonly ClockTime Slice = ClockTime.FromMilliseconds(100);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public MessagePropertyNotifyTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Writing a property of an element inside a watched pipeline produces a
    /// message that names the element, the property and its new value.
    /// </summary>
    [Fact]
    public void APropertyWriteArrivesAsAMessageWithItsValue()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("notify"));
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "sink"));
        using Bus bus = pipeline.GetBus();

        Assert.True(pipeline.Add(sink));

        // The watch is on the pipeline and covers every element in it, which is
        // what "deep" means: the notification travels up the parent chain.
        System.Runtime.InteropServices.CULong watch = pipeline.AddPropertyDeepNotifyWatch(null, true);

        try
        {
            // READY rather than NULL: a pipeline flushes its bus on the way back
            // to NULL, and a test that posted into a flushing bus would measure
            // nothing.
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

            using (Value silent = Value.New(GType.Boolean))
            {
                silent.SetBoolean(true);
                sink.SetProperty("silent", silent);
            }

            using Message? message = WaitForProperty(bus, "silent", MessageTimeout);

            Assert.NotNull(message);

            (Gst.Object? source, string name, Value value) = message.ParsePropertyNotify();

            using (value)
            {
                _output.WriteLine($"{source?.Name}: {name} = {Global.ValueSerialize(value)}");

                Assert.NotNull(source);
                Assert.Equal("sink", source.Name);

                // The object of the message is its source, which is what the C
                // function hands out as well.
                Assert.Equal(message.Src?.Handle, source.Handle);

                Assert.Equal("silent", name);
                Assert.False(value.IsEmpty);
                Assert.Equal(GType.Boolean, value.Type);
                Assert.True(value.GetBoolean());
                Assert.Equal("true", Global.ValueSerialize(value));
            }
        }
        finally
        {
            pipeline.RemovePropertyNotifyWatch(watch);
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// A watch installed without values posts messages without one, which is
    /// the empty value rather than a failure to read the message.
    /// </summary>
    [Fact]
    public void AWatchWithoutValuesPostsMessagesWithoutOne()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("notify-bare"));
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "sink"));
        using Bus bus = pipeline.GetBus();

        Assert.True(pipeline.Add(sink));

        System.Runtime.InteropServices.CULong watch = pipeline.AddPropertyDeepNotifyWatch(null, false);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

            using (Value silent = Value.New(GType.Boolean))
            {
                silent.SetBoolean(true);
                sink.SetProperty("silent", silent);
            }

            using Message? message = WaitForProperty(bus, "silent", MessageTimeout);

            Assert.NotNull(message);

            (Gst.Object? source, string name, Value value) = message.ParsePropertyNotify();

            using (value)
            {
                Assert.Equal("sink", source?.Name);
                Assert.Equal("silent", name);
                Assert.True(value.IsEmpty);
            }
        }
        finally
        {
            pipeline.RemovePropertyNotifyWatch(watch);
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Reading a message of another type as a property notification is a
    /// mistake that has to be reported rather than answered with an empty
    /// result.
    /// </summary>
    [Fact]
    public void AnotherMessageTypeIsRejected()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("notify-wrong-type"));
        using Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

            using Message? message = BusPump.WaitFor(bus, MessageType.StateChanged, MessageTimeout);

            Assert.NotNull(message);
            Assert.Throws<InvalidOperationException>(() => { _ = message.ParsePropertyNotify(); });
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Polls the bus until a property notification for the given property
    /// arrives.
    /// </summary>
    /// <param name="bus">The bus to poll.</param>
    /// <param name="propertyName">The property to wait for.</param>
    /// <param name="timeout">How long to wait in total.</param>
    /// <returns>
    /// The message, which the caller owns and has to dispose, or
    /// <see langword="null"/> when none arrived in time.
    /// </returns>
    /// <remarks>
    /// A deep notify watch reports every property that anything in the pipeline
    /// writes, and a state change writes a few, so the wait is for a named
    /// property rather than for the first message of the type.
    /// </remarks>
    private static Message? WaitForProperty(Bus bus, string propertyName, TimeSpan timeout)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < timeout)
        {
            Message? message = bus.TimedPop(Slice);

            if (message is null)
            {
                continue;
            }

            bool wanted = false;

            if (message.Type == MessageType.PropertyNotify)
            {
                (_, string name, Value value) = message.ParsePropertyNotify();
                value.Dispose();
                wanted = name == propertyName;
            }

            if (wanted)
            {
                return message;
            }

            message.Dispose();
        }

        return null;
    }
}
