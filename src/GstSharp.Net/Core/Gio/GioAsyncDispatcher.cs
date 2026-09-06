using Gst.GLib;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// The main context every asynchronous Gio operation of the binding runs on,
/// and the thread that iterates it.
/// </summary>
/// <remarks>
/// <para>
/// A <c>GTask</c> based <c>_async</c> function captures the main context that
/// is thread default on the thread that starts it, and delivers its
/// <c>GAsyncReadyCallback</c> in a later iteration of that context — never
/// synchronously and never on an arbitrary thread. A .NET caller on a thread
/// pool thread has pushed no thread default context, so the operation captures
/// the default one, and a GStreamer application frequently iterates no main
/// context at all. The callback would then never be dispatched: the returned
/// <see cref="System.Threading.Tasks.Task"/> hangs for the lifetime of the
/// process and the state behind it leaks. That failure cannot be detected at
/// the call site and cannot be repaired from user code, so the binding owns a
/// context instead of documenting a requirement.
/// </para>
/// <para>
/// The contract is:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The dispatcher starts on the first asynchronous call and lives as long as
/// the process. Its thread is a background thread, so it never keeps the
/// process alive; ending the process is how it is torn down, the same stance
/// the binding takes on <c>ges_deinit</c>.
/// </description></item>
/// <item><description>
/// It owns a private <see cref="MainContext"/>, makes it thread default on its
/// own thread and iterates it forever. Making a context thread default
/// <em>acquires</em> it, so from then on the dispatcher thread owns the
/// context outright and no other thread can take it.
/// </description></item>
/// <item><description>
/// That ownership is why the native <c>_async</c> call is <em>started</em> on
/// the dispatcher thread as well, through <see cref="Post"/>, rather than
/// bracketed with a push and a pop on the calling thread: pushing a context
/// another thread owns fails, which is what GLib means by "normally you would
/// call this function shortly after creating a new thread". The operation
/// therefore captures the dispatcher context because it is genuinely running
/// under it.
/// </description></item>
/// <item><description>
/// Both halves of an operation — the <c>_async</c> call and the
/// <c>_finish</c> call in its callback — consequently run on this one thread,
/// which is also what the libraries that are least thread safe, the editing
/// services among them, prefer. Continuations of the returned task never run
/// here: the completion sources are built with
/// <see cref="System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously"/>,
/// so user code can neither stall the dispatcher nor re-enter the binding from
/// inside a GLib dispatch frame.
/// </description></item>
/// </list>
/// <para>
/// An application that runs a GLib main loop of its own can opt out by setting
/// <c>GstSharp.GioAsyncContext</c>, in which case operations are posted to that
/// context and none of the above is started. Iterating that context is then the
/// application's job; the push and pop of the thread default around the native
/// call, which <c>GioAsyncState.StartHere</c> does, is what makes the operation
/// capture it regardless of what the iterating thread pushed.
/// </para>
/// <para>
/// See <c>docs/gio-async.md</c> for the whole shape, of which this is one
/// half.
/// </para>
/// </remarks>
internal static unsafe class GioAsyncDispatcher
{
    private static readonly Lazy<MainContext> Dispatcher = new(Start, isThreadSafe: true);

    /// <summary>
    /// Gets the context asynchronous operations run on, starting the
    /// dispatcher when this is the first call.
    /// </summary>
    internal static MainContext Context => Dispatcher.Value;

    /// <summary>
    /// Gets the context the next operation is started on: the one the
    /// application chose through <c>GstSharp.GioAsyncContext</c>, or the
    /// binding's own.
    /// </summary>
    /// <remarks>
    /// The fallback is behind <c>??</c> deliberately: an application that
    /// chose a context of its own never evaluates <see cref="Context"/>, so
    /// the dispatcher thread is never started.
    /// </remarks>
    internal static MainContext Target => global::GstSharp.GioAsyncContext ?? Context;

    /// <summary>
    /// Runs <paramref name="function"/> on <paramref name="context"/>, with
    /// <paramref name="data"/> as its argument.
    /// </summary>
    /// <param name="context">The context to run it on.</param>
    /// <param name="function">The function to run. It runs exactly once.</param>
    /// <param name="data">What to pass to it.</param>
    /// <remarks>
    /// <c>g_main_context_invoke_full</c> queues an idle source into the context
    /// for every thread but the one that owns it, and calls the function
    /// directly on the one that does — so an operation started from inside a
    /// completion callback needs no round trip. No <c>GDestroyNotify</c> is
    /// passed: the state behind <paramref name="data"/> belongs to the
    /// operation, which releases it from its callback or from its own failure
    /// path, and a notify here would release it a second time.
    /// </remarks>
    internal static void Post(nint context, delegate* unmanaged[Cdecl]<nint, int> function, nint data) =>
        GLibNative.MainContextInvokeFull(context, GLibNative.PriorityDefault, function, data, null);

    private static MainContext Start()
    {
        MainContext context = MainContext.New();

        Thread thread = new(static state => Run((MainContext)state!))
        {
            IsBackground = true,
            Name = "GstSharp.Net Gio async dispatcher",
        };

        thread.Start(context);
        return context;
    }

    private static void Run(MainContext context)
    {
        // This both acquires the context for good and makes it what an
        // operation started here captures.
        context.PushThreadDefault();

        while (true)
        {
            try
            {
                context.Iteration(mayBlock: true);
            }
            catch (Exception exception)
            {
                // A dispatcher that died is exactly the silent hang this class
                // exists to close, so one bad dispatch must not end the loop.
                // The trampolines trap their own callbacks already; this covers
                // what GLib itself could throw at the boundary.
                ExceptionTrap.Report(exception);
            }
        }
    }
}
