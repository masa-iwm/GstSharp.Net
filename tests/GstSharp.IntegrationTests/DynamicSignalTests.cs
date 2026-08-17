using System.Globalization;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Emitting and connecting signals by name, against the signals a real
/// GStreamer element has.
/// </summary>
/// <remarks>
/// <para>
/// This is the path an application needs for the action signals
/// that no <c>.gir</c> file describes as a method, for example the
/// <c>resize</c> of a Direct3D sink, and for the signals a plugin adds behind
/// the back of introspection.
/// </para>
/// <para>
/// The element under test is an <c>appsink</c>, because its
/// <c>try-pull-sample</c> is an action signal with an argument and a mini
/// object return value, which is the whole conversion path in one call.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class DynamicSignalTests
{
    private const string SignalName = "try-pull-sample";

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public DynamicSignalTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The signature of an action signal is read out of GObject, and emitting
    /// it runs the call the signal stands for.
    /// </summary>
    [Fact]
    public void ActionSignalIsIntrospectedAndEmitted()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "introspected"));

        Assert.True(SignalQuery.TryLookup(sink.NativeType, SignalName, out SignalQuery query));
        _output.WriteLine(query.ToString());

        Assert.True(query.IsValid);
        Assert.Equal(SignalName, query.Name);
        Assert.True(query.IsAction);
        Assert.Equal(1, query.ParameterCount);
        Assert.Equal(GType.UInt64, query.GetParameterType(0));
        Assert.Equal("GstSample", query.ReturnType.Name);

        // The sink was never started and the timeout is zero, so the emission
        // returns no sample at all, which is a null boxed value.
        Assert.Null(sink.EmitSignal(SignalName, 0UL));
        Assert.Equal(nint.Zero, sink.EmitSignal<nint>(SignalName, 0UL));
    }

    /// <summary>
    /// An argument that does not fit the signature is refused before anything
    /// is emitted, and the message says what the signal expects.
    /// </summary>
    [Fact]
    public void WrongArgumentsAreRefusedWithTheExpectation()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "refusing"));

        ArgumentException missing = Assert.Throws<ArgumentException>(() => sink.EmitSignal(SignalName));
        _output.WriteLine(missing.Message);
        Assert.Contains("takes 1 argument(s), and 0 were given", missing.Message, StringComparison.Ordinal);

        ArgumentException wrongType =
            Assert.Throws<ArgumentException>(() => sink.EmitSignal(SignalName, "not a timeout"));
        _output.WriteLine(wrongType.Message);
        Assert.Contains("guint64", wrongType.Message, StringComparison.Ordinal);
        Assert.Contains("System.String", wrongType.Message, StringComparison.Ordinal);

        // A negative number does not fit an unsigned argument either.
        Assert.Throws<ArgumentException>(() => sink.EmitSignal(SignalName, -1));
    }

    /// <summary>
    /// A signal that does not exist fails immediately, and the message lists
    /// the ones that do.
    /// </summary>
    [Fact]
    public void UnknownSignalNamesTheOnesThatExist()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "unknown"));

        ArgumentException emitting = Assert.Throws<ArgumentException>(() => sink.EmitSignal("no-such-signal"));
        _output.WriteLine(emitting.Message);
        Assert.Contains("new-sample", emitting.Message, StringComparison.Ordinal);

        ArgumentException connecting = Assert.Throws<ArgumentException>(
            () => sink.ConnectSignal("no-such-signal", static (sender, args) => null));

        Assert.Contains("new-sample", connecting.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A handler connected by name receives the arguments of the emission, and
    /// disconnecting it stops the handler for good.
    /// </summary>
    [Fact]
    public void DynamicHandlerReceivesTheArgumentsAndStopsWhenRemoved()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "watched"));

        int fired = 0;
        string? changed = null;

        ulong handler = sink.ConnectSignal("notify::name", (sender, args) =>
        {
            fired++;
            Assert.Same(sink, sender);

            // notify carries the parameter specification of the property that
            // changed, borrowed for the duration of this call.
            ParamSpec specification = Assert.IsType<ParamSpec>(Assert.Single(args));
            changed = specification.Name;
            return null;
        });

        Assert.NotEqual(0uL, handler);

        Rename(sink, "renamed");

        Assert.Equal(1, fired);
        Assert.Equal("name", changed);

        sink.RemoveHandler(handler);
        Assert.False(SignalRegistry.IsConnected(sink.Handle, handler));

        Rename(sink, "renamed-again");
        Assert.Equal(1, fired);
    }

    /// <summary>
    /// What a dynamic handler returns is converted against the return type of
    /// the signal and reaches the code that emitted it.
    /// </summary>
    [Fact]
    public void DynamicHandlerReturnValueReachesTheEmitter()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "answering"));

        // new-sample returns a GstFlowReturn, and GST_FLOW_CUSTOM_SUCCESS is 100:
        // an enumeration reaches this layer as its number, because the generated
        // enumerations are not visible from the runtime.
        const int CustomSuccess = 100;

        ulong handler = sink.ConnectSignal("new-sample", static (sender, args) =>
        {
            Assert.Empty(args);
            return CustomSuccess;
        });

        try
        {
            Assert.Equal(CustomSuccess, sink.EmitSignal<int>("new-sample"));
        }
        finally
        {
            sink.RemoveHandler(handler);
        }

        // Without a handler the signal returns what the emission started with.
        Assert.Equal(0, sink.EmitSignal<int>("new-sample"));
    }

    /// <summary>
    /// A mini object that an action signal returns outlives the emission: the
    /// value that carried it back is unset as soon as the emission is over, so
    /// the caller is handed a reference of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole path in one test: a buffer is pushed into an
    /// <c>appsrc</c> through the <c>push-buffer</c> action signal, which takes
    /// a mini object as an argument, and pulled out of the <c>appsink</c> at
    /// the other end through <c>try-pull-sample</c>, which returns one.
    /// </para>
    /// <para>
    /// The sample comes back as its wrapper rather than as a raw handle, and
    /// the wrapper owns the reference the emission would otherwise have
    /// released with the value that carried it. Comparing the reference count
    /// against the one the C call leaves behind is what shows that.
    /// </para>
    /// </remarks>
    [Fact]
    public unsafe void BoxedReturnValueIsOwnedByTheCaller()
    {
        const ulong FiveSeconds = 5_000_000_000UL;

        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "appsrc name=source caps=video/x-raw,format=GRAY8,width=2,height=2,framerate=0/1 ! appsink name=sink"));

        using Element source = Assert.IsAssignableFrom<Element>(pipeline.GetByName("source"));
        using Element sink = Assert.IsAssignableFrom<Element>(pipeline.GetByName("sink"));

        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

        try
        {
            nint buffer = TestNatives.BufferNewAllocate(nint.Zero, 4, nint.Zero);
            Assert.NotEqual(nint.Zero, buffer);

            try
            {
                // push-buffer takes a GstBuffer, which is a boxed type as far as
                // GObject is concerned, so it travels as its handle and the
                // emission takes a reference of its own. GST_FLOW_OK is zero.
                // Two buffers, because two samples are pulled below.
                Assert.Equal(0, source.EmitSignal<int>("push-buffer", buffer));
                Assert.Equal(0, source.EmitSignal<int>("push-buffer", buffer));
            }
            finally
            {
                GstNative.MiniObjectUnref(buffer);
            }

            // The C call is the yardstick: whatever it leaves behind is what the
            // caller of the action signal has to end up with.
            nint direct = TestNatives.AppSinkTryPullSample(sink.Handle, FiveSeconds);
            Assert.NotEqual(nint.Zero, direct);
            int expected = ((MiniObjectRaw*)direct)->Refcount;
            GstNative.MiniObjectUnref(direct);

            // A GstSample is a mini object, so the emission hands out the
            // wrapper of the binding and not the handle it used to.
            using (Sample wrapper = Assert.IsType<Sample>(sink.EmitSignal(SignalName, FiveSeconds)))
            {
                int emitted = ((MiniObjectRaw*)wrapper.Handle)->Refcount;
                _output.WriteLine(FormattableString.Invariant(
                    $"refcount after the C call: {expected}, after the emission: {emitted}"));

                // The value that carried the sample back was unset when the
                // emission ended, so the reference the wrapper holds has to be
                // one of its own. Without that, this count would be one short
                // and the wrapper would be living on borrowed time.
                Assert.Equal(expected, emitted);

                // And it really is a usable sample rather than a freed one.
                using Gst.Buffer? pulled = wrapper.GetBuffer();
                Assert.NotNull(pulled);
                Assert.Equal(4UL, (ulong)pulled.GetSize());
            }
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Ten thousand emissions and ten thousand connect and disconnect cycles
    /// leave nothing behind: every value is unset again, and every closure
    /// releases the state of its handler when it is destroyed.
    /// </summary>
    [Fact]
    public void RepeatedEmissionsAndConnectionsStayStable()
    {
        const int Rounds = 10_000;

        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "hammered"));

        long before = GC.GetTotalMemory(forceFullCollection: true);

        for (int i = 0; i < Rounds; i++)
        {
            Assert.Null(sink.EmitSignal(SignalName, 0UL));
        }

        int fired = 0;

        for (int i = 0; i < Rounds; i++)
        {
            ulong handler = sink.ConnectSignal("notify::name", (sender, args) =>
            {
                fired++;
                return null;
            });

            Assert.True(SignalRegistry.IsConnected(sink.Handle, handler));
            sink.RemoveHandler(handler);
            Assert.False(SignalRegistry.IsConnected(sink.Handle, handler));
        }

        long after = GC.GetTotalMemory(forceFullCollection: true);
        _output.WriteLine(FormattableString.Invariant($"Managed memory: {before} -> {after} bytes."));

        // Nothing fired: every handler was disconnected before anything could
        // change the name.
        Assert.Equal(0, fired);

        // The handlers of the ten thousand cycles are gone, so the state of a
        // handler is not what would be growing here.
        Assert.True(
            after - before < 2 * 1024 * 1024,
            FormattableString.Invariant($"Managed memory grew by {after - before} bytes."));

        // The last connection still works, which it would not if the bookkeeping
        // of the wrapper had drifted.
        ulong last = sink.ConnectSignal("notify::name", (sender, args) =>
        {
            fired++;
            return null;
        });

        Rename(sink, "hammered-again");
        Assert.Equal(1, fired);
        sink.RemoveHandler(last);
    }

    /// <summary>
    /// Renames an object through its property, which is what makes GObject
    /// emit <c>notify::name</c>.
    /// </summary>
    /// <param name="target">The object to rename.</param>
    /// <param name="name">The new name.</param>
    private static void Rename(Gst.GObject.Object target, string name)
    {
        using Value value = Value.CreateFor(name, GType.String);
        target.SetProperty("name", value);

        using Value written = target.GetProperty("name");
        Assert.Equal(name, written.GetString());
    }
}
