using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GLib;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// The two static callbacks the asynchronous Gio surface of the binding needs:
/// the one that starts an operation on the dispatcher thread, and the one
/// <c>GAsyncReadyCallback</c> that completes it.
/// </summary>
/// <remarks>
/// <para>
/// Every asynchronous Gio operation hands native code these
/// <see cref="UnmanagedCallersOnlyAttribute"/> function pointers plus a
/// <see cref="CallbackHandle"/> to a <see cref="GioAsyncState"/>. The per
/// operation typing lives in the state object rather than in the callbacks, so
/// the C to managed edge is two static function pointers for the whole
/// surface: no delegate is marshalled, nothing is discovered by reflection,
/// and the dispatch to the right <c>_async</c> and <c>_finish</c> entry points
/// is an ordinary virtual call on a managed object.
/// </para>
/// <para>
/// The gir guarantees that the callback runs exactly once and that
/// <c>_finish</c> may be called at most once. <see cref="Bridge"/> is the only
/// caller of either, so both guarantees are structural rather than a rule
/// somebody has to keep.
/// </para>
/// </remarks>
internal static unsafe class GioAsync
{
    /// <summary>
    /// Gets the <c>GAsyncReadyCallback</c> that completes a
    /// <see cref="GioAsyncState"/>.
    /// </summary>
    internal static delegate* unmanaged[Cdecl]<nint, nint, nint, void> Bridge => &AsyncReadyBridge;

