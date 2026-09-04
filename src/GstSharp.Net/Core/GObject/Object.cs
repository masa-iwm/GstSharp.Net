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
/// wrapper is looked up, whenever a mini object is adopted, from an idle
/// callback of a running main loop (see <see cref="EnableIdleDrain"/>), and
/// whenever the application asks for it.
/// </para>
/// <para>
/// An application that runs its own main context, or none at all, should call
/// <c>GstSharp.DrainPendingReleases</c> periodically. The idle source of
/// <see cref="EnableIdleDrain"/> is attached to the default main context, so it
/// only runs when something iterates that context. Draining is cheap when the
/// queue is empty and it is the wrapper lookups that keep it short in practice,
/// so most applications never notice.
/// </para>
/// </remarks>
public partial class Object : IDisposable
{
    private static readonly ConcurrentDictionary<nint, ToggleRef> Wrappers = new();

    /// <summary>
    /// The toggle references that are installed, keyed by the identifier that
    /// native code carries as the <c>data</c> pointer of the toggle
    /// notification.
    /// </summary>
    /// <remarks>
    /// GObject documents that a toggle notification may still be in flight when
    /// <c>g_object_remove_toggle_ref</c> returns, and that its <c>data</c>
    /// pointer is dangling from then on. A <see cref="GCHandle"/> must
    /// therefore never be the <c>data</c> pointer of a toggle reference: the
    /// slot of a freed handle is reused by the next allocation, so a late
    /// notification would resolve to an unrelated object rather than to
    /// nothing. The pointer is an identifier from a counter instead, and this
    /// table turns it back into the <see cref="ToggleRef"/>. The entry is
    /// removed before the toggle reference is, so a late notification finds
    /// nothing and returns.
    /// </remarks>
    private static readonly ConcurrentDictionary<nint, ToggleRef> ToggleRefs = new();

    private static readonly ConcurrentQueue<PendingRelease> PendingReleases = new();

    /// <summary>
    /// The gates that make the fabrication of a wrapper for one native instance
    /// a single winner, keyed by that instance.
    /// </summary>
    /// <remarks>
    /// A streaming thread running a trampoline and an application thread
    /// running <see cref="FromNative(nint, Transfer)"/> can reach the same
    /// unwrapped instance of a managed subclass at once, and the second
    /// constructor would refuse to wrap an object that is wrapped already -
    /// inside a reverse P/Invoke, under NativeAOT, that ends the process. The
    /// gate makes "look the interned wrapper up, and build one when there is
    /// none" one step. It is per instance rather than global so that a factory
    /// of one object never waits for the factory of another, and it is taken
    /// <em>outside</em> <see cref="Sync"/> everywhere, which is the order the
    /// constructor takes them in.
    /// </remarks>
    private static readonly ConcurrentDictionary<nint, System.Threading.Lock> FabricationGates = new();

    /// <summary>
    /// The instance the constructor of this thread is adopting: one native code
    /// created, owns, and may still hold floating.
    /// </summary>
    /// <remarks>
    /// The constructor chain of a subclass cannot carry an extra argument, so
    /// the one bit that separates adoption from the ordinary wrapping travels
    /// on the thread, armed by
    /// <see cref="SubclassCtorArgs.HandleFor(nuint)"/> and consumed - and
    /// cleared, whatever happens next - by the constructor below. Nothing can
    /// run in between: the argument is evaluated in the base call of the
    /// constructor it arms.
    /// </remarks>
    [ThreadStatic]
    private static nint t_adopting;

    /// <summary>The quark of the disposed marker, resolved on first use.</summary>
    private static uint _disposedQuark;
    private static readonly object Sync = new();

    private static long _nextToggleId;
    private static int _idleScheduled;
    private static bool _idleDrainEnabled;

