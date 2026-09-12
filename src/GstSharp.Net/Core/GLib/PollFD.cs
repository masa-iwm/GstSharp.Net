using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Gst.GLib;

/// <summary>
/// The events a file descriptor is watched for and the ones it reports, the
/// bits of the <c>GIOCondition</c> of GLib.
/// </summary>
/// <remarks>
/// <para>
/// GLib spells the enumeration as six <c>GLIB_SYSDEF_POLL*</c> macros
/// (<c>glib/gmain.h:33-41</c>) that its build fills in from the <c>POLL*</c>
/// constants of <c>&lt;poll.h&gt;</c>. The numbers are the same everywhere
/// this binding runs: Linux and macOS define <c>POLLIN</c> 1, <c>POLLPRI</c>
/// 2, <c>POLLOUT</c> 4, <c>POLLERR</c> 8, <c>POLLHUP</c> 16 and
/// <c>POLLNVAL</c> 32, and the Windows build of GLib writes those same six
/// numbers into its <c>glibconfig.h</c> unconditionally rather than reading
/// them from Winsock, to keep the public header compatible with what its
/// autotools builds produced (the <c>poll_defines</c> table of the top level
/// <c>meson.build:2005-2029</c> of GLib).
/// </para>
/// <para>
/// The values are bits of a <c>gushort</c> field, which is why the
/// enumeration is one as well.
/// </para>
/// </remarks>
[Flags]
public enum IOCondition : ushort
{
    /// <summary>There is data to read (<c>G_IO_IN</c>).</summary>
    In = 1,

    /// <summary>There is urgent data to read (<c>G_IO_PRI</c>).</summary>
    Pri = 2,

    /// <summary>Writing is possible without blocking (<c>G_IO_OUT</c>).</summary>
    Out = 4,

    /// <summary>An error happened (<c>G_IO_ERR</c>).</summary>
    Err = 8,

    /// <summary>The other end hung up (<c>G_IO_HUP</c>).</summary>
    Hup = 16,

    /// <summary>The descriptor is not open (<c>G_IO_NVAL</c>).</summary>
    Nval = 32,
}

/// <summary>
/// A <c>GPollFD</c>: one descriptor of a poll set, with the events it is
/// watched for and the events it reported.
/// </summary>
/// <remarks>
/// <para>
/// This is a projection and not the ABI. The C structure carries its
/// descriptor in a field whose width depends on the platform — a
/// <c>gint64</c> on 64 bit Windows, where the value is a <c>HANDLE</c>, and a
/// <c>gint</c> everywhere else, where it is a file descriptor
/// (<c>glib/gpoll.h:93-104</c>) — so the managed type holds an
/// <see cref="nint"/> that covers both, and the two raw mirrors behind it are
/// what a native call is actually given.
/// </para>
/// <para>
/// <b>This is not <see cref="Gst.PollFD"/>.</b> That one is the
/// <c>GstPollFD</c> of GStreamer, the descriptor of a <see cref="Gst.Poll"/>
/// set, and the two are unrelated structures. Code that has both <c>Gst</c>
/// and <c>Gst.GLib</c> in scope has to qualify the name.
/// </para>
/// <para>
/// The descriptor belongs to whoever handed it out — a <see cref="Gst.Bus"/>
/// or a <see cref="Gst.Poll"/> — and stays valid only while that object does.
/// It must never be read from, written to or closed; waiting on it is the
/// whole of what it is for.
/// </para>
/// </remarks>
public partial struct PollFD
{
    /// <summary>
    /// The number of bytes a native <c>GPollFD</c> needs on the widest
    /// platform, which is what a caller allocated out parameter reserves.
    /// </summary>
    internal const int RawSize = 16;

    /// <summary>
    /// How many descriptors <see cref="Poll"/> converts on the stack before it
    /// reaches for the heap. Windows refuses more than
    /// <c>MAXIMUM_WAIT_OBJECTS</c> of them anyway.
    /// </summary>
    private const int StackLimit = 64;

    /// <summary>
    /// The descriptor to wait on: a file descriptor on Unix, a <c>HANDLE</c>
    /// on Windows.
    /// </summary>
    public nint Fd;

    /// <summary>The events the descriptor is watched for.</summary>
    public IOCondition Events;

    /// <summary>
    /// The events that happened, which <see cref="Poll"/> overwrites on every
    /// call.
    /// </summary>
    public IOCondition Revents;

    /// <summary>
    /// Gets a value indicating whether the native structure carries its
    /// descriptor in eight bytes rather than four.
    /// </summary>
    /// <remarks>
    /// The condition is the one of the C header: <c>G_OS_WIN32</c> together
    /// with a pointer size of eight.
    /// </remarks>
    internal static bool IsWide => OperatingSystem.IsWindows() && nint.Size == 8;