    /// <summary>
    /// Gets the <c>GSourceFunc</c> that makes the native <c>_async</c> call of
    /// a <see cref="GioAsyncState"/> on the dispatcher thread.
    /// </summary>
    internal static delegate* unmanaged[Cdecl]<nint, int> Starter => &StartTrampoline;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StartTrampoline(nint userData)
    {
        try
        {
            CallbackHandle.GetState<GioAsyncState>(userData)?.StartHere(userData);
        }
        catch (Exception exception)
        {
            // StartHere traps everything it can itself; this is what keeps a
            // managed exception from unwinding a native frame regardless.
            ExceptionTrap.Report(exception);
        }

        // G_SOURCE_REMOVE: an operation is started exactly once. The invoke
        // path calls the function in a loop while it answers true, so this is
        // not merely tidy.
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void AsyncReadyBridge(nint sourceObject, nint result, nint userData)
    {
        CallbackHandle handle = CallbackHandle.FromUserData(userData);

        try
        {
            CallbackHandle.GetState<GioAsyncState>(userData)?.Complete(sourceObject, result);
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
        finally
        {
            // CallbackScope.Async: the state is released by the one invocation
            // the operation makes. The path where no invocation ever happens is
            // GioAsyncState.StartHere's, see the remarks there.
            handle.Free();
        }
    }
}

/// <summary>
/// The managed state of one asynchronous Gio operation, as far as the shared
/// callbacks need to know it.
/// </summary>
/// <remarks>
/// The base is not generic so that <see cref="GioAsync"/> needs no generics;
/// the result type appears one layer up, in <see cref="GioAsyncState{T}"/>.
/// </remarks>
internal abstract class GioAsyncState
{
    /// <summary>
    /// The error domain Gio reports cancellation in (<c>G_IO_ERROR</c>).
    /// Interning it once here rather than in the generic class keeps it to one
    /// quark for the whole surface.
    /// </summary>
    private protected static readonly Quark IoErrorDomain = Quark.FromString("g-io-error-quark");

    /// <summary>The <c>G_IO_ERROR_CANCELLED</c> code of that domain.</summary>
    private protected const int IoErrorCancelled = 19;

    private readonly Gst.GObject.Object? _owner;
    private readonly CancellationToken _token;

    private Cancellable? _cancellable;
    private CancellationTokenRegistration _registration;
    private nint _borrowed;

    /// <summary>
    /// Initialises the state of an operation.
    /// </summary>
    /// <param name="owner">
    /// The wrapper the operation was started on, which the state keeps
    /// reachable until the operation completes, or <see langword="null"/> when
    /// the entry point is a C function rather than a method and there is no
    /// wrapper to keep.
    /// </param>
    /// <param name="cancellationToken">The token the caller passed in.</param>
    protected GioAsyncState(Gst.GObject.Object? owner, CancellationToken cancellationToken)
    {
        _owner = owner;
        _token = cancellationToken;
    }

    /// <summary>
    /// Initialises the state of an operation that watches a
    /// <c>GCancellable</c> the caller owns.
    /// </summary>
    /// <param name="owner">
    /// The wrapper the operation was started on, or <see langword="null"/> when
    /// there is none.
    /// </param>
    /// <param name="cancellable">
    /// The token the caller wants the operation to watch. It is borrowed: the
    /// binding never cancels, resets or disposes it.
    /// </param>
    /// <remarks>
    /// The native reference is taken <em>here</em>, on the calling thread,
    /// rather than read out of the wrapper when the operation starts. The
    /// operation starts on another thread and completes on another thread
    /// again, while a caller writing <c>using var cancellable =
    /// Cancellable.New();</c> is writing ordinary code: reading
    /// <c>Handle</c> later could therefore find a disposed wrapper. A borrowed
    /// object that has to outlive the call it was passed to gets a reference of
    /// its own, which is the same rule the rest of the binding follows.
    /// </remarks>
    protected GioAsyncState(Gst.GObject.Object? owner, Cancellable cancellable)
    {
        ArgumentNullException.ThrowIfNull(cancellable);

        _owner = owner;
        _token = default;
        _borrowed = GObjectNative.ObjectRef(cancellable.Handle);
        GC.KeepAlive(cancellable);
    }

    /// <summary>
    /// Gets the token the caller passed in.
    /// </summary>
    private protected CancellationToken Token => _token;

    /// <summary>
    /// Hands the operation to the dispatcher, which starts it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cancellation bridge is built here, on the calling thread, so that
    /// it is in place before the operation can start. A token that is already
    /// cancelled then cancels the <c>GCancellable</c> before the operation
    /// starts, which Gio documents as harmless: the callback still runs, with
    /// <c>G_IO_ERROR_CANCELLED</c>.
    /// </para>
    /// <para>
    /// Everything after that happens on the dispatcher thread, so this method
    /// only fails when the hand over itself does. Nothing took the state over
    /// in that case and the caller releases it, which is the rule
    /// <c>Gst.GObject.Object.ConnectSignal</c> and
    /// <c>GLibSynchronizationContext.Post</c> already follow.
    /// </para>
    /// </remarks>
    internal unsafe void Post()
    {
        CallbackHandle handle = CallbackHandle.Alloc(this);
        bool posted = false;

        try
        {
            AttachCancellation();
            GioAsyncDispatcher.Post(GioAsync.Starter, handle.UserData);
            posted = true;
        }
        finally
        {
            if (!posted)
            {
                Cleanup();
                handle.Free();
            }
        }
    }

    /// <summary>
    /// Makes the native <c>_async</c> call, on the dispatcher thread.
    /// </summary>
    /// <param name="userData">
    /// The <c>user_data</c> pointer of this state, which the native call has to
    /// pass back to <see cref="GioAsync.Bridge"/>.
    /// </param>
    /// <remarks>
    /// When the call itself fails, the operation never started, so its callback
    /// will never run: the failure becomes the result of the task and this
    /// releases the handle the completion would otherwise have released.
    /// </remarks>
    internal void StartHere(nint userData)
    {
        try
        {
            Invoke(CancellableHandle, userData);
        }
        catch (Exception exception)
        {
            Abort(exception);
            CallbackHandle.FromUserData(userData).Free();
        }
    }

    /// <summary>
    /// Completes the operation from the bridge.
    /// </summary>
    /// <param name="sourceObject">
    /// The <c>GObject</c> the operation was started on, which some
    /// <c>_finish</c> entry points take and others do not.
    /// </param>
    /// <param name="result">The <c>GAsyncResult</c> to finish.</param>
    internal abstract void Complete(nint sourceObject, nint result);

    /// <summary>
    /// Fails the operation before it ever reached native code, and cleans up.
    /// </summary>
    /// <param name="exception">Why the operation could not be started.</param>
    internal abstract void Abort(Exception exception);

    /// <summary>
    /// Releases what the state allocated around the operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This runs exactly once per operation, on whichever of the three paths it
    /// took: the completion, the failed start, or the failed hand over to the
    /// dispatcher.
    /// </para>
    /// <para>
    /// The order matters. Disposing the registration first waits out a
    /// <c>Cancel</c> that is in flight on another thread, so the wrapper below
    /// is never released underneath a call that is using it. It cannot
    /// deadlock: <c>g_cancellable_cancel</c> returns without waiting for the
    /// operation to complete. The <c>GCancellable</c> is exclusively owned by
    /// the binding and was never handed out, which is what makes disposing a
    /// GObject wrapper here the right thing rather than a doctrine violation —
    /// the same reasoning as the consuming callback install of
    /// <c>Gst.App.AppSink.SetSimpleCallbacks</c>. A <c>GCancellable</c> the
    /// caller handed in is the opposite case and is only <em>unreferenced</em>
    /// here: the wrapper belongs to the caller, who may go on using it.
    /// </para>
    /// </remarks>
    internal virtual void Cleanup()
    {
        _registration.Dispose();
        _registration = default;

        Cancellable? cancellable = Interlocked.Exchange(ref _cancellable, null);
        cancellable?.Dispose();

        nint borrowed = Interlocked.Exchange(ref _borrowed, nint.Zero);
        if (borrowed != nint.Zero)
        {
            GObjectNative.ObjectUnref(borrowed);
        }

        // The wrapper had to stay reachable for the whole operation, not just
        // for the extent of the call that started it.
        GC.KeepAlive(_owner);
    }

    /// <summary>
    /// Makes the native <c>_async</c> call of this operation.
    /// </summary>
    /// <param name="cancellable">
    /// The <c>GCancellable</c> to hand to it, or <see cref="nint.Zero"/> when
    /// the caller's token can never be cancelled.
    /// </param>
    /// <param name="userData">
    /// The <c>user_data</c> pointer to hand to it, beside
    /// <see cref="GioAsync.Bridge"/>.
    /// </param>
    protected abstract void Invoke(nint cancellable, nint userData);

    /// <summary>
    /// Gets the <c>GCancellable</c> to hand to the native call: the one the
    /// caller passed in when there is one, the private one built for the
    /// caller's token when the token can be cancelled, and
    /// <see cref="nint.Zero"/> otherwise.
    /// </summary>
    private nint CancellableHandle =>
        _borrowed != nint.Zero ? _borrowed : _cancellable is null ? nint.Zero : _cancellable.Handle;

    /// <summary>
    /// Builds the private <c>GCancellable</c> the operation watches and hooks
    /// the caller's token up to it.
    /// </summary>
    /// <remarks>
    /// <c>g_cancellable_cancel</c> is documented thread safe, so the
    /// registration may fire on any thread, and cancelling an operation that
    /// has already completed is a documented no-op.
    /// </remarks>
    private void AttachCancellation()
    {
        if (!_token.CanBeCanceled)
        {
            return;
        }

        Cancellable cancellable = Cancellable.New();
        _cancellable = cancellable;
        _registration = _token.Register(static state => ((Cancellable)state!).Cancel(), cancellable);
    }
}

/// <summary>
/// The managed state of one asynchronous Gio operation that produces a
/// <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The result of the operation.</typeparam>
/// <remarks>
/// <para>
/// One sealed subclass per operation supplies <see cref="GioAsyncState.Invoke"/>
/// and <see cref="Finish"/>, which call that operation's typed <c>_async</c>
/// and <c>_finish</c> entry points. Everything else — the completion source,
/// the error mapping, the cancellation bridge and the cleanup — is shared.
/// </para>
/// <para>
/// The reachability chain is <c>GCHandle</c> → state → { completion source,
/// owner wrapper, <c>GCancellable</c>, registration, the arguments of the
/// call }, so the whole graph survives until the callback without any
/// cooperation from the caller. That is deliberately stronger than the
/// <c>GC.KeepAlive</c> the generator emits, which only covers the synchronous
/// extent of a call; the last use of an asynchronous call is its callback, not
/// its return.
/// </para>
/// </remarks>
internal abstract class GioAsyncState<T> : GioAsyncState
{
    private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initialises the state of an operation.
    /// </summary>
    /// <param name="owner">The wrapper to keep reachable, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token the caller passed in.</param>
    protected GioAsyncState(Gst.GObject.Object? owner, CancellationToken cancellationToken)
        : base(owner, cancellationToken)
    {
    }

    /// <summary>
    /// Initialises the state of an operation that watches a
    /// <c>GCancellable</c> the caller owns.
    /// </summary>
    /// <param name="owner">The wrapper to keep reachable, or <see langword="null"/>.</param>
    /// <param name="cancellable">The token the caller passed in, which is borrowed.</param>
    protected GioAsyncState(Gst.GObject.Object? owner, Cancellable cancellable)
        : base(owner, cancellable)
    {
    }

    /// <summary>
    /// Hands the operation to the dispatcher and returns the task the public
    /// method gives its caller.
    /// </summary>
    /// <returns>The task of the operation.</returns>
    /// <remarks>
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> is
    /// required rather than merely tidy: without it an <c>await</c>
    /// continuation runs inline inside the GLib dispatch frame on the
    /// dispatcher thread, where user code could block the loop, re-enter the
    /// binding, or iterate the context recursively.
    /// </remarks>
    internal Task<T> Start()
    {
        Post();
        return _tcs.Task;
    }

    /// <inheritdoc/>
    internal sealed override void Complete(nint sourceObject, nint result)
    {
        try
        {
            _tcs.TrySetResult(Finish(sourceObject, result));
        }
        catch (GException exception) when (exception.Domain == IoErrorDomain && exception.Code == IoErrorCancelled)
        {
            // Cancelled by our token is a cancellation the awaiter can match
            // against; cancelled by anything else is still a cancellation
            // rather than a fault.
            _tcs.TrySetCanceled(Token.IsCancellationRequested ? Token : default);
        }
        catch (Exception exception)
        {
            _tcs.TrySetException(exception);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <inheritdoc/>
    internal sealed override void Abort(Exception exception)
    {
        try
        {
            _tcs.TrySetException(exception);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>
    /// Calls the <c>_finish</c> entry point of this operation and marshals what
    /// it produced.
    /// </summary>
    /// <param name="sourceObject">
    /// The <c>GObject</c> the operation was started on. Entry points that take
    /// only the <c>GAsyncResult</c> ignore it.
    /// </param>
    /// <param name="result">The <c>GAsyncResult</c> to finish.</param>
    /// <returns>The result of the operation.</returns>
    /// <exception cref="GException">The operation failed or was cancelled.</exception>
    protected abstract T Finish(nint sourceObject, nint result);
}
