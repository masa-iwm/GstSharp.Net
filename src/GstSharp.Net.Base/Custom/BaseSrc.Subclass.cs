using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

namespace Gst.Base;

/// <content>
/// The subclassing surface of <c>GstBaseSrc</c>: the lifecycle and caps slots
/// that every managed source shares.
/// </content>
/// <remarks>
/// The slot that produces data is not here. <c>GstBaseSrc</c> has three of them
/// — <c>create</c>, <c>alloc</c> and <c>fill</c> — that interact with the
/// requested offset and length and with the buffer pool; stage 1 of
/// <c>docs/subclassing.md</c> binds the one shape that has none of that,
/// <see cref="PushSrc.OnCreate"/>, so a managed source that produces buffers
/// derives from <see cref="PushSrc"/> and inherits everything below.
/// </remarks>
public abstract unsafe partial class BaseSrc
{
    /// <summary>
    /// Constructs the managed subclass that
    /// <see cref="SubclassType.NewInstance"/> created an instance of.
    /// </summary>
    /// <param name="args">The instance, as the registration handed it out.</param>
    /// <exception cref="ArgumentException">The instance is not a <c>GstBaseSrc</c>.</exception>
    protected BaseSrc(SubclassCtorArgs args)
        : this(args.HandleFor(GetGType()), args.Transfer)
    {
    }

    /// <summary>
    /// Gets the declaration of <c>GstBaseSrcClass.start</c>, for a subclass
    /// that overrides <see cref="OnStart"/>.
    /// </summary>
    public static VfuncOverride StartOverride { get; } = new(
        &GetGType,
        BaseSrcClassRaw.StartOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, int>)&StartTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseSrcClass.stop</c>, for a subclass that
    /// overrides <see cref="OnStop"/>.
    /// </summary>
    public static VfuncOverride StopOverride { get; } = new(
        &GetGType,
        BaseSrcClassRaw.StopOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, int>)&StopTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseSrcClass.is_seekable</c>, for a
    /// subclass that overrides <see cref="OnIsSeekable"/>.
    /// </summary>
    public static VfuncOverride IsSeekableOverride { get; } = new(
        &GetGType,
        BaseSrcClassRaw.IsSeekableOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, int>)&IsSeekableTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseSrcClass.set_caps</c>, for a subclass
    /// that overrides <see cref="OnSetCaps"/>.
    /// </summary>
    public static VfuncOverride SetCapsOverride { get; } = new(
        &GetGType,
        BaseSrcClassRaw.SetCapsOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&SetCapsTrampoline);

    /// <summary>
    /// Registers a managed subclass of <c>GstBaseSrc</c> with GObject.
    /// </summary>
    /// <param name="typeName">The <c>GType</c> name, unique in the process.</param>
    /// <param name="configureClass">
    /// Describes the class while it is being initialised. It <b>has to</b> add
    /// a pad template named <c>src</c>: <c>GstBaseSrc</c> creates the source
    /// pad of every instance from it, and an element whose class has none
    /// cannot be built.
    /// </param>
    /// <param name="overrides">The slots the subclass takes over.</param>
    /// <returns>The registration.</returns>
    /// <remarks>
    /// See <see cref="Gst.Element.DefineSubclass"/> for what the arguments mean
    /// and when the registration happens.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The type name is not a legal <c>GType</c> name, or a declared slot
    /// belongs to a class that <c>GstBaseSrc</c> does not derive from.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is taken, the class initialiser failed, or it added no
    /// <c>src</c> pad template.
    /// </exception>
    public static new SubclassType DefineSubclass(
        string typeName,
        Action<ClassConfig> configureClass,
        params VfuncOverride[] overrides)
    {
        ArgumentNullException.ThrowIfNull(configureClass);

        SubclassType type = SubclassType.Define(new GType(GetGType()), typeName, configureClass, overrides);
        type.RequirePadTemplate("src");
        return type;
    }

    /// <summary>
    /// Starts the source: opens whatever it reads from, and gets ready to
    /// produce data.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the source is ready. Answering
    /// <see langword="false"/> fails the transition into
    /// <see cref="Gst.State.Paused"/> and posts an error on the bus.
    /// </returns>
    /// <remarks>
    /// This runs on the <see cref="Gst.State.Ready"/> to
    /// <see cref="Gst.State.Paused"/> transition, before any buffer is asked
    /// for, and <see cref="OnStop"/> is its counterpart. An exception is
    /// reported through <c>GstSharp.UnhandledCallbackException</c> and answered
    /// with <see langword="false"/>.
    /// </remarks>
    protected virtual bool OnStart() => ChainUpStart();

    /// <summary>
    /// Stops the source: releases what <see cref="OnStart"/> acquired.
    /// </summary>
    /// <returns><see langword="true"/> when the source stopped cleanly.</returns>
    /// <remarks>
    /// This runs on the <see cref="Gst.State.Paused"/> to
    /// <see cref="Gst.State.Ready"/> transition, after the streaming thread has
    /// gone away, so no buffer is being produced while it runs.
    /// </remarks>
    protected virtual bool OnStop() => ChainUpStop();

    /// <summary>
    /// Reports whether the source can seek, which decides whether it can be
    /// driven in pull mode and whether seek events reach it.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the source can seek. The base implementation
    /// chains up, and <c>GstBaseSrc</c> answers <see langword="false"/>.
    /// </returns>
    protected virtual bool OnIsSeekable() => ChainUpIsSeekable();