    /// <summary>
    /// Waits until one of the descriptors reports an event or the timeout
    /// expires.
    /// </summary>
    /// <param name="fds">
    /// The descriptors to wait on. The <see cref="Revents"/> of each of them
    /// is overwritten with what it reported.
    /// </param>
    /// <param name="timeoutMs">
    /// How long to wait, in milliseconds; <c>0</c> to ask without waiting and
    /// <c>-1</c> to wait for as long as it takes.
    /// </param>
    /// <returns>
    /// The number of descriptors that reported something, which is <c>0</c>
    /// when the timeout expired first.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>g_poll</c>. On Unix it is <c>poll()</c> and the bits of
    /// <see cref="Revents"/> are the ones the kernel reported. On Windows it
    /// is a <c>WaitForMultipleObjectsEx</c> over the descriptors read as
    /// <c>HANDLE</c>s — never as socket descriptors — which means three
    /// things: a signalled handle reports a <see cref="Revents"/> equal to its
    /// <see cref="Events"/>, because the wait cannot tell the conditions
    /// apart; more than <c>MAXIMUM_WAIT_OBJECTS</c> (64) handles produce a
    /// GLib warning and are not all waited on; and a descriptor of zero or
    /// less is ignored.
    /// </para>
    /// <para>
    /// On Unix a wait that a signal interrupted is not a failure: the call
    /// reports <c>EINTR</c>, and this member waits again with what is left of
    /// the timeout, which is what the main loop of GLib does with the same
    /// answer (gmain.c:4824-4825). A .NET process installs signal handlers of
    /// its own, so an infinite wait on a descriptor would otherwise fail for a
    /// reason that has nothing to do with the caller.
    /// </para>
    /// <para>
    /// An empty set is refused rather than passed on: on Windows it is a
    /// failed wait and on Unix a wait with no timeout over nothing never
    /// returns.
    /// </para>
    /// <para>
    /// A caller that only wants the one descriptor of a <see cref="Gst.Bus"/>
    /// may equally wait on it with whatever its platform offers: the bus hands
    /// out a manual reset event on Windows and the read end of a control
    /// socket on Unix.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="fds"/> is empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">The wait itself failed.</exception>
    public static unsafe int Poll(Span<PollFD> fds, int timeoutMs)
    {
        if (fds.IsEmpty)
        {
            throw new ArgumentException(
                "A poll needs at least one descriptor to wait on.",
                nameof(fds));
        }

        int result;

        if (IsWide)
        {
            Span<PollFDRaw64> raw = fds.Length <= StackLimit
                ? stackalloc PollFDRaw64[StackLimit]
                : new PollFDRaw64[fds.Length];
            raw = raw[..fds.Length];

            for (int index = 0; index < fds.Length; index++)
            {
                raw[index] = new PollFDRaw64
                {
                    Fd = fds[index].Fd,
                    Events = (ushort)fds[index].Events,
                    Revents = 0,
                };
            }

            fixed (PollFDRaw64* first = raw)
            {
                result = Wait((nint)first, (uint)fds.Length, timeoutMs);
            }

            for (int index = 0; index < fds.Length; index++)
            {
                fds[index].Revents = (IOCondition)raw[index].Revents;
            }
        }
        else
        {
            Span<PollFDRaw32> raw = fds.Length <= StackLimit
                ? stackalloc PollFDRaw32[StackLimit]
                : new PollFDRaw32[fds.Length];
            raw = raw[..fds.Length];

            for (int index = 0; index < fds.Length; index++)
            {
                raw[index] = new PollFDRaw32
                {
                    Fd = (int)fds[index].Fd,
                    Events = (ushort)fds[index].Events,
                    Revents = 0,
                };
            }

            fixed (PollFDRaw32* first = raw)
            {
                result = Wait((nint)first, (uint)fds.Length, timeoutMs);
            }

            for (int index = 0; index < fds.Length; index++)
            {
                fds[index].Revents = (IOCondition)raw[index].Revents;
            }
        }

        return result;
    }

