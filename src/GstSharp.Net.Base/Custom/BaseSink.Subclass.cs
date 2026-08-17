using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

namespace Gst.Base;

/// <content>
/// The subclassing surface of <c>GstBaseSink</c>: the lifecycle, the caps and
/// the two slots that consume data.
/// </content>
public abstract unsafe partial class BaseSink
{
    /// <summary>
    /// Constructs the managed subclass that
    /// <see cref="SubclassType.NewInstance"/> created an instance of.
    /// </summary>
    /// <param name="args">The instance, as the registration handed it out.</param>
    /// <exception cref="ArgumentException">The instance is not a <c>GstBaseSink</c>.</exception>
    protected BaseSink(SubclassCtorArgs args)
        : this(args.HandleFor(GetGType()), args.Transfer)
    {
    }

    /// <summary>
    /// Gets the declaration of <c>GstBaseSinkClass.start</c>, for a subclass
    /// that overrides <see cref="OnStart"/>.
    /// </summary>
    public static VfuncOverride StartOverride { get; } = new(
        &GetGType,
        BaseSinkClassRaw.StartOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, int>)&StartTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseSinkClass.stop</c>, for a subclass
    /// that overrides <see cref="OnStop"/>.
    /// </summary>
    public static VfuncOverride StopOverride { get; } = new(
        &GetGType,
        BaseSinkClassRaw.StopOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, int>)&StopTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseSinkClass.set_caps</c>, for a subclass
    /// that overrides <see cref="OnSetCaps"/>.
    /// </summary>
    public static VfuncOverride SetCapsOverride { get; } = new(
        &GetGType,
        BaseSinkClassRaw.SetCapsOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&SetCapsTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseSinkClass.preroll</c>, for a subclass
    /// that overrides <see cref="OnPreroll"/>.
    /// </summary>
    public static VfuncOverride PrerollOverride { get; } = new(
        &GetGType,
        BaseSinkClassRaw.PrerollOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&PrerollTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseSinkClass.render</c>, for a subclass
    /// that overrides <see cref="OnRender"/>.
    /// </summary>
    public static VfuncOverride RenderOverride { get; } = new(
        &GetGType,
        BaseSinkClassRaw.RenderOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&RenderTrampoline);

    /// <summary>
    /// Registers a managed subclass of <c>GstBaseSink</c> with GObject.
    /// </summary>
    /// <param name="typeName">The <c>GType</c> name, unique in the process.</param>
    /// <param name="configureClass">
    /// Describes the class while it is being initialised. It <b>has to</b> add
    /// a pad template named <c>sink</c>: <c>GstBaseSink</c> creates the sink
    /// pad of every instance from it, and an element whose class has none
    /// cannot be built.
    /// </param>
    /// <param name="overrides">The slots the subclass takes over.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The type name is not a legal <c>GType</c> name, or a declared slot
    /// belongs to a class that <c>GstBaseSink</c> does not derive from.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is taken, the class initialiser failed, or it added no
    /// <c>sink</c> pad template.
    /// </exception>
    public static new SubclassType DefineSubclass(
        string typeName,
        Action<ClassConfig> configureClass,
        params VfuncOverride[] overrides)
    {
        ArgumentNullException.ThrowIfNull(configureClass);

        SubclassType type = SubclassType.Define(new GType(GetGType()), typeName, configureClass, overrides);
        type.RequirePadTemplate("sink");
        return type;
    }

    /// <summary>Starts the sink: opens whatever it writes to.</summary>
    /// <returns>
    /// <see langword="true"/> when the sink is ready. Answering
    /// <see langword="false"/> fails the transition into
    /// <see cref="Gst.State.Paused"/> and posts an error on the bus.
    /// </returns>
    protected virtual bool OnStart() => ChainUpStart();

    /// <summary>Stops the sink: releases what <see cref="OnStart"/> acquired.</summary>
    /// <returns><see langword="true"/> when the sink stopped cleanly.</returns>
    protected virtual bool OnStop() => ChainUpStop();

    /// <summary>Takes the caps that were negotiated on the sink pad.</summary>
    /// <param name="caps">
    /// The negotiated caps, borrowed for the length of the call.
    /// <see cref="Gst.Caps.Copy"/> is how to keep them.
    /// </param>
    /// <returns><see langword="true"/> when the sink accepts that format.</returns>
    protected virtual bool OnSetCaps(Gst.Caps caps) => ChainUpSetCaps(caps);

    /// <summary>
    /// Prerolls one buffer: the first buffer of the stream, which reaches the
    /// sink before the pipeline plays and which is what makes a pipeline reach
    /// <see cref="Gst.State.Paused"/>.
    /// </summary>
    /// <param name="buffer">
    /// The buffer, borrowed for the length of the call.
    /// <see cref="Gst.Buffer.Copy"/> is how to keep it.
    /// </param>
    /// <returns>
    /// <see cref="Gst.FlowReturn.Ok"/> normally. Anything else stops the
    /// stream.
    /// </returns>
    /// <remarks>
    /// The same buffer is rendered again once the pipeline plays, so a sink
    /// that displays a preview frame does it here and a sink that counts data
    /// does not count here. This runs on the streaming thread.
    /// </remarks>
    protected virtual Gst.FlowReturn OnPreroll(Gst.Buffer buffer) => ChainUpPreroll(buffer);

    /// <summary>
    /// Consumes one buffer. This is what a managed sink is.
    /// </summary>
    /// <param name="buffer">
    /// The buffer, <b>borrowed for the length of the call</b>: GStreamer keeps
    /// owning it and the wrapper is released when the override returns, so a
    /// sink that keeps data copies it — <see cref="Gst.Buffer.Copy"/>, or the
    /// bytes out of a <see cref="Gst.Buffer.Map(Gst.MapFlags)"/> scope.
    /// </param>
    /// <returns>
    /// <see cref="Gst.FlowReturn.Ok"/> when the buffer was consumed.
    /// <see cref="Gst.FlowReturn.Error"/> stops the stream and posts an error;
    /// an exception is reported through
    /// <c>GstSharp.UnhandledCallbackException</c> and answered the same way.
    /// </returns>
    /// <remarks>
    /// This runs on the streaming thread of whatever feeds the sink, after the
    /// clock has waited for the buffer when synchronisation is on. Sinks that
    /// are driven by a managed source in a test or a sample usually turn
    /// synchronisation off, because buffers without timestamps have no time to
    /// wait for.
    /// </remarks>
    protected virtual Gst.FlowReturn OnRender(Gst.Buffer buffer) => ChainUpRender(buffer);

    /// <summary>Runs the implementation of <c>start</c> below the managed override.</summary>
    /// <returns>What the parent class reports, or <see langword="true"/> when it has no implementation.</returns>
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

    /// <summary>Runs the implementation of <c>preroll</c> below the managed override.</summary>
    /// <param name="buffer">The buffer, borrowed by the call.</param>
    /// <returns>
    /// What the parent class reports, or <see cref="Gst.FlowReturn.Ok"/> when
    /// it has no implementation, which is what <c>GstBaseSink</c> does with a
    /// buffer nobody prerolls.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    protected Gst.FlowReturn ChainUpPreroll(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        Gst.FlowReturn result = ChainUpPreroll(Handle, buffer.Handle);
        GC.KeepAlive(this);
        GC.KeepAlive(buffer);
        return result;
    }

    /// <summary>Runs the implementation of <c>render</c> below the managed override.</summary>
    /// <param name="buffer">The buffer, borrowed by the call.</param>
    /// <returns>
    /// What the parent class reports, or <see cref="Gst.FlowReturn.Ok"/> when
    /// it has no implementation: a sink whose class renders nothing accepts the
    /// data and drops it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    protected Gst.FlowReturn ChainUpRender(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        Gst.FlowReturn result = ChainUpRender(Handle, buffer.Handle);
        GC.KeepAlive(this);
        GC.KeepAlive(buffer);
        return result;
    }

