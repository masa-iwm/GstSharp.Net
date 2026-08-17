using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;
using GObjectObject = Gst.GObject.Object;

namespace Gst.Base;

/// <content>
/// The subclassing surface of <c>GstPushSrc</c>: the one slot that produces
/// data, on top of everything <see cref="BaseSrc"/> offers.
/// </content>
public unsafe partial class PushSrc
{
    /// <summary>
    /// Constructs the managed subclass that
    /// <see cref="SubclassType.NewInstance"/> created an instance of.
    /// </summary>
    /// <param name="args">The instance, as the registration handed it out.</param>
    /// <exception cref="ArgumentException">The instance is not a <c>GstPushSrc</c>.</exception>
    protected PushSrc(SubclassCtorArgs args)
        : this(args.HandleFor(GetGType()), args.Transfer)
    {
    }

    /// <summary>
    /// Gets the declaration of <c>GstPushSrcClass.create</c>, for a subclass
    /// that overrides <see cref="OnCreate"/>.
    /// </summary>
    /// <remarks>
    /// This slot is presence sensitive: <c>GstPushSrc</c> looks at whether
    /// <c>create</c> is set and only falls back to <c>alloc</c> plus
    /// <c>fill</c> when it is not. Declaring it is therefore the statement that
    /// the subclass produces whole buffers itself.
    /// </remarks>
    public static VfuncOverride CreateOverride { get; } = new(
        &GetGType,
        PushSrcClassRaw.CreateOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint*, int>)&CreateTrampoline);

    /// <summary>
    /// Registers a managed subclass of <c>GstPushSrc</c> with GObject.
    /// </summary>
    /// <param name="typeName">The <c>GType</c> name, unique in the process.</param>
    /// <param name="configureClass">
    /// Describes the class while it is being initialised. It <b>has to</b> add
    /// a pad template named <c>src</c>.
    /// </param>
    /// <param name="overrides">
    /// The slots the subclass takes over, from this class, from
    /// <see cref="BaseSrc"/> and from <see cref="Gst.Element"/>.
    /// </param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The type name is not a legal <c>GType</c> name, or a declared slot
    /// belongs to a class that <c>GstPushSrc</c> does not derive from.
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
    /// Produces the next buffer of the stream. This is what a managed source
    /// is.
    /// </summary>
    /// <param name="buffer">
    /// Receives the buffer that was produced, which the call takes over: the
    /// wrapper is released when the override returns and the reference goes to
    /// GStreamer. Produce a buffer only when the answer is
    /// <see cref="Gst.FlowReturn.Ok"/>.
    /// </param>
    /// <returns>
    /// <see cref="Gst.FlowReturn.Ok"/> with a buffer,
    /// <see cref="Gst.FlowReturn.Eos"/> to end the stream — which makes
    /// <c>GstBaseSrc</c> push an end of stream event downstream, so that the
    /// sink posts <see cref="Gst.MessageType.Eos"/> on the bus — or an error
    /// such as <see cref="Gst.FlowReturn.Error"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This runs on the streaming thread of the source, over and over, between
    /// <see cref="BaseSrc.OnStart"/> and <see cref="BaseSrc.OnStop"/>. Blocking
    /// in it is normal for a live source, but a source that blocks has to be
    /// woken by the <c>unlock</c> slot, which stage 1 does not bind: a managed
    /// source therefore either produces promptly or ends the stream.
    /// </para>
    /// <para>
    /// The ownership of the produced buffer follows the C signature, whose
    /// <c>GstBuffer **buf</c> is <c>transfer full</c>: the override hands a
    /// buffer it owns over and keeps nothing, in the same shape in which
    /// <c>AppSrc.PushBuffer</c> consumes the buffer it is given. Answering
    /// <see cref="Gst.FlowReturn.Ok"/> without a buffer is a bug in the
    /// override and is reported through
    /// <c>GstSharp.UnhandledCallbackException</c> as
    /// <see cref="Gst.FlowReturn.Error"/>, as is an exception.
    /// </para>
    /// <para>
    /// The buffer GStreamer may have put into <c>*buf</c> before the call is
    /// not passed on: in push scheduling there is none, and a source that
    /// allocates its own buffer ignores it. Filling a provided buffer is what
    /// the <c>fill</c> slot is for, which stage 2 binds.
    /// </para>
    /// </remarks>
    protected virtual Gst.FlowReturn OnCreate(out Gst.Buffer? buffer) => ChainUpCreate(out buffer);

    /// <summary>
    /// Runs the implementation of <c>create</c> below the managed override.
    /// </summary>
    /// <param name="buffer">Receives the buffer the parent class produced, which the caller owns.</param>
    /// <returns>
    /// What the parent class reports, or
    /// <see cref="Gst.FlowReturn.NotSupported"/> when it has no implementation
    /// — which is the normal case, because <c>GstPushSrc</c> installs no
    /// <c>create</c> of its own. A subclass that declares the slot is expected
    /// to produce the data rather than to chain up.
    /// </returns>
    protected Gst.FlowReturn ChainUpCreate(out Gst.Buffer? buffer)
    {
        nint produced = nint.Zero;
        Gst.FlowReturn result = ChainUpCreate(Handle, &produced);
        GC.KeepAlive(this);

        buffer = produced == nint.Zero ? null : Gst.Buffer.FromNative(produced, Transfer.Full);
        return result;
    }

    private static Gst.FlowReturn ChainUpCreate(nint source, nint* buffer)
    {
        delegate* unmanaged[Cdecl]<nint, nint*, int> slot =
            (delegate* unmanaged[Cdecl]<nint, nint*, int>)ParentClassOf(source)->Create;

        return slot is null ? Gst.FlowReturn.NotSupported : (Gst.FlowReturn)slot(source, buffer);
    }

    /// <summary>
    /// Returns the class the registration captured, which is the one an
    /// override chains up through.
    /// </summary>
    /// <param name="source">The native source.</param>
    /// <returns>The parent class of the managed subclass.</returns>
    private static PushSrcClassRaw* ParentClassOf(nint source) =>
        (PushSrcClassRaw*)SubclassRegistry.DescriptorFor(source).ParentClass;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int CreateTrampoline(nint source, nint* buffer)
    {
        try
        {
            if (GObjectObject.TryGetInterned(source) is not PushSrc managed)
            {
                return (int)ChainUpCreate(source, buffer);
            }

            Gst.Buffer? produced = null;

            try
            {
                Gst.FlowReturn result = managed.OnCreate(out produced);

                if (result != Gst.FlowReturn.Ok)
                {
                    // Nothing is written on a non-OK answer, so a buffer that
                    // was produced anyway is simply released with the wrapper.
                    return (int)result;
                }

                if (produced is null)
                {
                    throw new InvalidOperationException(
                        "A managed push source answered FlowReturn.Ok without producing a buffer. Produce one, " +
                        "or answer Eos to end the stream.");
                }

                // The reference the wrapper owns is what GStreamer gets: it is
                // duplicated here and given back by disposing the wrapper, so
                // that the count ends up exactly where a C implementation
                // leaves it — one reference, owned by GstBaseSrc.
                nint handle = produced.Handle;
                GstNative.MiniObjectRef(handle);
                *buffer = handle;

                return (int)Gst.FlowReturn.Ok;
            }
            finally
            {
                produced?.Dispose();
            }
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            return (int)Gst.FlowReturn.Error;
        }
    }
}
