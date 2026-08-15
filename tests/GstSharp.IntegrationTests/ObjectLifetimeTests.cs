using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Object = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The two lifetime rules of the interned GObject wrapper that a review can
/// only assert against a real object: a wrapper that is on its way out is never
/// handed to anybody else, and a connection that GObject refuses leaves nothing
/// behind.
/// </summary>
[Collection(GstCollection.Name)]
public sealed unsafe class ObjectLifetimeTests
{
    /// <summary>A signal name that no GObject has.</summary>
    private const string UnknownSignal = "no-such-signal-anywhere";

    /// <summary>
    /// Looking an object up while its wrapper is being disposed builds a fresh
    /// wrapper instead of handing the disposed one out. The disposed wrapper
    /// would throw from every member, including <c>Handle</c>, which turns a
    /// perfectly ordinary lookup into an <see cref="ObjectDisposedException"/>
    /// somewhere else entirely.
    /// </summary>
    [Fact]
    public void LookupSkipsAWrapperThatIsBeingDisposed()
    {
        Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "half-disposed"));
        nint handle = element.Handle;

        // The state another thread sees between the flag and the release: the
        // wrapper is disposed, its toggle reference is still installed, and it
        // is still the one the interning table points at.
        element.SimulateInterruptedDispose();
        Assert.True(element.IsDisposed);

        Object fresh = Assert.IsAssignableFrom<Object>(Object.FromNative(handle, Transfer.None));

        Assert.NotSame(element, fresh);
        Assert.False(fresh.IsDisposed);
        Assert.Equal(handle, fresh.Handle);

        // The fresh wrapper is the one that is interned now, and it stays that
        // way when the release of the old one finally runs: the old release
        // removes its own entry, matched by value, and not the entry that
        // replaced it.
        Assert.Same(fresh, Object.FromNative(handle, Transfer.None));

        element.CompleteInterruptedDispose();

        Assert.Same(fresh, Object.FromNative(handle, Transfer.None));
        Assert.Equal(handle, fresh.Handle);

        fresh.Dispose();
    }

    /// <summary>
    /// Constructing a second wrapper for an object that already has one is a
    /// misuse: it would install a second toggle reference. Wrappers are
    /// obtained from <c>FromNative</c>, which is what the message says.
    /// </summary>
    [Fact]
    public void ASecondWrapperOfTheSameObjectIsRefused()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "only-one"));

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => new Probe(element.Handle, Transfer.None));

        Assert.Contains("FromNative", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>g_signal_connect_data</c> answers a signal name it does not know with
    /// zero, and it does not run the destroy notification of the closure while
    /// doing so. Reporting that as a failure is what keeps the state of the
    /// handler from leaking and a handler identifier of zero out of the list of
    /// tracked handlers.
    /// </summary>
    [Fact]
    public void ConnectingAnUnknownSignalFailsAndLeavesTheStateWithTheCaller()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "unconnected"));

        (WeakReference state, CallbackHandle handle) = ConnectToNothing(element.Handle);

        // Nothing took the state over, so freeing it here is the first and only
        // free. A second one would throw, which is exactly why the connection
        // does not free it itself: every caller of it already does.
        handle.Free();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(state.IsAlive);
    }

    /// <summary>
    /// Connects to a signal that does not exist and reports what is left over.
    /// </summary>
    /// <param name="instance">The object to connect to.</param>
    /// <returns>
    /// A weak reference to the state of the handler, and the handle that holds
    /// it.
    /// </returns>
    /// <remarks>
    /// The state is created in a frame of its own, so that the test frame does
    /// not keep it alive when the collection below decides whether the handle
    /// was the only thing that did.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference State, CallbackHandle Handle) ConnectToNothing(nint instance)
    {
        object state = new();
        WeakReference weak = new(state);
        CallbackHandle handle = CallbackHandle.Alloc(state);

        ArgumentException error = Assert.Throws<ArgumentException>(() => SignalRegistry.Connect(
            instance,
            UnknownSignal,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&NeverCalled,
            handle,
            after: false));

        Assert.Equal("detailedSignal", error.ParamName);
        Assert.Contains(UnknownSignal, error.Message, StringComparison.Ordinal);

        return (weak, handle);
    }

    /// <summary>
    /// The address that is offered as the handler. GObject rejects the name
    /// before it ever looks at it.
    /// </summary>
    /// <param name="instance">The object the signal was emitted on.</param>
    /// <param name="userData">The state of the handler.</param>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void NeverCalled(nint instance, nint userData)
    {
        _ = instance;
        _ = userData;
    }

    /// <summary>
    /// A wrapper that is built by hand, the way a binding assembly builds one
    /// from a factory of the type registry.
    /// </summary>
    private sealed class Probe : Object
    {
        /// <summary>Wraps an object.</summary>
        /// <param name="handle">The object to wrap.</param>
        /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
        internal Probe(nint handle, Transfer transfer)
            : base(handle, transfer)
        {
        }
    }
}
