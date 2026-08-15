using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The managed wrapper of a <c>GObject</c>.
/// </summary>
/// <remarks>
/// <para>
/// The lifetime of the pair is kept in sync with a toggle reference. As long as
/// native code holds a reference besides ours, the wrapper is held strongly, so
/// managed state that hangs off it survives a round trip through native code.
/// Once the toggle reference is the only one left, the wrapper is held weakly
/// and the garbage collector may collect it, which then releases the native
/// object.
/// </para>
/// <para>
/// Finalizers therefore never touch the toggle reference themselves: they
/// enqueue the release, and <see cref="DrainPendingReleases"/> performs it on a
/// thread that is allowed to run native code. The queue is drained whenever a
/// wrapper is looked up, from an idle callback of a running main loop (see
/// <see cref="EnableIdleDrain"/>), and whenever the application asks for it.
/// </para>
/// </remarks>
public class Object : IDisposable
{
    private static readonly ConcurrentDictionary<nint, ToggleRef> Wrappers = new();
    private static readonly ConcurrentQueue<PendingRelease> PendingReleases = new();
    private static readonly object Sync = new();

    private static int _idleScheduled;
    private static bool _idleDrainEnabled;

    private readonly nint _handle;
    private ToggleRef? _toggleRef;
    private List<ulong>? _handlers;
    private bool _disposed;

    /// <summary>
    /// Wraps a native <c>GObject</c> and takes part in its lifetime.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands a reference over,
    /// <see cref="Transfer.None"/> when the wrapper has to take its own. A
    /// floating reference is sunk in both cases, because the wrapper always
    /// ends up owning a real reference.
    /// </param>
    protected unsafe Object(nint handle, Transfer transfer)
    {
        if (handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "An object handle must not be null.");
        }

        _handle = handle;

