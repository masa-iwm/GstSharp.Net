using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Initialises a metadata item that was just attached to a buffer.
/// </summary>
/// <param name="meta">The item, whose payload is zero filled when this runs.</param>
/// <param name="params">
/// The pointer that was handed to <see cref="Gst.Buffer.AddMeta"/>, or <c>0</c>
/// when the library itself attached the item.
/// </param>
/// <param name="buffer">The buffer the item was attached to.</param>
/// <returns>
/// <see langword="true"/> when the item was initialised.
/// </returns>
/// <remarks>
/// <para>
/// The delegate runs on whatever thread touches the buffer, which is usually a
/// streaming thread of the pipeline and never one the caller chose.
/// </para>
/// <para>
/// Answering <see langword="false"/> makes <see cref="Gst.Buffer.AddMeta"/>
/// answer <see langword="null"/> and the item is freed without the free
/// delegate ever running, so this delegate has to undo whatever it had already
/// done before it refuses. The item wrapper is detached on that path, because
/// the memory behind it is freed as the refusal returns.
/// </para>
/// </remarks>
public delegate bool MetaInitFunction(Gst.Meta meta, nint @params, Gst.Buffer buffer);

/// <summary>
/// Releases whatever a metadata item owns, immediately before it is freed.
/// </summary>
/// <param name="meta">The item that is about to be freed.</param>
/// <param name="buffer">The buffer that carried it.</param>
/// <remarks>
/// <para>
/// The delegate runs on whatever thread touches the buffer, which is usually a
/// streaming thread of the pipeline and never one the caller chose.
/// </para>
/// <para>
/// The buffer is being freed or has just lost the item; the delegate must not
/// keep, ref or return the buffer wrapper, which is disposed when the delegate
/// returns. The item wrapper is dead as soon as this returns as well, because
/// the memory behind it is freed there.
/// </para>
/// </remarks>
public delegate void MetaFreeFunction(Gst.Meta meta, Gst.Buffer buffer);

/// <summary>
/// Carries a metadata item from one buffer onto another.
/// </summary>
/// <param name="transbuf">The buffer the item has to be added to.</param>
/// <param name="meta">The item of <paramref name="buffer"/> that is being carried.</param>
/// <param name="buffer">The buffer that carries <paramref name="meta"/>.</param>
/// <param name="type">
/// What is being done to the buffer; a copy is
/// <c>Gst.GLib.Quark.FromString("gst-copy")</c>.
/// </param>
/// <param name="data">
/// The transformation data, or <c>0</c>. A copy passes the address of a
/// <see cref="Gst.MetaTransformCopy"/>.
/// </param>
/// <returns>
/// <see langword="true"/> when the item was carried over.
/// </returns>
/// <remarks>
/// <para>
/// The delegate runs on whatever thread touches the buffer, which is usually a
/// streaming thread of the pipeline and never one the caller chose.
/// </para>
/// <para>
/// The delegate is called on the SOURCE item and has to add an item to
/// <paramref name="transbuf"/> itself. Answering <see langword="false"/> is
/// only logged by the library; the copy goes on either way. A registration
/// with no transform delegate is not carried across a copy at all.
/// </para>
/// <para>
/// A copy is read by comparing <paramref name="type"/> against
/// <c>Gst.GLib.Quark.FromString("gst-copy")</c> and casting
/// <paramref name="data"/> to <c>Gst.MetaTransformCopy*</c>: the projection is
/// a plain structure, so reading it is a copy of three words and neither side
/// owns anything.
/// </para>
/// </remarks>
public delegate bool MetaTransformFunction(
    Gst.Buffer transbuf,
    Gst.Meta meta,
    Gst.Buffer buffer,
    Gst.GLib.Quark type,
    nint data);