    private readonly nint _handle;
    private ToggleRef? _toggleRef;
    private List<ulong>? _handlers;
    private int _disposed;

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
    /// <remarks>
    /// <para>
    /// This is the constructor a wrapper class chains to, including one in a
    /// binding module outside this repository; see
    /// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md">docs/modules.md</see>.
    /// Reaching it means accepting two obligations, because the interning table
    /// this constructor writes to is what the whole GObject lifetime rests on.
    /// </para>
    /// <para>
    /// <b>Call it from a type-registry factory, never from application code.</b>
    /// <see cref="FromNative(nint, Transfer)"/> is the supported way to turn a
    /// handle into a wrapper: it hands out the interned wrapper when there is
    /// one and only builds a new one when there is not, which is precisely the
    /// decision a raw constructor call skips. A derived wrapper therefore
    /// exposes a <c>CreateWrapper</c> factory to
    /// <see cref="Gst.Interop.ModuleTypeEntry"/> and keeps its own constructor
    /// out of the public surface; a public factory of its own, if it has one,
    /// goes through <see cref="FromNative(nint, Transfer)"/> as well.
    /// </para>
    /// <para>
    /// <b>Pass the transfer the C function documented.</b> The wrapper ends up
    /// owning one reference either way — this takes one when
    /// <paramref name="transfer"/> is not <see cref="Transfer.Full"/> — so
    /// getting it wrong leaks the object or releases a reference that was never
    /// handed over.
    /// </para>
    /// <para>
    /// Wrapping a handle that a live wrapper already holds throws rather than
    /// producing a second wrapper. Two toggle references on one object suspend
    /// each other's toggling until one is removed, so the failure is loud here
    /// instead of being a lifetime bug much later.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="handle"/> is <see cref="nint.Zero"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A live wrapper of <paramref name="handle"/> exists already. GObject
    /// wrappers are interned, so there is exactly one of them per object;
    /// <see cref="FromNative(nint, Transfer)"/> hands out the existing one and
    /// is the only supported way to wrap a handle that came out of native code.
    /// </exception>
    protected unsafe Object(nint handle, Transfer transfer)
    {
        // Consumed and cleared before anything can throw, so that a factory
        // that failed halfway leaves no armed handle behind for the next
        // constructor on this thread to pick up.
        nint adopting = t_adopting;
        t_adopting = nint.Zero;
        bool adopted = adopting != nint.Zero && adopting == handle;

        if (handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "An object handle must not be null.");
        }

        _handle = handle;

