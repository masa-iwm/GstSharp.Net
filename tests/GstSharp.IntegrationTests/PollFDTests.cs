using Gst;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The poll descriptors of a bus and of a <see cref="Poll"/> set, against the
/// library that is installed: what the descriptor reports and when it stops
/// reporting it is decided by the C, and this is where that shows.
/// </summary>
/// <remarks>
/// The bus keeps a counter beside its descriptor and only raises it when the
/// queue goes from empty to holding something, and only releases it when the
/// last message is popped (gstpoll.c:281-330, gstbus.c:396, :560-563). That is
/// what makes the wait here answer nothing again after the message was taken.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class PollFDTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public PollFDTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The descriptor of a bus reports a message that was posted and stops
    /// reporting it once it was popped.
    /// </summary>
    [Fact]
    public void TheDescriptorOfABusReportsAPendingMessage()
    {
        using Bus bus = Bus.New(enableAsync: true);

        Gst.GLib.PollFD fd = bus.GetPollfd();
        _output.WriteLine(FormattableString.Invariant(
            $"bus fd={fd.Fd}, events={fd.Events}, revents={fd.Revents}"));

        Assert.NotEqual(-1, fd.Fd);
        Assert.True(fd.Events.HasFlag(Gst.GLib.IOCondition.In));

        Gst.GLib.PollFD[] set = [fd];

        // Nothing has been posted, so the wait has nothing to report.
        Assert.Equal(0, Gst.GLib.PollFD.Poll(set, 0));

        using (Structure payload = Structure.NewEmpty("GstSharpPollProbe"))
        {
            using Message message = Message.NewApplication(null, payload);
            Assert.True(bus.Post(message));
        }

        Assert.Equal(1, Gst.GLib.PollFD.Poll(set, 1000));

        // On Windows g_poll answers a signalled HANDLE with revents equal to
        // the events it was asked for, so the bit is asserted rather than the
        // whole field.
        Assert.True(set[0].Revents.HasFlag(Gst.GLib.IOCondition.In));

        using (Message? popped = bus.Pop())
        {
            Assert.NotNull(popped);
        }

        // The pop released the wakeup, because it emptied the queue.
        Assert.Equal(0, Gst.GLib.PollFD.Poll(set, 0));
    }

    /// <summary>
    /// The descriptor of a poll set is filled entirely by the library: the
    /// three events it watches for, nothing reported yet, and a descriptor
    /// that exists.
    /// </summary>
    [Fact]
    public void TheDescriptorOfAPollSetIsFilledByTheLibrary()
    {
        nint handle = PollNatives.New(1);
        Assert.NotEqual(nint.Zero, handle);

        Poll set = Assert.IsType<Poll>(Poll.FromNative(handle));
        try
        {
            Gst.GLib.PollFD fd = set.GetReadGpollfd();
            _output.WriteLine(FormattableString.Invariant(
                $"poll fd={fd.Fd}, events={fd.Events}, revents={fd.Revents}"));

            Assert.Equal(
                Gst.GLib.IOCondition.In | Gst.GLib.IOCondition.Hup | Gst.GLib.IOCondition.Err,
                fd.Events);
            Assert.Equal(default, fd.Revents);
            Assert.NotEqual(-1, fd.Fd);
        }
        finally
        {
            set.Free();
        }
    }

    /// <summary>
    /// A wait over no descriptor at all is refused rather than handed on: on
    /// Windows it is a failed wait and on Unix a wait with no timeout over
    /// nothing never returns.
    /// </summary>
    [Fact]
    public void AWaitOverNoDescriptorIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Gst.GLib.PollFD.Poll(System.Span<Gst.GLib.PollFD>.Empty, 0));
    }
}