/// <summary>
/// Writes the payload of a metadata item into a byte sink.
/// </summary>
/// <param name="meta">The item to serialise.</param>
/// <param name="data">The sink to append the payload to.</param>
/// <param name="version">
/// The version of the payload format, which starts at <c>0</c> and is handed
/// back to the deserialisation delegate.
/// </param>
/// <returns>
/// <see langword="true"/> when the payload was written. Answering
/// <see langword="false"/> rolls the sink back to the length it had.
/// </returns>
/// <remarks>
/// <para>Available since GStreamer 1.24.</para>
/// <para>
/// The delegate runs on whatever thread touches the buffer, which is usually a
/// streaming thread of the pipeline and never one the caller chose.
/// </para>
/// <para>
/// <paramref name="data"/> is valid only until the delegate returns: the
/// wrapper addresses a structure the library's caller owns, usually one on that
/// caller's stack, and it is never detached, so filing it away leaves a wrapper
/// that reads whatever now lives at that address.
/// </para>
/// </remarks>
public delegate bool MetaSerializeFunction(Gst.Meta meta, Gst.ByteArrayInterface data, ref byte version);

/// <summary>
/// Reads a metadata item back out of the bytes a serialisation wrote.
/// </summary>
/// <param name="info">The implementation the bytes belong to.</param>
/// <param name="buffer">The buffer the item has to be added to.</param>
/// <param name="data">The payload that was serialised.</param>
/// <param name="version">The version the serialisation wrote.</param>
/// <returns>
/// The item the delegate added to <paramref name="buffer"/>, or
/// <see langword="null"/> when the payload was refused.
/// </returns>
/// <remarks>
/// <para>Available since GStreamer 1.24.</para>
/// <para>
/// The delegate runs on whatever thread touches the buffer, which is usually a
/// streaming thread of the pipeline and never one the caller chose.
/// </para>
/// <para>
/// The delegate has to add the item to <paramref name="buffer"/> itself and
/// answer that item; the buffer keeps owning it.
/// </para>
/// </remarks>
public delegate Gst.Meta? MetaDeserializeFunction(
    Gst.MetaInfo info,
    Gst.Buffer buffer,
    System.ReadOnlySpan<byte> data,
    byte version);

/// <summary>
/// Resets a metadata item when a buffer pool takes its buffer back.
/// </summary>
/// <param name="buffer">The buffer being released to its pool.</param>
/// <param name="meta">The item to reset.</param>
/// <remarks>
/// <para>Available since GStreamer 1.24.</para>
/// <para>
/// The buffer comes first, as it does in C. Only
/// <c>GstBufferPool</c> ever calls this; a buffer that has no pool is freed
/// through the free delegate instead.
/// </para>
/// <para>
/// The delegate runs on whatever thread touches the buffer, which is usually a
/// streaming thread of the pipeline and never one the caller chose.
/// </para>
/// </remarks>
public delegate void MetaClearFunction(Gst.Buffer buffer, Gst.Meta meta);

/// <summary>
/// What one call of <see cref="Gst.Meta.Register{T}"/> settled: the payload
/// type of the implementation and the delegates its items run.
/// </summary>
/// <remarks>
/// A registration is immortal, exactly as the implementation block it belongs
/// to is: the library keeps every block it registered until <c>gst_deinit</c>
/// and offers no way of taking one back, so nothing here is ever released and
/// the delegates are rooted for the life of the process.
/// </remarks>
internal sealed class MetaAuthorRegistration
{
    /// <summary>Gets the type stored after the header of every item.</summary>
    internal required Type PayloadType { get; init; }

    /// <summary>Gets how many bytes that type takes.</summary>
    internal required int PayloadSize { get; init; }

    /// <summary>Gets the initialisation delegate, or <see langword="null"/>.</summary>
    internal MetaInitFunction? Init { get; init; }

    /// <summary>Gets the release delegate, or <see langword="null"/>.</summary>
    internal MetaFreeFunction? Free { get; init; }

    /// <summary>Gets the transformation delegate, or <see langword="null"/>.</summary>
    internal MetaTransformFunction? Transform { get; init; }

