using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// A <see cref="SynchronizationContext"/> that dispatches onto a GLib main
/// context, so that <c>await</c> continuations run on the thread of the main
/// loop.
/// </summary>
/// <remarks>
/// The work items travel as a static, unmanaged callback plus a
/// <see cref="GCHandle"/>, so no delegate is ever marshalled and nothing has to
/// be discovered by reflection. <see cref="Post"/> attaches an idle source to
/// the context, <see cref="Send"/> uses <c>g_main_context_invoke_full</c> and
/// waits.
/// </remarks>
public sealed unsafe class GLibSynchronizationContext : SynchronizationContext
{
    private readonly nint _context;

    /// <summary>
    /// Creates a synchronization context for the default main context.
    /// </summary>
    public GLibSynchronizationContext()
        : this(MainContext.Default)
    {
    }

    /// <summary>
    /// Creates a synchronization context for a main context.
    /// </summary>
    /// <param name="context">The main context to dispatch to.</param>
    public GLibSynchronizationContext(MainContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context.Handle;
    }

    private GLibSynchronizationContext(nint context) => _context = context;

    /// <inheritdoc/>
    /// <remarks>
    /// The work item is always queued, never run inline: it becomes an idle
    /// source of the target context. <c>g_main_context_invoke_full</c> would
    /// run it on the calling thread whenever that thread can acquire the
    /// context, which breaks the contract of
    /// <see cref="SynchronizationContext.Post"/>.
    /// </remarks>
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        CallbackHandle handle = CallbackHandle.Alloc(new WorkItem(d, state, completion: null));
        nint source = GLibNative.IdleSourceNew();

        try
        {
            GLibNative.SourceSetPriority(source, GLibNative.PriorityDefault);
            GLibNative.SourceSetCallback(source, &Dispatch, handle.UserData, CallbackHandle.DestroyNotify);
            GLibNative.SourceAttach(source, _context);
        }
        catch
        {
            handle.Free();
            throw;
        }
        finally
        {
            GLibNative.SourceUnref(source);
        }
    }

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        if (GLibNative.MainContextIsOwner(_context) != 0)
        {
            // Already on the thread that runs this context.
            d(state);
            return;
        }

        using ManualResetEventSlim completion = new(initialState: false);
        WorkItem item = new(d, state, completion);
        CallbackHandle handle = CallbackHandle.Alloc(item);

        try
        {
            GLibNative.MainContextInvokeFull(
                _context,
                GLibNative.PriorityDefault,
                &Dispatch,
                handle.UserData,
                CallbackHandle.DestroyNotify);
        }
        catch
        {
            handle.Free();
            throw;
        }

        completion.Wait();
        item.Error?.Throw();
    }

    /// <inheritdoc/>
    public override SynchronizationContext CreateCopy() => new GLibSynchronizationContext(_context);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Dispatch(nint userData)
    {
        try
        {
            CallbackHandle.GetState<WorkItem>(userData)?.Run();
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }

        // G_SOURCE_REMOVE: the item runs exactly once.
        return 0;
    }

    private sealed class WorkItem
    {
        private readonly SendOrPostCallback _callback;
        private readonly object? _state;
        private readonly ManualResetEventSlim? _completion;

        internal WorkItem(SendOrPostCallback callback, object? state, ManualResetEventSlim? completion)
        {
            _callback = callback;
            _state = state;
            _completion = completion;
        }

        internal ExceptionDispatchInfo? Error { get; private set; }

        internal void Run()
        {
            try
            {
                _callback(_state);
            }
            catch (Exception exception)
            {
                if (_completion is null)
                {
                    ExceptionTrap.Report(exception);
                }
                else
                {
                    Error = ExceptionDispatchInfo.Capture(exception);
                }
            }
            finally
            {
                _completion?.Set();
            }
        }
    }
}
