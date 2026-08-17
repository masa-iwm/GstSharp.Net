using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

namespace Gst.Base;

/// <content>
/// The subclassing surface of <c>GstBaseTransform</c>: the lifecycle, the caps
/// and the in place transform.
/// </content>
/// <remarks>
/// Only the in place shape is bound in stage 1 of
/// <c>docs/subclassing.md</c>. The out of place <c>transform</c> comes with
/// <c>transform_caps</c>, <c>transform_size</c> and <c>get_unit_size</c>,
/// because a filter that changes the format has to say what it can produce and
/// how large it is; a filter that rewrites a buffer where it lies needs none of
/// that.
/// </remarks>
public abstract unsafe partial class BaseTransform
{
    /// <summary>
    /// Constructs the managed subclass that
    /// <see cref="SubclassType.NewInstance"/> created an instance of.
    /// </summary>
    /// <param name="args">The instance, as the registration handed it out.</param>
    /// <exception cref="ArgumentException">The instance is not a <c>GstBaseTransform</c>.</exception>
    protected BaseTransform(SubclassCtorArgs args)
        : this(args.HandleFor(GetGType()), args.Transfer)
    {
    }

    /// <summary>
    /// Gets the declaration of <c>GstBaseTransformClass.start</c>, for a
    /// subclass that overrides <see cref="OnStart"/>.
    /// </summary>
    public static VfuncOverride StartOverride { get; } = new(
        &GetGType,
        BaseTransformClassRaw.StartOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, int>)&StartTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseTransformClass.stop</c>, for a
    /// subclass that overrides <see cref="OnStop"/>.
    /// </summary>
    public static VfuncOverride StopOverride { get; } = new(
        &GetGType,
        BaseTransformClassRaw.StopOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, int>)&StopTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseTransformClass.set_caps</c>, for a
    /// subclass that overrides <see cref="OnSetCaps"/>.
    /// </summary>
    public static VfuncOverride SetCapsOverride { get; } = new(
        &GetGType,
        BaseTransformClassRaw.SetCapsOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&SetCapsTrampoline);

    /// <summary>
    /// Gets the declaration of <c>GstBaseTransformClass.transform_ip</c>, for a
    /// subclass that overrides <see cref="OnTransformIp"/>.
    /// </summary>
    /// <remarks>
    /// This slot is presence sensitive, and more so than most:
    /// <c>GstBaseTransform</c> decides from which of <c>transform</c> and
    /// <c>transform_ip</c> are set whether it works in place, whether it can
    /// pass buffers through untouched, and whether it has to allocate an output
    /// buffer at all. Declaring it is the statement that the element rewrites
    /// the buffers it is given.
    /// </remarks>
    public static VfuncOverride TransformIpOverride { get; } = new(
        &GetGType,
        BaseTransformClassRaw.TransformIpOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&TransformIpTrampoline);

    /// <summary>
    /// Registers a managed subclass of <c>GstBaseTransform</c> with GObject.
    /// </summary>
    /// <param name="typeName">The <c>GType</c> name, unique in the process.</param>
    /// <param name="configureClass">
    /// Describes the class while it is being initialised. It <b>has to</b> add
    /// pad templates named <c>src</c> and <c>sink</c>:
    /// <c>GstBaseTransform</c> creates both pads of every instance from them,
    /// and an element whose class is missing either cannot be built.
    /// </param>
    /// <param name="overrides">The slots the subclass takes over.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The type name is not a legal <c>GType</c> name, or a declared slot
    /// belongs to a class that <c>GstBaseTransform</c> does not derive from.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is taken, the class initialiser failed, or it added no
    /// <c>src</c> or no <c>sink</c> pad template.
    /// </exception>
    public static new SubclassType DefineSubclass(
        string typeName,
        Action<ClassConfig> configureClass,
        params VfuncOverride[] overrides)
    {
        ArgumentNullException.ThrowIfNull(configureClass);

        SubclassType type = SubclassType.Define(new GType(GetGType()), typeName, configureClass, overrides);
        type.RequirePadTemplate("sink");
        type.RequirePadTemplate("src");
        return type;
    }

    /// <summary>Starts the filter: acquires whatever it needs to transform.</summary>
    /// <returns><see langword="true"/> when the filter is ready.</returns>
    protected virtual bool OnStart() => ChainUpStart();

    /// <summary>Stops the filter: releases what <see cref="OnStart"/> acquired.</summary>
    /// <returns><see langword="true"/> when the filter stopped cleanly.</returns>
    protected virtual bool OnStop() => ChainUpStop();

    /// <summary>
    /// Takes the caps that were negotiated on both pads.
    /// </summary>
    /// <param name="inCaps">
    /// The caps of the sink pad, borrowed for the length of the call.
    /// </param>
    /// <param name="outCaps">
    /// The caps of the source pad, borrowed for the length of the call. For an
    /// in place filter these are the same caps as <paramref name="inCaps"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the filter can work with that pair.
    /// Answering <see langword="false"/> fails the negotiation.
    /// </returns>
    protected virtual bool OnSetCaps(Gst.Caps inCaps, Gst.Caps outCaps) => ChainUpSetCaps(inCaps, outCaps);