    /// <summary>Gets the serialisation delegate, or <see langword="null"/>.</summary>
    internal MetaSerializeFunction? Serialize { get; init; }

    /// <summary>Gets the deserialisation delegate, or <see langword="null"/>.</summary>
    internal MetaDeserializeFunction? Deserialize { get; init; }

    /// <summary>Gets the reset delegate, or <see langword="null"/>.</summary>
    internal MetaClearFunction? Clear { get; init; }
}

/// <summary>
/// The implementations this process registered through
/// <see cref="Gst.Meta.Register{T}"/>, keyed by the implementation block the
/// library handed back.
/// </summary>
/// <remarks>
/// <para>
/// The six trampolines below are one shared function pointer per callback kind,
/// not one per registration: NativeAOT has no per-type code generation, so the
/// discriminator has to be recovered from something the callback is handed.
/// That something is the implementation block - <c>meta-&gt;info</c> for five of
/// the six kinds and the first argument for the deserialisation - which is what
/// this table is keyed by. It is the shape <c>Core/GObject/GTypeInfo.cs</c> uses
/// for class initialisation, one step smaller: there is no per-item state at
/// all, so no handle is allocated per call and the trampolines are free of
/// thread affinity.
/// </para>
/// <para>
/// An entry is filed before <c>gst_meta_info_register</c> is called, so that a
/// callback reached through a block another thread already resolved by name
/// always finds it, and it is taken back only when that call refuses the block.
/// Filing it early is exact rather than hopeful: the call answers the very
/// pointer it was handed, and that pointer is what an item stores in its
/// <c>info</c> field. A block that survives the call is immortal, so an entry
/// of a completed registration is never removed. Reads are lock free, so that a
/// metadata callback on a streaming thread never waits on a registration.
/// </para>
/// </remarks>
internal static class MetaAuthorRegistry
{
    private static readonly ConcurrentDictionary<nint, MetaAuthorRegistration> Entries = new();

    /// <summary>Records what a registration settled, before it is offered.</summary>
    /// <param name="info">The implementation block, as it will be registered.</param>
    /// <param name="registration">The payload type and the delegates.</param>
    internal static void Add(nint info, MetaAuthorRegistration registration) =>
        Entries[info] = registration;

    /// <summary>Takes an entry back after the library refused its block.</summary>
    /// <param name="info">The block, which the library has freed.</param>
    internal static void Remove(nint info) => Entries.TryRemove(info, out _);

    /// <summary>Looks an implementation block up.</summary>
    /// <param name="info">The implementation block, or <c>0</c>.</param>
    /// <returns>
    /// The registration, or <see langword="null"/> when the block was not
    /// registered through <see cref="Gst.Meta.Register{T}"/>.
    /// </returns>
    internal static MetaAuthorRegistration? Find(nint info) =>
        info != 0 && Entries.TryGetValue(info, out MetaAuthorRegistration? registration)
            ? registration
            : null;
}

public sealed unsafe partial class Meta
{
    /// <summary>
    /// Gets where the payload of an item of an implementation registered
    /// through <see cref="Register{T}"/> starts, counted from the item itself.
    /// </summary>
    /// <remarks>
    /// An item is the <c>GstMeta</c> header followed by the payload. The header
    /// is two pointer sized words on every platform the bindings run on, and
    /// the start of the payload is rounded up to eight so that a payload of
    /// pointers and 64 bit scalars is aligned wherever the header ends.
    /// </remarks>
    internal static int PayloadOffset => (sizeof(MetaRaw) + 7) & ~7;

