using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;
using GObjectObject = Gst.GObject.Object;

namespace Gst;

/// <content>
/// The subclassing surface of <c>GstBin</c>: everything <c>GstElement</c>
/// offers, plus the one hook that makes a container more than a list of
/// children.
/// </content>
public unsafe partial class Bin
{
    /// <summary>
    /// Constructs the managed subclass that
    /// <see cref="SubclassType.NewInstance"/> created an instance of.
    /// </summary>
    /// <param name="args">The instance, as the registration handed it out.</param>
    /// <exception cref="ArgumentException">
    /// The instance is not a <c>GstBin</c>.
    /// </exception>
    protected Bin(SubclassCtorArgs args)
        : this(args.HandleFor(GetGType()), args.Transfer)
    {
    }

    /// <summary>
    /// Gets the declaration of <c>GstBinClass.handle_message</c>, for a
    /// subclass that overrides <see cref="OnHandleMessage"/>.
    /// </summary>
    public static VfuncOverride HandleMessageOverride { get; } = new(
        &GetGType,
        BinClassRaw.HandleMessageOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&HandleMessageTrampoline);

    /// <summary>
    /// Registers a managed subclass of <c>GstBin</c> with GObject.
    /// </summary>
    /// <param name="typeName">The <c>GType</c> name, unique in the process.</param>
    /// <param name="configureClass">
    /// Describes the class while it is being initialised, or
    /// <see langword="null"/>. A bin needs no pad template of its own.
    /// </param>
    /// <param name="overrides">
    /// The slots the subclass takes over, from this class and from
    /// <see cref="Element"/>.
    /// </param>
    /// <returns>The registration.</returns>
    /// <remarks>
    /// See <see cref="Element.DefineSubclass"/> for what the arguments mean and
    /// when the registration happens.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="typeName"/> or <paramref name="overrides"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The type name is not a legal <c>GType</c> name, or a declared slot
    /// belongs to a class that <c>GstBin</c> does not derive from.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is taken, or the class initialiser failed.
    /// </exception>
    public static new SubclassType DefineSubclass(
        string typeName,
        Action<ClassConfig>? configureClass,
        params VfuncOverride[] overrides) =>
        SubclassType.Define(new GType(GetGType()), typeName, configureClass, overrides);

    /// <summary>
    /// Handles a message that a child of this bin posted, before it reaches the
    /// bus.
    /// </summary>
    /// <param name="message">
    /// The message. <b>The override takes it over</b>: chaining up passes it on
    /// and disposes it, and an override that does neither drops the message,
    /// which is what makes this the hook for swallowing or rewriting what
    /// children post.
    /// </param>
    /// <remarks>
    /// <para>
    /// The base implementation chains up, which is what puts the message on the
    /// bus of the pipeline. This runs on whichever thread posted the message,
    /// which for an error or an end of stream is a streaming thread, so it has
    /// to be quick and must not wait for anything that needs that thread.
    /// </para>
    /// <para>
    /// The message wrapper is released when the call returns, whether the
    /// override chained up or not. <see cref="Message.Copy"/> is how to keep
    /// one, and disposing it early is harmless because disposal is idempotent.
    /// </para>
    /// <para>
    /// An exception is reported through <c>GstSharp.UnhandledCallbackException</c>
    /// and the message is dropped: this vfunc has no result to report a failure
    /// with, and passing a message on after the override failed halfway through
    /// is less predictable than losing it.
    /// </para>
    /// </remarks>
    protected virtual void OnHandleMessage(Message message) => ChainUpHandleMessage(message);

    /// <summary>
    /// Passes a message on to the implementation below the managed override,
    /// which is what posts it on the bus.
    /// </summary>
    /// <param name="message">
    /// The message to pass on. The call consumes it: it is disposed when this
    /// method returns, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This wrapper or the message was disposed.</exception>
    protected void ChainUpHandleMessage(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Both handles are read before anything is referenced, so that a
        // disposed wrapper throws without leaking the reference of the other.
        nint bin = Handle;
        nint owned = message.Handle;

        // The reference the parent implementation consumes. Without it the
        // wrapper and the parent would both own the one reference the wrapper
        // holds.
        GstNative.MiniObjectRef(owned);

        ChainUpHandleMessage(bin, owned);

        GC.KeepAlive(this);
        message.Dispose();
    }

    /// <summary>
    /// The static core of the chain-up, shared by the instance helper and by
    /// the no-wrapper fallback of the trampoline.
    /// </summary>
    /// <param name="bin">The native bin.</param>
    /// <param name="message">The message, whose reference is consumed.</param>
    /// <remarks>
    /// <c>GstBin.handle_message</c> is never null, so the fallback below is
    /// unreachable in practice; a null slot means nothing takes the message,
    /// and the documented behaviour of dropping it is to release it.
    /// </remarks>
    private static void ChainUpHandleMessage(nint bin, nint message)
    {
        BinClassRaw* parent = (BinClassRaw*)SubclassRegistry.DescriptorFor(bin).ParentClass;
        delegate* unmanaged[Cdecl]<nint, nint, void> slot =
            (delegate* unmanaged[Cdecl]<nint, nint, void>)parent->HandleMessage;

        if (slot is null)
        {
            GstNative.MiniObjectUnref(message);
            return;
        }

        slot(bin, message);
    }

    /// <summary>
    /// The one unmanaged entry point that every managed bin subclass shares for
    /// this slot.
    /// </summary>
    /// <param name="bin">The bin the vtable was reached through.</param>
    /// <param name="message">The message, whose reference is transferred in.</param>
    /// <remarks>
    /// The message is adopted rather than borrowed, because the C signature
    /// hands its reference over; the wrapper therefore owns it and releasing
    /// the wrapper is what drops the message. See
    /// <c>docs/subclassing.md</c> §4.3.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void HandleMessageTrampoline(nint bin, nint message)
    {
        try
        {
            if (GObjectObject.TryGetInterned(bin) is Bin managed)
            {
                using Message? adopted = Message.FromNative(message, Transfer.Full);

                if (adopted is not null)
                {
                    managed.OnHandleMessage(adopted);
                }

                return;
            }

            ChainUpHandleMessage(bin, message);
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }
}