    private static bool ChainUpStart(nint sink)
    {
        delegate* unmanaged[Cdecl]<nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, int>)ParentClassOf(sink)->Start;

        return slot is null || slot(sink) != 0;
    }

    private static bool ChainUpStop(nint sink)
    {
        delegate* unmanaged[Cdecl]<nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, int>)ParentClassOf(sink)->Stop;

        return slot is null || slot(sink) != 0;
    }

    private static bool ChainUpSetCaps(nint sink, nint caps)
    {
        delegate* unmanaged[Cdecl]<nint, nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, nint, int>)ParentClassOf(sink)->SetCaps;

        return slot is null || slot(sink, caps) != 0;
    }

    private static Gst.FlowReturn ChainUpPreroll(nint sink, nint buffer)
    {
        delegate* unmanaged[Cdecl]<nint, nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, nint, int>)ParentClassOf(sink)->Preroll;

        return slot is null ? Gst.FlowReturn.Ok : (Gst.FlowReturn)slot(sink, buffer);
    }

    private static Gst.FlowReturn ChainUpRender(nint sink, nint buffer)
    {
        delegate* unmanaged[Cdecl]<nint, nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, nint, int>)ParentClassOf(sink)->Render;

        return slot is null ? Gst.FlowReturn.Ok : (Gst.FlowReturn)slot(sink, buffer);
    }

    /// <summary>
    /// Returns the class the registration captured, which is the one an
    /// override chains up through.
    /// </summary>
    /// <param name="sink">The native sink.</param>
    /// <returns>The parent class of the managed subclass.</returns>
    private static BaseSinkClassRaw* ParentClassOf(nint sink) =>
        (BaseSinkClassRaw*)SubclassRegistry.DescriptorFor(sink).ParentClass;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StartTrampoline(nint sink)
    {
        try
        {
            return GObjectObject.TryGetInterned(sink) is BaseSink managed
                ? managed.OnStart() ? 1 : 0
                : ChainUpStart(sink) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StopTrampoline(nint sink)
    {
        try
        {
            return GObjectObject.TryGetInterned(sink) is BaseSink managed
                ? managed.OnStop() ? 1 : 0
                : ChainUpStop(sink) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetCapsTrampoline(nint sink, nint caps)
    {
        try
        {
            if (GObjectObject.TryGetInterned(sink) is not BaseSink managed)
            {
                return ChainUpSetCaps(sink, caps) ? 1 : 0;
            }

            using Gst.Caps borrowed = Gst.Caps.Borrow(caps);
            return managed.OnSetCaps(borrowed) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int PrerollTrampoline(nint sink, nint buffer)
    {
        try
        {
            if (GObjectObject.TryGetInterned(sink) is not BaseSink managed)
            {
                return (int)ChainUpPreroll(sink, buffer);
            }

            using Gst.Buffer borrowed = Gst.Buffer.Borrow(buffer);
            return (int)managed.OnPreroll(borrowed);
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return (int)Gst.FlowReturn.Error;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RenderTrampoline(nint sink, nint buffer)
    {
        try
        {
            if (GObjectObject.TryGetInterned(sink) is not BaseSink managed)
            {
                return (int)ChainUpRender(sink, buffer);
            }

            // Borrowed, never referenced: the buffer belongs to whoever is
            // pushing it, and a reference of our own would tell the override
            // that a buffer it may legitimately own alone is shared. See
            // Gst.Interop.Borrowed.
            using Gst.Buffer borrowed = Gst.Buffer.Borrow(buffer);
            return (int)managed.OnRender(borrowed);
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return (int)Gst.FlowReturn.Error;
        }
    }
}