    /// <summary>
    /// Registers a metadata implementation whose item is a <c>GstMeta</c>
    /// header followed by one <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The payload. Its alignment requirement must not exceed eight bytes: the
    /// library allocates an item with <c>g_malloc</c> and the bindings promise
    /// nothing stronger than the eight byte alignment the payload offset is
    /// rounded to.
    /// </typeparam>
    /// <param name="api">
    /// The metadata API the implementation implements, which
    /// <see cref="ApiTypeRegister"/> answers.
    /// </param>
    /// <param name="impl">
    /// The name of the implementation, which becomes a <c>GType</c> name and
    /// must therefore be unique in the process and at least three characters
    /// long.
    /// </param>
    /// <param name="init">
    /// Runs when an item is attached to a buffer, or <see langword="null"/>.
    /// </param>
    /// <param name="free">
    /// Runs immediately before an item is freed, or <see langword="null"/>.
    /// </param>
    /// <param name="transform">
    /// Runs when a buffer that carries an item is copied, or
    /// <see langword="null"/> to let the item be dropped by every copy.
    /// </param>
    /// <param name="serialize">
    /// Writes the payload for <see cref="Serialize()"/>, or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="deserialize">
    /// Reads the payload back for <see cref="Deserialize"/>, or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="clear">
    /// Runs when a buffer pool takes a buffer back, or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The implementation block, which the library owns and never releases.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The registration lives for the rest of the process: the library keeps
    /// every implementation it registered in a table it only empties in
    /// <c>gst_deinit</c>, so the delegates handed here are never released and
    /// whatever they capture is rooted for as long.
    /// </para>
    /// <para>
    /// Every delegate handed here runs on whatever thread touches the buffer,
    /// which is usually a streaming thread of the pipeline and never one the
    /// caller chose. Two of them can be running at once, on two buffers, so
    /// whatever state they share has to be safe for that.
    /// </para>
    /// <para>
    /// The payload of an item is reached with <see cref="Payload{T}"/>. It is
    /// zero filled before <paramref name="init"/> runs.
    /// </para>
    /// <para>
    /// <paramref name="transform"/> is handed the item of the SOURCE buffer and
    /// has to add an item to the destination buffer itself; a registration
    /// without one is simply not carried across a copy, which is the behaviour
    /// of a C implementation with a null <c>transform_func</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="impl"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused the registration, which is what an invalid argument
    /// and a name that is already taken both produce.
    /// </exception>
    public static Gst.MetaInfo Register<T>(
        Gst.GObject.GType api,
        string impl,
        MetaInitFunction? init = null,
        MetaFreeFunction? free = null,
        MetaTransformFunction? transform = null,
        MetaSerializeFunction? serialize = null,
        MetaDeserializeFunction? deserialize = null,
        MetaClearFunction? clear = null)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(impl);
        System.Span<byte> implBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
        using Gst.Interop.Utf8Scope implScope = Gst.Interop.GMarshal.StackUtf8(impl, implBuffer);

        int payloadSize = sizeof(T);
        nint info = MetaInfoNew(api.Value, implScope.Pointer, (nuint)(PayloadOffset + payloadSize));
        if (info == 0)
        {
            // The only way past the arguments this member itself builds is an
            // API type of zero, which is what gst_meta_api_type_register
            // answers for a name that was already taken. A clash on the
            // implementation name is not seen here: it leaves the block with
            // an invalid type and is answered by the registration below.
            throw new InvalidOperationException(
                "gst_meta_info_new returned no value, so the metadata implementation was refused: the metadata " +
                "API type is zero, which is what Meta.ApiTypeRegister answers for a name that is already taken.");
        }

        // The six function fields of an implementation block are written here,
        // between the two halves of the registration, because that is the only
        // point at which the C contract makes them mutable: gst_meta_info_new
        // answers a zeroed block that belongs to the caller and
        // gst_meta_info_register takes it over, after which the block is const
        // for the rest of the process. This is why MetaInfoRaw is written to
        // here and read from nowhere else.
        MetaInfoRaw* raw = (MetaInfoRaw*)info;

        // The initialisation trampoline is installed unconditionally: it zero
        // fills the payload before anything else can see it, which the caller
        // gets whether or not it passed an init delegate.
        raw->InitFunc = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&InitTrampoline;
        if (free is not null)
        {
            raw->FreeFunc = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&FreeTrampoline;
        }