    /// <summary>
    /// Runs <c>g_poll</c> and waits again over what is left of the timeout
    /// when a signal interrupted the wait.
    /// </summary>
    /// <param name="fds">The array of native descriptors.</param>
    /// <param name="count">How many of them there are.</param>
    /// <param name="timeoutMs">The timeout of the whole wait.</param>
    /// <returns>The number of descriptors that reported something.</returns>
    /// <remarks>
    /// On Unix <c>g_poll</c> is <c>poll()</c> (gpoll.c:121-126), which answers
    /// <c>-1</c> with <c>EINTR</c> when a signal arrived while it waited. That
    /// is not a failure of the wait, and the main loop of GLib treats it as
    /// none (gmain.c:4824-4825). Windows has no such answer: an interrupted
    /// wait is <c>WAIT_IO_COMPLETION</c> there, which <c>g_poll</c> already
    /// reports as zero descriptors (gpoll.c:201-203).
    /// </remarks>
    /// <exception cref="InvalidOperationException">The wait itself failed.</exception>
    private static int Wait(nint fds, uint count, int timeoutMs)
    {
        // errno EINTR, which is 4 on Linux and on macOS alike.
        const int Interrupted = 4;

        long startedAt = Stopwatch.GetTimestamp();
        int remainingMs = timeoutMs;

        while (true)
        {
            int result = GPoll(fds, count, remainingMs);
            if (result >= 0)
            {
                return result;
            }

            int error = Marshal.GetLastPInvokeError();
            if (OperatingSystem.IsWindows() || error != Interrupted)
            {
                throw new InvalidOperationException(
                    FormattableString.Invariant($"g_poll failed with error {error}."));
            }

            if (timeoutMs > 0)
            {
                // A finite timeout is the timeout of the whole wait, not of
                // every attempt: what is left of it is what the next one gets.
                long elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                remainingMs = elapsedMs >= timeoutMs ? 0 : timeoutMs - (int)elapsedMs;
            }
        }
    }

    /// <summary>
    /// Prepares a block of <see cref="RawSize"/> bytes for a call that fills a
    /// <c>GPollFD</c>: every byte is cleared and the descriptor is set to
    /// <c>-1</c>, which no platform hands out, so that a call that wrote
    /// nothing can be told from one that did.
    /// </summary>
    /// <param name="raw">The block to prepare.</param>
    internal static unsafe void PrepareOut(byte* raw)
    {
        new Span<byte>(raw, RawSize).Clear();

        // Written as the widest of the two mirrors, so that the sentinel reads
        // as -1 through either of them.
        *(long*)raw = -1;
    }

    /// <summary>
    /// Reads a native <c>GPollFD</c> out of a block of <see cref="RawSize"/>
    /// bytes.
    /// </summary>
    /// <param name="raw">The block the call filled.</param>
    /// <returns>The projection of what it holds.</returns>
    internal static unsafe PollFD FromNative(byte* raw) =>
        IsWide
            ? new PollFD
            {
                Fd = (nint)((PollFDRaw64*)raw)->Fd,
                Events = (IOCondition)((PollFDRaw64*)raw)->Events,
                Revents = (IOCondition)((PollFDRaw64*)raw)->Revents,
            }
            : new PollFD
            {
                Fd = ((PollFDRaw32*)raw)->Fd,
                Events = (IOCondition)((PollFDRaw32*)raw)->Events,
                Revents = (IOCondition)((PollFDRaw32*)raw)->Revents,
            };

    /// <summary>The <c>g_poll</c> entry point.</summary>
    /// <param name="fds">The array of native descriptors.</param>
    /// <param name="count">How many of them there are.</param>
    /// <param name="timeout">The timeout in milliseconds, or <c>-1</c>.</param>
    /// <returns>The number of descriptors that reported something, or <c>-1</c>.</returns>
    [LibraryImport("GLib", EntryPoint = "g_poll", SetLastError = true)]
    private static partial int GPoll(nint fds, uint count, int timeout);
}

/// <summary>
/// The <c>GPollFD</c> of a platform whose descriptor is eight bytes wide,
/// which is 64 bit Windows and nothing else (<c>glib/gpoll.h:93-104</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PollFDRaw64
{
    /// <summary>The <c>HANDLE</c>, as the <c>gint64</c> the header declares.</summary>
    internal long Fd;

    /// <summary>The <c>gushort</c> events field.</summary>
    internal ushort Events;

    /// <summary>The <c>gushort</c> revents field.</summary>
    internal ushort Revents;
}

/// <summary>
/// The <c>GPollFD</c> of every other platform, whose descriptor is the
/// <c>gint</c> file descriptor (<c>glib/gpoll.h:93-104</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PollFDRaw32
{
    /// <summary>The file descriptor, as the <c>gint</c> the header declares.</summary>
    internal int Fd;

    /// <summary>The <c>gushort</c> events field.</summary>
    internal ushort Events;

    /// <summary>The <c>gushort</c> revents field.</summary>
    internal ushort Revents;
}