    /// <summary>
    /// Rewrites one buffer where it lies. This is what a managed in place
    /// filter is.
    /// </summary>
    /// <param name="buffer">
    /// The buffer to modify. <b>It is borrowed for the length of the call</b>
    /// and it is writable: <c>GstBaseTransform</c> made sure of that before
    /// calling, and the wrapper deliberately takes no reference of its own so
    /// that <see cref="Gst.Buffer.Map(Gst.MapFlags)"/> with
    /// <see cref="Gst.MapFlags.Write"/> works. Do not keep it;
    /// <see cref="Gst.Buffer.Copy"/> is how to.
    /// </param>
    /// <returns>
    /// <see cref="Gst.FlowReturn.Ok"/> when the buffer was transformed.
    /// Anything else stops the stream; an exception is reported through
    /// <c>GstSharp.UnhandledCallbackException</c> and answered with
    /// <see cref="Gst.FlowReturn.Error"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This runs on the streaming thread. The buffer may be the very one the
    /// upstream element allocated, so a filter that has to keep the data copies
    /// it out; the size of the buffer cannot change here, which is what makes
    /// the in place shape simple enough to bind by hand.
    /// </para>
    /// <para>
    /// The element also has to be told to work in place, with
    /// <see cref="BaseTransform.SetInPlace(bool)"/>, unless the negotiated caps
    /// of both pads are the same and the class was left to work it out.
    /// </para>
    /// </remarks>
    protected virtual Gst.FlowReturn OnTransformIp(Gst.Buffer buffer) => ChainUpTransformIp(buffer);

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
    /// <param name="inCaps">The caps of the sink pad, borrowed by the call.</param>
    /// <param name="outCaps">The caps of the source pad, borrowed by the call.</param>
    /// <returns>What the parent class reports, or <see langword="true"/> when it has no implementation.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected bool ChainUpSetCaps(Gst.Caps inCaps, Gst.Caps outCaps)
    {
        ArgumentNullException.ThrowIfNull(inCaps);
        ArgumentNullException.ThrowIfNull(outCaps);

        bool result = ChainUpSetCaps(Handle, inCaps.Handle, outCaps.Handle);
        GC.KeepAlive(this);
        GC.KeepAlive(inCaps);
        GC.KeepAlive(outCaps);
        return result;
    }

    /// <summary>Runs the implementation of <c>transform_ip</c> below the managed override.</summary>
    /// <param name="buffer">The buffer to transform, borrowed by the call.</param>
    /// <returns>
    /// What the parent class reports, or <see cref="Gst.FlowReturn.Ok"/> when
    /// it has no implementation: nothing below the override transforms
    /// anything, so the buffer passes through as it is.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    protected Gst.FlowReturn ChainUpTransformIp(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        Gst.FlowReturn result = ChainUpTransformIp(Handle, buffer.Handle);
        GC.KeepAlive(this);
        GC.KeepAlive(buffer);
        return result;
    }

    private static bool ChainUpStart(nint transform)
    {
        delegate* unmanaged[Cdecl]<nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, int>)ParentClassOf(transform)->Start;

        return slot is null || slot(transform) != 0;
    }

    private static bool ChainUpStop(nint transform)
    {
        delegate* unmanaged[Cdecl]<nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, int>)ParentClassOf(transform)->Stop;

        return slot is null || slot(transform) != 0;
    }

    private static bool ChainUpSetCaps(nint transform, nint inCaps, nint outCaps)
    {
        delegate* unmanaged[Cdecl]<nint, nint, nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, nint, nint, int>)ParentClassOf(transform)->SetCaps;

        return slot is null || slot(transform, inCaps, outCaps) != 0;
    }

    private static Gst.FlowReturn ChainUpTransformIp(nint transform, nint buffer)
    {
        delegate* unmanaged[Cdecl]<nint, nint, int> slot =
            (delegate* unmanaged[Cdecl]<nint, nint, int>)ParentClassOf(transform)->TransformIp;

        return slot is null ? Gst.FlowReturn.Ok : (Gst.FlowReturn)slot(transform, buffer);
    }

    /// <summary>
    /// Returns the class the registration captured, which is the one an
    /// override chains up through.
    /// </summary>
    /// <param name="transform">The native filter.</param>
    /// <returns>The parent class of the managed subclass.</returns>
    private static BaseTransformClassRaw* ParentClassOf(nint transform) =>
        (BaseTransformClassRaw*)SubclassRegistry.DescriptorFor(transform).ParentClass;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StartTrampoline(nint transform)
    {
        try
        {
            return GObjectObject.TryGetInterned(transform) is BaseTransform managed
                ? managed.OnStart() ? 1 : 0
                : ChainUpStart(transform) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StopTrampoline(nint transform)
    {
        try
        {
            return GObjectObject.TryGetInterned(transform) is BaseTransform managed
                ? managed.OnStop() ? 1 : 0
                : ChainUpStop(transform) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetCapsTrampoline(nint transform, nint inCaps, nint outCaps)
    {
        try
        {
            if (GObjectObject.TryGetInterned(transform) is not BaseTransform managed)
            {
                return ChainUpSetCaps(transform, inCaps, outCaps) ? 1 : 0;
            }

            using Gst.Caps borrowedIn = Gst.Caps.Borrow(inCaps);
            using Gst.Caps borrowedOut = Gst.Caps.Borrow(outCaps);
            return managed.OnSetCaps(borrowedIn, borrowedOut) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TransformIpTrampoline(nint transform, nint buffer)
    {
        try
        {
            if (GObjectObject.TryGetInterned(transform) is not BaseTransform managed)
            {
                return (int)ChainUpTransformIp(transform, buffer);
            }

            // The whole point of the borrow: GstBaseTransform hands over a
            // buffer it has already made writable, and gst_buffer_map refuses a
            // write mapping on a buffer that anybody else holds. A wrapper that
            // took a reference of its own would be that somebody.
            using Gst.Buffer borrowed = Gst.Buffer.Borrow(buffer);
            return (int)managed.OnTransformIp(borrowed);
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return (int)Gst.FlowReturn.Error;
        }
    }
}