    /// <summary>
    /// Takes the caps that were negotiated on the source pad.
    /// </summary>
    /// <param name="caps">
    /// The negotiated caps, which are fixed. <b>They are borrowed for the
    /// length of the call</b>: GStreamer keeps owning them, and the wrapper is
    /// released when the call returns. <see cref="Gst.Caps.Copy"/> is how to
    /// keep them.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the source can produce data in that format.
    /// Answering <see langword="false"/> fails the negotiation.
    /// </returns>
    /// <remarks>
    /// This is where a source learns the format it has to produce, and it runs
    /// before the first buffer. It is only reached when negotiation actually
    /// happens: a source whose pad template caps are <c>ANY</c> is left alone
    /// by <c>GstBaseSrc</c>, which is why a managed source that wants this
    /// declares real caps on its pad template.
    /// </remarks>
    protected virtual bool OnSetCaps(Gst.Caps caps) => ChainUpSetCaps(caps);

    /// <summary>Runs the implementation of <c>start</c> below the managed override.</summary>
    /// <returns>What the parent class reports, or <see langword="true"/> when it has no implementation.</returns>
    /// <remarks>
    /// <c>GstBaseSrc</c> installs no <c>start</c> of its own — a source that
    /// needs nothing to start is started — so the documented default of a null
    /// slot is success.
    /// </remarks>
    protected bool ChainUpStart()
    {
        bool result = ChainUpStart(Handle);
        GC.KeepAlive(this);
        return result;
    }

    /// <summary>Runs the implementation of <c>stop</c> below the managed override.</summary>
    /// <returns>What the parent class reports, or <see langword="true"/> when it has no implementation.</returns>
    protected bool ChainUpStop()
    {
        bool result = ChainUpStop(Handle);
        GC.KeepAlive(this);
        return result;
    }

    /// <summary>Runs the implementation of <c>is_seekable</c> below the managed override.</summary>
    /// <returns>What the parent class reports, or <see langword="false"/> when it has no implementation.</returns>
    protected bool ChainUpIsSeekable()
    {
        bool result = ChainUpIsSeekable(Handle);
        GC.KeepAlive(this);
        return result;
    }

    /// <summary>Runs the implementation of <c>set_caps</c> below the managed override.</summary>
    /// <param name="caps">The negotiated caps, borrowed by the call.</param>
    /// <returns>What the parent class reports, or <see langword="true"/> when it has no implementation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="caps"/> is <see langword="null"/>.</exception>
    protected bool ChainUpSetCaps(Gst.Caps caps)
    {
        ArgumentNullException.ThrowIfNull(caps);

        bool result = ChainUpSetCaps(Handle, caps.Handle);
        GC.KeepAlive(this);
        GC.KeepAlive(caps);
        return result;
    }

    private static bool ChainUpStart(nint source)
    {
        delegate* unmanaged[Cdecl]<nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, int>)ParentClassOf(source)->Start;

        return slot is null || slot(source) != 0;
    }

    private static bool ChainUpStop(nint source)
    {
        delegate* unmanaged[Cdecl]<nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, int>)ParentClassOf(source)->Stop;

        return slot is null || slot(source) != 0;
    }

    private static bool ChainUpIsSeekable(nint source)
    {
        delegate* unmanaged[Cdecl]<nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, int>)ParentClassOf(source)->IsSeekable;

        return slot is not null && slot(source) != 0;
    }

    private static bool ChainUpSetCaps(nint source, nint caps)
    {
        delegate* unmanaged[Cdecl]<nint, nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, nint, int>)ParentClassOf(source)->SetCaps;

        return slot is null || slot(source, caps) != 0;
    }

    /// <summary>
    /// Returns the class the registration captured, which is the one an
    /// override chains up through.
    /// </summary>
    /// <param name="source">The native source.</param>
    /// <returns>The parent class of the managed subclass.</returns>
    /// <remarks>
    /// Never the instance's own class: its slots hold the trampolines. The
    /// lookup is one level deep, which is correct exactly because a managed
    /// subclass cannot be derived from. See <c>docs/subclassing.md</c> §4.4.
    /// </remarks>
    private static BaseSrcClassRaw* ParentClassOf(nint source) =>
        (BaseSrcClassRaw*)SubclassRegistry.DescriptorFor(source).ParentClass;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StartTrampoline(nint source)
    {
        try
        {
            return GObjectObject.TryGetInterned(source) is BaseSrc managed
                ? managed.OnStart() ? 1 : 0
                : ChainUpStart(source) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StopTrampoline(nint source)
    {
        try
        {
            return GObjectObject.TryGetInterned(source) is BaseSrc managed
                ? managed.OnStop() ? 1 : 0
                : ChainUpStop(source) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int IsSeekableTrampoline(nint source)
    {
        try
        {
            return GObjectObject.TryGetInterned(source) is BaseSrc managed
                ? managed.OnIsSeekable() ? 1 : 0
                : ChainUpIsSeekable(source) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetCapsTrampoline(nint source, nint caps)
    {
        try
        {
            if (GObjectObject.TryGetInterned(source) is not BaseSrc managed)
            {
                return ChainUpSetCaps(source, caps) ? 1 : 0;
            }

            // Borrowed, not referenced: the caps belong to the negotiation that
            // is running, and a reference of our own would only make them look
            // unwritable to the override. See Gst.Interop.Borrowed.
            using Gst.Caps borrowed = Gst.Caps.Borrow(caps);
            return managed.OnSetCaps(borrowed) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }
}