        lock (Sync)
        {
            // Constructing a second wrapper for the same object would install a
            // second toggle reference on it, which suspends the toggling of
            // both until one of them is removed again, and would leave the
            // interning table pointing at whichever wrapper was built last. A
            // wrapper that is merely disposed, or whose release is still
            // queued, does not count: it is on its way out and the fresh
            // wrapper is exactly what FromNative asks for in that case.
            if (Wrappers.TryGetValue(handle, out ToggleRef? known) &&
                known.TryGetTarget(out Object? live) &&
                !live.IsDisposed)
            {
                throw new InvalidOperationException(
                    "A wrapper of this object exists already. Use Gst.GObject.Object.FromNative to get it: " +
                    "GObject wrappers are interned and a second one would install a second toggle reference.");
            }

            if (adopted)
            {
                // The instance belongs to whoever created it - a factory, a
                // base class building its pad - and may still be floating.
                // Sinking it here would take an ownership that the caller of
                // gst_element_factory_create_with_properties is about to
                // complain about, so the wrapper only takes a reference of its
                // own; the reference below is what the toggle reference ends
                // up holding. See docs/subclassing.md §5.4.
                GObjectNative.ObjectRef(handle);
            }
            else if (IsFloating(handle))
            {
                // Turns the floating reference into one that we own, whether it
                // came with the call or not.
                GObjectNative.ObjectRefSink(handle);
            }
            else if (transfer != Transfer.Full)
            {
                GObjectNative.ObjectRef(handle);
            }

            // The identifier has to resolve before the toggle reference is
            // installed: the unref below drops the object back to the single
            // reference the toggle reference holds, which notifies straight
            // away.
            ToggleRef toggleRef = new(this, NextToggleId());
            while (!ToggleRefs.TryAdd(toggleRef.UserData, toggleRef))
            {
                // The counter wrapped around, which needs four billion live
                // wrappers on a 32 bit process to happen at all, and landed on
                // an identifier that is still in use. Take the next one.
                toggleRef = new ToggleRef(this, NextToggleId());
            }

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
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _handle;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this wrapper has given up its part in
    /// the lifetime of the object.
    /// </summary>
    /// <remarks>
    /// The flag is set by <see cref="Dispose()"/> and by the finalizer, and the
    /// release of the native object follows it: a wrapper can be disposed while
    /// its release is still queued. <see cref="FromNative(nint, Transfer)"/>
    /// therefore never hands a disposed wrapper out, it builds a fresh one.
    /// </remarks>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// Gets the type of the wrapped instance.
    /// </summary>
    public GType NativeType => TypeRegistry.GetInstanceType(_handle);

    /// <summary>
    /// Casts to a GObject interface that the native instance implements but
    /// the class of this wrapper does not declare, such as the <c>volume</c>
    /// element and <c>Gst.Audio.IStreamVolume</c>.
    /// </summary>
    /// <typeparam name="T">
    /// The interface to cast to. A class, or any other type that is not an
    /// interface, is answered with <c>this as T</c>.
    /// </typeparam>
    /// <returns>
    /// A view of this object as <typeparamref name="T"/>, or
    /// <see langword="null"/> when the instance does not implement it. The
    /// view keeps this wrapper alive and shares its handle, so disposing this
    /// wrapper makes the view throw.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is a GObject interface of a module that has
    /// not been initialised.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public T? As<T>()
        where T : class
    {
        // A wrapper whose class declares the interface answers on its own, so
        // bin.As<IChildProxy>() is the bin and not an adapter around it.
        if (this is T self)
        {
            return self;
        }

        nint handle = Handle;
        if (!TypeRegistry.TryGetInterface(typeof(T), out ModuleInterfaceEntry entry))
        {
            // Only an interface can be registered, so an unregistered one means
            // the module of T was never initialised; anything else is simply a
            // type this object is not.
            if (typeof(T).IsInterface)
            {
                throw new InvalidOperationException(
                    $"{typeof(T)} is not a GObject interface of an initialised module. " +
                    "Call the module's Initialize method first.");
            }

            return null;
        }

        bool implements =
            GObjectNative.TypeIsA(TypeRegistry.GetInstanceType(handle).Value, entry.GetNativeType()) != 0;
        GC.KeepAlive(this);
        return implements ? (T)Adapt(entry, this) : null;
    }

    /// <summary>Calls the adapter factory of an interface entry.</summary>
    /// <param name="entry">The entry of the interface.</param>
    /// <param name="owner">The wrapper to present as the interface.</param>
    /// <returns>The view of <paramref name="owner"/>.</returns>
    private static unsafe object Adapt(ModuleInterfaceEntry entry, Object owner) => entry.Adapt(owner);

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

        // The fabrication of a managed subclass takes a gate of its own, and
        // that gate is always taken before Sync: the factory it runs ends in
        // this constructor, which takes Sync itself.
        if (TypeRegistry.TryFabricate(handle, transfer, out Object? fabricated))
        {
            return fabricated;
        }

        lock (Sync)
        {
            // A disposed wrapper is deliberately not handed out: it has given
            // up its part in the lifetime of the object, its Handle throws, and
            // its release may still be sitting in the queue. Falling through
            // builds a fresh wrapper, which takes a toggle reference of its own
            // and replaces the entry of the old one; the queued release removes
            // its own entry by key and value, so it cannot take the new one
            // away with it.
            if (Wrappers.TryGetValue(handle, out ToggleRef? known) &&
                known.TryGetTarget(out Object? existing) &&
                !existing.IsDisposed)
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
    /// Returns the live wrapper of a native object, and never creates one.
    /// </summary>
    /// <param name="handle">The object to look up, may be <see cref="nint.Zero"/>.</param>
    /// <returns>
    /// The interned wrapper, or <see langword="null"/> when there is none, when
    /// it was collected, or when it was disposed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the lookup that vfunc dispatch uses.
    /// <see cref="FromNative(nint, Transfer)"/> is exactly wrong there: it
    /// builds a wrapper when none is interned, and inside a vfunc that means
    /// building one during <c>g_object_new</c>, whose toggle reference would
    /// collide with the one the constructor is about to install. The doctrine
    /// is therefore that vfunc dispatch only ever dispatches to an interned
    /// live wrapper and otherwise chains up, which covers the construction
    /// window and the window after <see cref="Dispose()"/> with one rule. See
    /// <c>docs/subclassing.md</c> §4.1.
    /// </para>
    /// <para>
    /// The lookup takes no lock. It runs on whichever thread GStreamer calls a
    /// vfunc on — a streaming thread, most of the time — and the answer is
    /// only ever used to decide between dispatching and chaining up, so a
    /// wrapper that is being disposed concurrently may be seen either way and
    /// both are correct.
    /// </para>
    /// </remarks>
    internal static Object? TryGetInterned(nint handle)
    {
        if (handle == nint.Zero)
        {
            return null;
        }

        return Wrappers.TryGetValue(handle, out ToggleRef? known) &&
            known.TryGetTarget(out Object? existing) &&
            !existing.IsDisposed
                ? existing
                : null;
    }

    /// <summary>
    /// Tells the constructor of this thread that the instance it is about to
    /// wrap is one native code created and still owns.
    /// </summary>
    /// <param name="handle">The instance being adopted.</param>
    /// <remarks>
    /// The constructor consumes and clears this whatever happens next, and only
    /// takes the adopting branch when the handle it was armed with is the one
    /// it is given.
    /// </remarks>
    internal static void ArmAdopt(nint handle) => t_adopting = handle;

    /// <summary>
    /// Returns the live wrapper of a native object, and builds the one of a
    /// managed subclass when there is none.
    /// </summary>
    /// <param name="handle">The object to look up, may be <see cref="nint.Zero"/>.</param>
    /// <returns>
    /// The interned wrapper, the wrapper that was just built, or
    /// <see langword="null"/> when the instance is not one of a managed
    /// subclass that stated how its wrapper is built, when an instance of that
    /// type is being constructed on this thread, or when its wrapper was
    /// disposed.
    /// </returns>
    /// <remarks>
    /// This is the lookup the generated trampolines use.
    /// <see cref="TryGetInterned"/> is the pure search, which answers nothing
    /// for an instance native code created and which is why an element a
    /// factory made used to reach the implementation below the managed override
    /// instead of the override. Everything that is not fabricated here still
    /// chains up. See <c>docs/subclassing.md</c> §5.4.
    /// </remarks>
    internal static Object? TryGetOrFabricate(nint handle)
    {
        if (TryGetInterned(handle) is { } interned)
        {
            return interned;
        }

        return TypeRegistry.TryFabricate(handle, Transfer.None, out Object? fabricated) ? fabricated : null;
    }

    /// <summary>
    /// Builds the wrapper of an instance of a managed subclass, at most once
    /// per instance.
    /// </summary>
    /// <param name="handle">The instance to wrap, which native code owns.</param>
    /// <param name="factory">The factory the subclass handed to its definition.</param>
    /// <returns>The wrapper, new or the one another thread interned first.</returns>
    /// <exception cref="InvalidOperationException">
    /// The factory answered a wrapper of another object, which is what ignoring
    /// its arguments looks like from here.
    /// </exception>
    internal static Object Fabricate(nint handle, Func<SubclassCtorArgs, Object> factory)
    {
        System.Threading.Lock gate = FabricationGates.GetOrAdd(handle, static _ => new System.Threading.Lock());

        try
        {
            lock (gate)
            {
                if (TryGetInterned(handle) is { } interned)
                {
                    return interned;
                }

                Object wrapper = factory(SubclassCtorArgs.Adopt(handle));

                if (wrapper._handle != handle)
                {
                    // The factory built an instance of its own instead of
                    // wrapping the one it was given, which leaves the instance
                    // native code is holding unwrapped and hands out an object
                    // nobody asked for. The stray one is released here; the
                    // failure is the caller's.
                    wrapper.Dispose();

                    throw new InvalidOperationException(
                        "The factory of a managed subclass answered a wrapper of another object. "
                        + "CreateWrapper has to hand the arguments it is given to the constructor of the "
                        + "subclass and do nothing else. See docs/subclassing.md §5.4.");
                }

                return wrapper;
            }
        }
        finally
        {
            // The gate is only needed until the wrapper is interned: whoever
            // arrives afterwards finds it in the table, whether through this
            // gate or through a fresh one.
            _ = FabricationGates.TryRemove(handle, out _);
        }
    }

    /// <summary>
    /// Tells whether the wrapper of a managed subclass instance was disposed.
    /// </summary>
    /// <param name="handle">The instance to ask about.</param>
    /// <returns>
    /// <see langword="true"/> when a wrapper of this instance gave up its part
    /// in the lifetime of the object.
    /// </returns>
    /// <remarks>
    /// The marker is a word attached to the object itself rather than an entry
    /// of a managed table: the wrapper is gone by then, and the object outlives
    /// it. Disposing the wrapper of a managed subclass is therefore final -
    /// every slot that fires afterwards chains up, and nothing is fabricated
    /// for it again. See <c>docs/subclassing.md</c> §5.4.
    /// </remarks>
    internal static bool WasSubclassDisposed(nint handle) =>
        handle != nint.Zero && GObjectNative.ObjectGetQdata(handle, DisposedQuark()) != nint.Zero;

    /// <summary>
    /// Marks the object of a disposed managed subclass wrapper.
    /// </summary>
    /// <param name="handle">The instance to mark.</param>
    private static void MarkSubclassDisposed(nint handle) =>
        GObjectNative.ObjectSetQdata(handle, DisposedQuark(), 1);

    /// <summary>
    /// Answers the quark of the marker, resolving it once.
    /// </summary>
    /// <returns>The quark of <c>gstsharp-managed-disposed</c>.</returns>
    private static unsafe uint DisposedQuark()
    {
        uint quark = _disposedQuark;
        if (quark != 0)
        {
            return quark;
        }

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8("gstsharp-managed-disposed", buffer);

        // g_quark_from_string copies the name, so the buffer it was read out of
        // is allowed to be the stack. Resolving it twice answers the same
        // quark, which is why the race is not worth a lock.
        quark = GLibNative.QuarkFromString(scope.Pointer);
        _disposedQuark = quark;
        return quark;
    }

    /// <summary>
    /// Releases the native objects whose wrappers have been collected.
    /// </summary>
    /// <remarks>
    /// Applications rarely need to call this: it runs whenever a wrapper is
    /// looked up, whenever a mini object is adopted, and from the idle callback
    /// of a running main loop. An application that has no main loop and that
    /// goes for long stretches without looking a wrapper up — a batch job, or
    /// one that runs its own main context — should call it periodically.
    /// </remarks>
    public static void DrainPendingReleases()
    {
        // Two volatile reads on the common path, so that the callers who drain
        // opportunistically — every wrapper lookup, every mini object that is
        // adopted — pay next to nothing for it.
        if (PendingReleases.IsEmpty)
        {
            return;
        }

        while (PendingReleases.TryDequeue(out PendingRelease pending))
        {
            try
            {
                Release(pending.Handle, pending.ToggleRef);
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
    /// <remarks>
    /// The source is attached to the default main context. An application that
    /// iterates a main context of its own has to call
    /// <see cref="DrainPendingReleases"/> instead, or in addition.
    /// </remarks>
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
    /// <returns>The identifier of the handler, which is never zero.</returns>
    /// <remarks>
    /// <paramref name="state"/> is taken over by GObject and released through
    /// its closure notification, but only once the connection has been made.
    /// When this throws, nothing took the state over and the caller has to
    /// release it with <see cref="CallbackHandle.Free"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="detailedSignal"/> is empty, or the object has no such
    /// signal.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
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
    /// <remarks>
    /// Removing a handler from a wrapper that is already disposed does nothing:
    /// disposing disconnects every handler the wrapper connected.
    /// </remarks>
    public void RemoveHandler(ulong handlerId)
    {
        if (handlerId == 0)
        {
            return;
        }

        // The disposed check and the disconnection have to be one step. The
        // flag is set before the disposer takes this lock, and the release of
        // the object does its bookkeeping under this lock as well, so a
        // handler that is seen as connected here cannot have its object freed
        // underneath it before this block is over. Holding the lock across the
        // disconnection is fine: it destroys our own closure, which frees a
        // GCHandle, and it never unrefs the instance — unlike the removal of
        // the toggle reference, which Release deliberately performs outside
        // the lock.
        lock (Sync)
        {
            _handlers?.Remove(handlerId);

            if (!IsDisposed)
            {
                SignalRegistry.Disconnect(_handle, handlerId);
            }
        }
    }

    /// <summary>
    /// Reads a property.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <returns>The value of the property, which the caller has to dispose.</returns>
    /// <remarks>
    /// <see cref="GetProperty{T}(string)"/> is the same lookup with the content
    /// handed back as a managed type, which is the shorter call wherever the
    /// type is known.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The object has no such property, or the property cannot be read.
    /// </exception>
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

        // g_object_get_property answers a write only property with a warning on
        // the console and a value that was never filled in, which is a failure
        // an application cannot see, so the question is asked here.
        if ((ParamSpec.FlagsOf(pspec) & ParamFlags.Readable) == 0)
        {
            throw new ArgumentException($"The property \"{name}\" of {NativeType.Name} cannot be read.", nameof(name));
        }

        Value value = Value.New(ParamSpec.ValueTypeOf(pspec));
        GObjectNative.ObjectGetProperty(handle, scope.Pointer, ref value.NativeValue);

        // Handle was the last use of this wrapper. A wrapper that native code
        // does not hold besides us is weakly held, so the collector may take it
        // while the calls above are still running, and the release that its
        // finalizer queues may be drained by another thread before they return.
        GC.KeepAlive(this);
        return value;
    }

    /// <summary>
    /// Lists the properties of the class of this object.
    /// </summary>
    /// <returns>
    /// One <see cref="ParamSpec"/> per property, in the order GObject reports
    /// them, each of which the caller has to dispose.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>g_object_class_list_properties</c> against
    /// <c>G_OBJECT_GET_CLASS</c> of this object. The question is about the
    /// class rather than about the instance — every <c>filesrc</c> answers the
    /// same list — and it is asked on the instance because that is where a
    /// class comes from: a <c>GType</c> alone would have to have its class
    /// referenced first, which for an element means creating one anyway.
    /// </para>
    /// <para>
    /// <b>Every specification is the caller's to dispose.</b> The array C hands
    /// back holds pointers the class owns and does not reference, and a
    /// <see cref="ParamSpec"/> wrapper takes a reference of its own so that it
    /// stays valid whatever happens to the class; the array itself is freed
    /// here. A <c>foreach</c> that disposes as it goes is the shape:
    /// </para>
    /// <code>
    /// foreach (ParamSpec property in element.ListProperties())
    /// {
    ///     using (property)
    ///     {
    ///         if ((property.Flags &amp; ParamFlags.Readable) != 0)
    ///         {
    ///             using Value value = element.GetProperty(property.Name);
    ///             Console.WriteLine($"{property.Name} = {Gst.Global.ValueSerialize(value)}");
    ///         }
    ///     }
    /// }
    /// </code>
    /// <para>
    /// <see cref="FindProperty(string)"/> asks the same class for one
    /// specification by name, which is the question to ask when the name is
    /// already known.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public unsafe ParamSpec[] ListProperties()
    {
        nint handle = Handle;

        // The first word of a GTypeInstance is its class, which is what
        // G_OBJECT_GET_CLASS reads; GetProperty takes the same route.
        nint* properties = GObjectNative.ObjectClassListProperties(*(nint*)handle, out uint count);

        // Handle was the last use of this wrapper, and the class is only known
        // to be alive for as long as the object is.
        GC.KeepAlive(this);

        if (properties is null)
        {
            return [];
        }

        try
        {
            ParamSpec[] result = new ParamSpec[count];

            for (uint i = 0; i < count; i++)
            {
                result[i] = ParamSpec.FromNative(properties[i], Transfer.None);
            }

            return result;
        }
        finally
        {
            // The array is a fresh allocation of GObject; what it points at
            // belongs to the class.
            GLibNative.Free((nint)properties);
        }
    }

    /// <summary>
    /// Writes a property.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The new value, which the property copies.</param>
    /// <remarks>
    /// <para>
    /// This is <c>g_object_set_property</c> with its misuses turned into
    /// exceptions. The C call answers an unknown name, a property that cannot
    /// be written — or can only be given to the constructor — and a value it
    /// cannot convert with a warning on the console and a write that never
    /// happens; each of those is refused here before the call instead, the way
    /// <see cref="GetProperty(string)"/> refuses a property that cannot be
    /// read. <see cref="FindProperty(string)"/> is where the same questions
    /// can be asked without an exception.
    /// </para>
    /// <para>
    /// The value does not have to hold the declared type exactly: whatever
    /// GObject can transform still goes through, exactly as it always has —
    /// an integer into a double property, for example. What is refused is a
    /// pair of types <c>g_value_transform</c> refuses, which is the same
    /// boundary the C call decides the write on.
    /// <see cref="SetProperty(string, object?)"/> is the same write taking a
    /// plain managed value instead of a <see cref="Value"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The object has no such property, the property cannot be written at all
    /// or cannot be written any more, or <paramref name="value"/> holds
    /// nothing or holds a type the property cannot take.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public unsafe void SetProperty(string name, in Value value)
    {
        ArgumentNullException.ThrowIfNull(name);

        nint handle = Handle;
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);

        // The first word of a GTypeInstance is its class, which is what
        // G_OBJECT_GET_CLASS reads; GetProperty takes the same route. The
        // specification belongs to the class and is borrowed, so there is
        // nothing here to release.
        nint pspec = GObjectNative.ObjectClassFindProperty(*(nint*)handle, scope.Pointer);
        if (pspec == nint.Zero)
        {
            throw new ArgumentException($"\"{name}\" is not a property of {NativeType.Name}.", nameof(name));
        }

        ParamFlags flags = ParamSpec.FlagsOf(pspec);

        if ((flags & ParamFlags.Writable) == 0)
        {
            throw new ArgumentException(
                $"The property \"{name}\" of {NativeType.Name} cannot be written.",
                nameof(name));
        }

        if ((flags & ParamFlags.ConstructOnly) != 0)
        {
            throw new ArgumentException(
                $"The property \"{name}\" of {NativeType.Name} can only be given to the constructor.",
                nameof(name));
        }

        if (value.IsEmpty)
        {
            throw new ArgumentException(
                $"An empty value cannot be written to the property \"{name}\" of {NativeType.Name}: " +
                "it has no type and holds nothing.",
                nameof(value));
        }

        GType declared = ParamSpec.ValueTypeOf(pspec);

        if (GObjectNative.ValueTypeTransformable(value.Type.Value, declared.Value) == 0)
        {
            throw new ArgumentException(
                $"The property \"{name}\" of {NativeType.Name} holds {declared.Name} and the value holds " +
                $"{value.Type.Name}, which GObject cannot transform into it.",
                nameof(value));
        }

        GObjectNative.ObjectSetProperty(handle, scope.Pointer, ref Unsafe.AsRef(in value).NativeValue);

        // See GetProperty: Handle was the last use of this wrapper.
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Writes a property whose specification the caller has already checked:
    /// <see cref="SetProperty(string, in Value)"/> below its guards.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The new value, of the declared type.</param>
    private unsafe void SetPropertyCore(string name, in Value value)
    {
        nint handle = Handle;
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);
        GObjectNative.ObjectSetProperty(handle, scope.Pointer, ref Unsafe.AsRef(in value).NativeValue);

        // See GetProperty: Handle was the last use of this wrapper.
        GC.KeepAlive(this);
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
        // The flag is set first, and atomically, so that only one caller ever
        // gets past here and so that every reader of IsDisposed — the wrapper
        // lookup, RemoveHandler, Handle — sees the wrapper leave before its
        // object is released.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ToggleRef? toggleRef = Interlocked.Exchange(ref _toggleRef, null);

        if (toggleRef is null || _handle == nint.Zero)
        {
            return;
        }

        if (disposing)
        {
            if (SubclassRegistry.Find(_handle) is not null)
            {
                // The object outlives this wrapper, and nothing is to be
                // fabricated for it again: a slot that fires from here on finds
                // no wrapper and chains up, which is the window after Dispose
                // that §4.1 describes. The marker is written while the toggle
                // reference is still installed, so the object is certainly
                // alive.
                MarkSubclassDisposed(_handle);
            }

            DisconnectAll();
            Release(_handle, toggleRef);
            return;
        }

        // A finalizer must not remove the toggle reference itself: the notify
        // can run concurrently, and native code must not be called from the
        // finalizer thread while another thread holds the object.
        PendingReleases.Enqueue(new PendingRelease(_handle, toggleRef));
        ScheduleIdleDrain();
    }

    /// <summary>
    /// Stops this wrapper halfway through <see cref="Dispose()"/>: the flag is
    /// set, and the toggle reference is still installed and still interned.
    /// </summary>
    /// <remarks>
    /// This is the state another thread can observe while a wrapper is being
    /// disposed, and it is what a lookup used to hand out. It cannot be reached
    /// from a test by disposing or collecting a wrapper, because both of those
    /// go on to release it; the seam exists so that the lookup can be pinned
    /// down on one thread. Pair it with
    /// <see cref="CompleteInterruptedDispose"/>, which finishes what this
    /// started.
    /// </remarks>
    internal void SimulateInterruptedDispose() => Interlocked.Exchange(ref _disposed, 1);

    /// <summary>
    /// Finishes the dispose that <see cref="SimulateInterruptedDispose"/> left
    /// half done.
    /// </summary>
    internal void CompleteInterruptedDispose()
    {
        ToggleRef? toggleRef = Interlocked.Exchange(ref _toggleRef, null);
        if (toggleRef is not null)
        {
            DisconnectAll();
            Release(_handle, toggleRef);
        }

        GC.SuppressFinalize(this);
    }

    private static bool IsFloating(nint handle) =>
        InitiallyUnowned.IsInitiallyUnownedType(TypeRegistry.GetInstanceType(handle)) &&
        GObjectNative.ObjectIsFloating(handle) != 0;

    /// <summary>
    /// Removes the toggle reference of one wrapper, at most once.
    /// </summary>
    /// <param name="handle">The object the toggle reference is on.</param>
    /// <param name="toggleRef">The toggle reference to remove.</param>
    /// <remarks>
    /// <para>
    /// The bookkeeping runs under <see cref="Sync"/> and the native call does
    /// not. Removing the last toggle reference drops the object to zero, which
    /// runs its dispose and finalize chain: arbitrary native code, which may
    /// take locks of its own, may post to a bus, and may come back into managed
    /// code through a weak notification or a signal handler. Holding the one
    /// lock that every wrapper lookup needs across all of that is a deadlock
    /// waiting for a second thread.
    /// </para>
    /// <para>
    /// Nothing is left to protect once the bookkeeping is done:
    /// <see cref="ToggleRef.MarkReleased"/> has already made this the only
    /// caller that reaches the native call, the interning entry is gone, so no
    /// lookup can find the wrapper any more, and the identifier is gone, so a
    /// toggle notification that is still in flight resolves to nothing.
    /// </para>
    /// </remarks>
    private static unsafe void Release(nint handle, ToggleRef toggleRef)
    {
        lock (Sync)
        {
            if (!toggleRef.MarkReleased())
            {
                return;
            }

            // Both removals are by key and value: a fresh wrapper of the same
            // object may have taken the entry over already, and it has to keep
            // it.
            Wrappers.TryRemove(new KeyValuePair<nint, ToggleRef>(handle, toggleRef));
            ToggleRefs.TryRemove(new KeyValuePair<nint, ToggleRef>(toggleRef.UserData, toggleRef));
        }

        GObjectNative.ObjectRemoveToggleRef(handle, &ToggleNotify, toggleRef.UserData);
    }

    /// <summary>
    /// Hands out the next identifier of a toggle reference.
    /// </summary>
    /// <returns>The identifier, which is never zero.</returns>
    private static nint NextToggleId()
    {
        nint id;

        do
        {
            id = (nint)Interlocked.Increment(ref _nextToggleId);
        }
        while (id == nint.Zero);

        return id;
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
            // A notification that arrives after the toggle reference was
            // removed finds nothing here, which is the whole point of the
            // indirection: see the remarks on ToggleRefs.
            if (ToggleRefs.TryGetValue(userData, out ToggleRef? toggleRef))
            {
                toggleRef.SetStrong(isLastRef == 0);
            }
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

            // The handler takes an instance, because a notification always has
            // one. Nothing to wrap it with is a gap in the registry rather than
            // a case the handler has to answer, so it is reported through the
            // trap of this frame instead of being dropped in silence.
            Object sender = FromNative(instance, Transfer.None)
                ?? throw new InvalidOperationException("The notify signal passed no instance.");

            using ParamSpec property = ParamSpec.FromNative(pspec, Transfer.None);
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

    private readonly record struct PendingRelease(nint Handle, ToggleRef ToggleRef);

    /// <summary>
    /// The state behind the toggle reference of one wrapper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The identifier of this object is the <c>data</c> pointer of the toggle
    /// notification, so it has to stay valid and unchanged for as long as the
    /// toggle reference exists. Whether the wrapper is held strongly or weakly
    /// is therefore switched inside this object rather than by swapping the
    /// pointer itself.
    /// </para>
    /// <para>
    /// The object stays alive because the tables that key it — the interning
    /// table and <see cref="ToggleRefs"/> — hold it, and because the release
    /// queue carries it. No <see cref="GCHandle"/> is involved: see the remarks
    /// on <see cref="ToggleRefs"/> for why one must not be.
    /// </para>
    /// </remarks>
    private sealed class ToggleRef
    {
        private readonly object _sync = new();
        private readonly WeakReference<Object> _weak;
        private readonly nint _id;
        private Object? _strong;
        private bool _released;

        internal ToggleRef(Object owner, nint id)
        {
            _weak = new WeakReference<Object>(owner);
            _id = id;

            // Starts strong: the toggle notification demotes it as soon as the
            // toggle reference is the only one left.
            _strong = owner;
        }

        internal nint UserData => _id;

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
    }
}