        if (transform is not null)
        {
            raw->TransformFunc = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, uint, nint, int>)&TransformTrampoline;
        }

        if (serialize is not null)
        {
            raw->SerializeFunc = (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte*, int>)&SerializeTrampoline;
        }

        if (deserialize is not null)
        {
            raw->DeserializeFunc = (nint)(delegate* unmanaged[Cdecl]<nint, nint, byte*, nuint, byte, nint>)&DeserializeTrampoline;
        }

        if (clear is not null)
        {
            raw->ClearFunc = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&ClearTrampoline;
        }

        // The entry is filed before the block is offered, not after: the
        // registration publishes the name, and another thread that resolves it
        // with gst_meta_get_info can reach a trampoline before this one gets its
        // answer back. The key is exact, because gst_meta_info_register answers
        // the very pointer it was handed and an item stores that pointer in its
        // info field.
        MetaAuthorRegistry.Add(
            info,
            new MetaAuthorRegistration
            {
                PayloadType = typeof(T),
                PayloadSize = payloadSize,
                Init = init,
                Free = free,
                Transform = transform,
                Serialize = serialize,
                Deserialize = deserialize,
                Clear = clear,
            });

        nint registered = MetaInfoRegister(info);
        if (registered == 0)
        {
            // The library freed the block on this path, so there is nothing to
            // release here; the entry is taken back so that a later block at the
            // same address does not inherit this registration.
            MetaAuthorRegistry.Remove(info);
            throw new InvalidOperationException(
                "gst_meta_info_register returned no value, so the metadata implementation was refused: the " +
                "implementation name is already taken by another type, or it is not a valid GType name.");
        }

        return Gst.MetaInfo.FromNative(registered)
            ?? throw new InvalidOperationException("gst_meta_info_register returned no value.");
    }

    /// <summary>
    /// Gets the payload stored after the header of this item.
    /// </summary>
    /// <typeparam name="T">
    /// The payload type the implementation of the item was registered with.
    /// </typeparam>
    /// <returns>
    /// A reference to the payload, which lives as long as the item does.
    /// </returns>
    /// <remarks>
    /// The reference points into the buffer's own allocation, so it is only
    /// valid while the item is attached: writing through it after the item was
    /// removed writes into freed memory.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    /// <exception cref="InvalidCastException">
    /// The implementation of the item was not registered through
    /// <see cref="Register{T}"/>, or it was registered with another payload
    /// type.
    /// </exception>
    public ref T Payload<T>()
        where T : unmanaged
    {
        nint handle = RequireHandle();
        nint info = ((MetaRaw*)handle)->Info;
        if (MetaAuthorRegistry.Find(info) is not { } registration)
        {
            GC.KeepAlive(this);
            throw new InvalidCastException(
                "The implementation of this metadata item was not registered through Gst.Meta.Register, so it " +
                "has no managed payload.");
        }

        if (registration.PayloadType != typeof(T))
        {
            GC.KeepAlive(this);
            throw new InvalidCastException(
                $"The implementation of this metadata item stores a {registration.PayloadType} payload, not a " +
                $"{typeof(T)} one.");
        }

        ref T payload = ref Unsafe.AsRef<T>((byte*)handle + PayloadOffset);
        GC.KeepAlive(this);
        return ref payload;
    }

    /// <summary>
    /// The registration a metadata item belongs to.
    /// </summary>
    /// <param name="meta">The item, as the library handed it over.</param>
    /// <returns>The registration, or <see langword="null"/>.</returns>
    private static MetaAuthorRegistration? RegistrationOf(nint meta) =>
        meta == 0 ? null : MetaAuthorRegistry.Find(((MetaRaw*)meta)->Info);

    /// <summary>
    /// Reports a callback that arrived for an implementation this process did
    /// not register, which is the only way a trampoline can be reached without
    /// a registration behind it.
    /// </summary>
    /// <param name="entryPoint">The C callback field the trampoline sits in.</param>
    private static void ReportUnknownRegistration(string entryPoint) =>
        Gst.Interop.ExceptionTrap.Report(new InvalidOperationException(
            $"The {entryPoint} of a metadata implementation that Gst.Meta.Register did not register was " +
            "invoked, so the callback has no delegate to run."));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int InitTrampoline(nint meta, nint @params, nint buffer)
    {
        // The wrapper is declared out here because every path that answers 0
        // has to empty it: gst_buffer_add_meta frees the item the moment an
        // initialisation refuses, and it does that without calling the free
        // delegate, so this is the only place the wrapper can be detached.
        Gst.Meta? metaValue = null;
        try
        {
            if (RegistrationOf(meta) is not { } registration)
            {
                ReportUnknownRegistration("init_func");
                return 0;
            }

            // The library allocates an item with g_malloc0 when there is no
            // init_func and with g_malloc when there is one, and there always
            // is one here, so the payload is cleared before anything sees it.
            new Span<byte>((byte*)meta + PayloadOffset, registration.PayloadSize).Clear();
            if (registration.Init is not { } init)
            {
                return 1;
            }

            if (buffer == 0)
            {
                throw new InvalidOperationException("GstMetaInitFunction passed no buffer.");
            }

            using Gst.Buffer bufferValue = Gst.Buffer.Borrow(buffer);
            metaValue = Gst.Meta.FromNative(meta)
                ?? throw new InvalidOperationException("GstMetaInitFunction passed no meta.");
            if (init(metaValue, @params, bufferValue))
            {
                // The item stays, so the wrapper stays with it.
                return 1;
            }

            metaValue.Detach();
            return 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            metaValue?.Detach();
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FreeTrampoline(nint meta, nint buffer)
    {
        try
        {
            if (RegistrationOf(meta) is not { } registration)
            {
                ReportUnknownRegistration("free_func");
                return;
            }

            if (registration.Free is not { } free)
            {
                return;
            }

            if (buffer == 0)
            {
                throw new InvalidOperationException("GstMetaFreeFunction passed no buffer.");
            }

            using Gst.Buffer bufferValue = Gst.Buffer.Borrow(buffer);
            Gst.Meta metaValue = Gst.Meta.FromNative(meta)
                ?? throw new InvalidOperationException("GstMetaFreeFunction passed no meta.");
            try
            {
                free(metaValue, bufferValue);
            }
            finally
            {
                // The memory of the item is freed as this returns, so the
                // wrapper that was lent out is emptied here: a caller that kept
                // it gets the removal sentence rather than freed memory.
                metaValue.Detach();
            }
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TransformTrampoline(nint transbuf, nint meta, nint buffer, uint type, nint data)
    {
        try
        {
            if (RegistrationOf(meta) is not { } registration)
            {
                ReportUnknownRegistration("transform_func");
                return 0;
            }

            if (registration.Transform is not { } transform)
            {
                return 0;
            }

            if (transbuf == 0 || buffer == 0)
            {
                throw new InvalidOperationException("GstMetaTransformFunction passed no buffer.");
            }

            using Gst.Buffer transbufValue = Gst.Buffer.Borrow(transbuf);
            using Gst.Buffer bufferValue = Gst.Buffer.Borrow(buffer);
            Gst.Meta metaValue = Gst.Meta.FromNative(meta)
                ?? throw new InvalidOperationException("GstMetaTransformFunction passed no meta.");
            return transform(transbufValue, metaValue, bufferValue, new Gst.GLib.Quark(type), data) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SerializeTrampoline(nint meta, nint data, byte* version)
    {
        try
        {
            if (RegistrationOf(meta) is not { } registration)
            {
                ReportUnknownRegistration("serialize_func");
                return 0;
            }

            if (registration.Serialize is not { } serialize)
            {
                return 0;
            }

            Gst.Meta metaValue = Gst.Meta.FromNative(meta)
                ?? throw new InvalidOperationException("GstMetaSerializeFunction passed no meta.");
            Gst.ByteArrayInterface dataValue = Gst.ByteArrayInterface.FromNative(data)
                ?? throw new InvalidOperationException("GstMetaSerializeFunction passed no data.");
            return serialize(metaValue, dataValue, ref *version) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint DeserializeTrampoline(nint info, nint buffer, byte* data, nuint size, byte version)
    {
        try
        {
            if (MetaAuthorRegistry.Find(info) is not { } registration)
            {
                ReportUnknownRegistration("deserialize_func");
                return 0;
            }

            if (registration.Deserialize is not { } deserialize)
            {
                return 0;
            }

            if (buffer == 0)
            {
                throw new InvalidOperationException("GstMetaDeserializeFunction passed no buffer.");
            }

            using Gst.Buffer bufferValue = Gst.Buffer.Borrow(buffer);
            Gst.MetaInfo infoValue = Gst.MetaInfo.FromNative(info)
                ?? throw new InvalidOperationException("GstMetaDeserializeFunction passed no info.");
            ReadOnlySpan<byte> dataValue = data == null
                ? default
                : new ReadOnlySpan<byte>(data, checked((int)size));

            // The item the delegate added belongs to the buffer, not to the
            // wrapper that is disposed here, so the answer stays valid.
            return deserialize(infoValue, bufferValue, dataValue, version) is { } added
                ? added.RequireHandle()
                : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ClearTrampoline(nint buffer, nint meta)
    {
        try
        {
            if (RegistrationOf(meta) is not { } registration)
            {
                ReportUnknownRegistration("clear_func");
                return;
            }

            if (registration.Clear is not { } clear)
            {
                return;
            }

            if (buffer == 0)
            {
                throw new InvalidOperationException("GstMetaClearFunction passed no buffer.");
            }

            using Gst.Buffer bufferValue = Gst.Buffer.Borrow(buffer);
            Gst.Meta metaValue = Gst.Meta.FromNative(meta)
                ?? throw new InvalidOperationException("GstMetaClearFunction passed no meta.");
            clear(bufferValue, metaValue);
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
        }
    }

    /// <summary>
    /// Allocates an implementation block whose function fields are still
    /// writable.
    /// </summary>
    /// <param name="api">The metadata API the implementation implements.</param>
    /// <param name="impl">The name of the implementation.</param>
    /// <param name="size">How many bytes one item takes.</param>
    /// <returns>
    /// The block, which the caller owns until it is registered, or <c>0</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The gir marks <c>gst_meta_info_new</c> <c>introspectable="0"</c>, so the
    /// generator never sees it and no overlay can bring it back; its sibling
    /// <c>gst_meta_info_register</c> takes the block <c>transfer full</c> and
    /// would be generated as a method of an opaque record that managed code
    /// cannot construct, which is why both are on the skip and hand bound lists
    /// of <c>girs/overlays/fixups.json</c> and imported here instead. Together
    /// they are a superset of <c>gst_meta_register</c>, which is hand bound as
    /// well and which nothing imports: the three callbacks it takes are the
    /// three oldest of the six this pair can write.
    /// </para>
    /// <para>
    /// Every signature is blittable: a <c>GType</c> is an <see cref="nuint"/>
    /// and a <c>gsize</c> is one as well.
    /// </para>
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_meta_info_new")]
    private static partial nint MetaInfoNew(nuint api, byte* impl, nuint size);

    /// <summary>
    /// Takes an implementation block over and makes it visible to the rest of
    /// the process.
    /// </summary>
    /// <param name="info">The block, which the call takes over.</param>
    /// <returns>
    /// The block, which is the pointer that was passed in and is now immutable,
    /// or <c>0</c> when the registration was refused and the block was freed.
    /// </returns>
    [LibraryImport("Gst", EntryPoint = "gst_meta_info_register")]
    private static partial nint MetaInfoRegister(nint info);
}