        lock (Sync)
        {
            if (IsFloating(handle))
            {
                // Turns the floating reference into one that we own, whether it
                // came with the call or not.
                GObjectNative.ObjectRefSink(handle);
            }
            else if (transfer != Transfer.Full)
            {
                GObjectNative.ObjectRef(handle);
            }

            ToggleRef toggleRef = new(this);
            _toggleRef = toggleRef;
            Wrappers[handle] = toggleRef;

            // The toggle reference takes the reference we own, and reports back
            // whenever native code is the only owner left.
            GObjectNative.ObjectAddToggleRef(handle, &ToggleNotify, toggleRef.UserData);
            GObjectNative.ObjectUnref(handle);
        }
    }

    /// <summary>
    /// Releases the native object if the wrapper was not disposed.
    /// </summary>
    ~Object() => Dispose(disposing: false);

    /// <summary>
    /// Gets the native <c>GObject</c>.
    /// </summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>
    /// Gets the type of the wrapped instance.
    /// </summary>
    public GType NativeType => TypeRegistry.GetInstanceType(_handle);

    /// <summary>
    /// Returns the wrapper of a native object, creating it when it does not
    /// exist yet.
    /// </summary>
    /// <param name="handle">The object to wrap, may be <see cref="nint.Zero"/>.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The wrapper, or <see langword="null"/> for a null handle.</returns>
    public static Object? FromNative(nint handle, Transfer transfer)
    {
        if (handle == nint.Zero)
        {
            return null;
        }

        DrainPendingReleases();

        lock (Sync)
        {
            if (Wrappers.TryGetValue(handle, out ToggleRef? known) && known.TryGetTarget(out Object? existing))
            {
                if (transfer == Transfer.Full)
                {
                    // The wrapper already owns the object through its toggle
                    // reference, so the one that came with the call is dropped.
                    if (IsFloating(handle))
                    {
                        GObjectNative.ObjectRefSink(handle);
                    }

                    GObjectNative.ObjectUnref(handle);
                }

                return existing;
            }

            if (TypeRegistry.TryCreateWrapper(handle, transfer, out object? created) && created is Object wrapper)
            {
                return wrapper;
            }

            return new Object(handle, transfer);
        }
    }

    /// <summary>
    /// Returns the wrapper of a native object, typed.
    /// </summary>
    /// <typeparam name="T">The expected wrapper type.</typeparam>
    /// <param name="handle">The object to wrap, may be <see cref="nint.Zero"/>.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>
    /// The wrapper, or <see langword="null"/> for a null handle or a wrapper of
    /// a different type.
    /// </returns>
    public static T? FromNative<T>(nint handle, Transfer transfer)
        where T : Object => FromNative(handle, transfer) as T;

    /// <summary>
    /// Releases the native objects whose wrappers have been collected.
    /// </summary>
    /// <remarks>
    /// Applications rarely need to call this: it runs whenever a wrapper is
    /// looked up and from the idle callback of a running main loop.
    /// </remarks>
    public static void DrainPendingReleases()
    {
        while (PendingReleases.TryDequeue(out PendingRelease pending))
        {
            try
            {
                ToggleRef? toggleRef = CallbackHandle.GetState<ToggleRef>(pending.UserData);
                if (toggleRef is not null)
                {
                    Release(pending.Handle, toggleRef);
                }
            }
            catch (Exception exception)
            {
                ExceptionTrap.Report(exception);
            }
        }
    }

    /// <summary>
    /// Lets the runtime drain the pending releases from an idle callback of the
    /// default main context. <see cref="Gst.GLib.MainLoop.Run"/> does this on
    /// its own.
    /// </summary>
    public static void EnableIdleDrain()
    {
        Volatile.Write(ref _idleDrainEnabled, true);
        ScheduleIdleDrain();
    }

    /// <summary>
    /// Connects a signal handler and keeps track of it, so that
    /// <see cref="Dispose()"/> disconnects it again.
    /// </summary>
    /// <param name="detailedSignal">The signal name, optionally with a detail.</param>
    /// <param name="callback">
    /// The unmanaged callback, normally the address of a static method that is
    /// marked with <see cref="UnmanagedCallersOnlyAttribute"/>.
    /// </param>
    /// <param name="state">The managed state of the callback.</param>
    /// <param name="after">
    /// <see langword="true"/> to run the handler after the default one.
    /// </param>
    /// <returns>The identifier of the handler.</returns>
    public ulong ConnectSignal(string detailedSignal, nint callback, CallbackHandle state, bool after = false)
    {
        ulong id = SignalRegistry.Connect(Handle, detailedSignal, callback, state, after);
        Track(id);
        return id;
    }

    /// <summary>
    /// Runs a handler whenever a property changes.
    /// </summary>
    /// <param name="propertyName">
    /// The property to watch, or <see langword="null"/> for every property.
    /// </param>
    /// <param name="handler">
    /// The handler, which receives the object and the parameter specification
    /// of the property that changed.
    /// </param>
    /// <returns>The identifier of the handler.</returns>
    public unsafe ulong AddNotifyHandler(string? propertyName, Action<Object, ParamSpec> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        string signal = propertyName is null ? "notify" : $"notify::{propertyName}";
        CallbackHandle state = CallbackHandle.Alloc(handler);

        try
        {
            return ConnectSignal(signal, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&NotifyTrampoline, state);
        }
        catch
        {
            state.Free();
            throw;
        }
    }

    /// <summary>
    /// Disconnects a handler that was connected through this wrapper.
    /// </summary>
    /// <param name="handlerId">The identifier of the handler.</param>
    public void RemoveHandler(ulong handlerId)
    {
        if (handlerId == 0)
        {
            return;
        }

        lock (Sync)
        {
            _handlers?.Remove(handlerId);
        }

        if (!_disposed)
        {
            SignalRegistry.Disconnect(_handle, handlerId);
        }
    }

    /// <summary>
    /// Reads a property.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <returns>The value of the property, which the caller has to dispose.</returns>
    /// <exception cref="ArgumentException">The object has no such property.</exception>
    public unsafe Value GetProperty(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        nint handle = Handle;
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);

        nint pspec = GObjectNative.ObjectClassFindProperty(*(nint*)handle, scope.Pointer);
        if (pspec == nint.Zero)
        {
            throw new ArgumentException($"\"{name}\" is not a property of {NativeType.Name}.", nameof(name));
        }

        Value value = Value.New(ParamSpec.ValueTypeOf(pspec));
        GObjectNative.ObjectGetProperty(handle, scope.Pointer, ref value.NativeValue);
        return value;
    }

    /// <summary>
    /// Writes a property.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The new value.</param>
    public unsafe void SetProperty(string name, in Value value)
    {
        ArgumentNullException.ThrowIfNull(name);

        nint handle = Handle;
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);
        GObjectNative.ObjectSetProperty(handle, scope.Pointer, ref Unsafe.AsRef(in value).NativeValue);
    }

    /// <summary>
    /// Disconnects the tracked signal handlers and releases the native object.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the native object.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when the call comes from <see cref="Dispose()"/>,
    /// <see langword="false"/> when it comes from the finalizer, in which case
    /// the release is queued instead of performed.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ToggleRef? toggleRef = _toggleRef;
        _toggleRef = null;

        if (toggleRef is null || _handle == nint.Zero)
        {
            return;
        }

        if (disposing)
        {
            DisconnectAll();
            Release(_handle, toggleRef);
            return;
        }

        // A finalizer must not remove the toggle reference itself: the notify
        // can run concurrently, and native code must not be called from the
        // finalizer thread while another thread holds the object.
        PendingReleases.Enqueue(new PendingRelease(_handle, toggleRef.UserData));
        ScheduleIdleDrain();
    }

    private static bool IsFloating(nint handle) =>
        InitiallyUnowned.IsInitiallyUnownedType(TypeRegistry.GetInstanceType(handle)) &&
        GObjectNative.ObjectIsFloating(handle) != 0;

    private static unsafe void Release(nint handle, ToggleRef toggleRef)
    {
        lock (Sync)
        {
            if (!toggleRef.MarkReleased())
            {
                return;
            }

            Wrappers.TryRemove(new KeyValuePair<nint, ToggleRef>(handle, toggleRef));

            nint userData = toggleRef.UserData;
            GObjectNative.ObjectRemoveToggleRef(handle, &ToggleNotify, userData);
            toggleRef.Free();
        }
    }

    private static unsafe void ScheduleIdleDrain()
    {
        if (!Volatile.Read(ref _idleDrainEnabled) || PendingReleases.IsEmpty)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _idleScheduled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            GLibNative.IdleAddFull(GLibNative.PriorityDefaultIdle, &DrainIdle, nint.Zero, null);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _idleScheduled, 0);
            ExceptionTrap.Report(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int DrainIdle(nint userData)
    {
        _ = userData;

        try
        {
            Volatile.Write(ref _idleScheduled, 0);
            DrainPendingReleases();

            if (!PendingReleases.IsEmpty && Interlocked.CompareExchange(ref _idleScheduled, 1, 0) == 0)
            {
                // G_SOURCE_CONTINUE: keep this source instead of adding a new one.
                return 1;
            }
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }

        // G_SOURCE_REMOVE.
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ToggleNotify(nint userData, nint instance, int isLastRef)
    {
        _ = instance;

        try
        {
            CallbackHandle.GetState<ToggleRef>(userData)?.SetStrong(isLastRef == 0);
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void NotifyTrampoline(nint instance, nint pspec, nint userData)
    {
        try
        {
            if (CallbackHandle.GetState<Action<Object, ParamSpec>>(userData) is not Action<Object, ParamSpec> handler)
            {
                return;
            }

            Object? sender = FromNative(instance, Transfer.None);
            if (sender is null)
            {
                return;
            }

            using ParamSpec property = new(pspec, Transfer.None);
            handler(sender, property);
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }

    private void Track(ulong handlerId)
    {
        lock (Sync)
        {
            (_handlers ??= []).Add(handlerId);
        }
    }

    private void DisconnectAll()
    {
        List<ulong>? handlers;

        lock (Sync)
        {
            handlers = _handlers;
            _handlers = null;
        }

        if (handlers is null)
        {
            return;
        }

        foreach (ulong handler in handlers)
        {
            SignalRegistry.Disconnect(_handle, handler);
        }
    }

    private readonly record struct PendingRelease(nint Handle, nint UserData);

    /// <summary>
    /// The state behind the toggle reference of one wrapper.
    /// </summary>
    /// <remarks>
    /// The <see cref="GCHandle"/> of this object is the <c>data</c> pointer of
    /// the toggle notification, so it has to stay valid and unchanged for as
    /// long as the toggle reference exists. Whether the wrapper is held
    /// strongly or weakly is therefore switched inside this object rather than
    /// by swapping the handle itself.
    /// </remarks>
    private sealed class ToggleRef
    {
        private readonly object _sync = new();
        private readonly WeakReference<Object> _weak;
        private GCHandle _self;
        private Object? _strong;
        private bool _released;

        internal ToggleRef(Object owner)
        {
            _weak = new WeakReference<Object>(owner);

            // Starts strong: the toggle notification demotes it as soon as the
            // toggle reference is the only one left.
            _strong = owner;
            _self = GCHandle.Alloc(this);
        }

        internal nint UserData => GCHandle.ToIntPtr(_self);

        internal bool TryGetTarget(out Object target)
        {
            if (_weak.TryGetTarget(out Object? value))
            {
                target = value;
                return true;
            }

            target = null!;
            return false;
        }

        internal void SetStrong(bool strong)
        {
            lock (_sync)
            {
                if (_released)
                {
                    return;
                }

                _strong = strong && _weak.TryGetTarget(out Object? owner) ? owner : null;
            }
        }

        internal bool MarkReleased()
        {
            lock (_sync)
            {
                if (_released)
                {
                    return false;
                }

                _released = true;
                _strong = null;
                return true;
            }
        }

        internal void Free()
        {
            if (_self.IsAllocated)
            {
                _self.Free();
            }
        }
    }
}
