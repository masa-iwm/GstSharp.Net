using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The factory of a custom query, the one <c>gst_query_new_*</c> whose payload
/// the call takes over.
/// </content>
/// <remarks>
/// Its <c>structure</c> parameter is <c>transfer-ownership="full"</c>, and the
/// generator emits no call that takes a wrapper over, because handing the only
/// value of a wrapper to the library would let both of them release it. See
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#calls-that-consume-their-argument">Calls that consume their argument</see>.
/// Without it <see cref="QueryType.Custom"/> has no constructor, and a query
/// that an element and an application agree on between themselves cannot be
/// asked at all.
/// </remarks>
public sealed partial class Query
{
    /// <summary>
    /// Creates a query of a custom type, carrying a payload of the
    /// application's own and taking that payload over.
    /// </summary>
    /// <param name="type">
    /// What the query is, normally <see cref="QueryType.Custom"/>. A type built
    /// with the <c>GST_QUERY_MAKE_TYPE</c> macro of C works here as well.
    /// </param>
    /// <param name="structure">
    /// The payload, whose name is what the element that answers dispatches on,
    /// and whose fields the answer is written into. The call consumes it:
    /// <paramref name="structure"/> is disposed when this method returns, and
    /// using it afterwards throws <see cref="ObjectDisposedException"/>. It may
    /// be <see langword="null"/>, which produces a query with no payload and
    /// leaves nothing to consume.
    /// </param>
    /// <returns>The query, which the caller owns and has to dispose.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_query_new_custom</c>. A query is asked with
    /// <see cref="Element.Query"/> or <see cref="Pad.Query"/> — neither of which
    /// consumes it, unlike an event — and the answer is read back out of the
    /// structure the query carries. <see cref="GetStructure"/> hands out a copy
    /// of that structure, so the answer is read from the copy and the query is
    /// disposed as usual.
    /// </para>
    /// <code>
    /// using Structure request = Structure.NewEmpty("GstSharpAsk");
    /// using Query query = Query.NewCustom(QueryType.Custom, request);
    ///
    /// if (element.Query(query))
    /// {
    ///     using Structure? answer = query.GetStructure();
    /// }
    /// </code>
    /// <para>
    /// The payload is written by whoever answers the query, which is why the
    /// call takes it over rather than borrowing it: the structure belongs to the
    /// query from here on. The wrapper is handed a copy to give away and is
    /// disposed afterwards, along the lines of
    /// <see cref="Message.NewApplication"/>, which leaves the caller with
    /// exactly what the C call leaves it with.
    /// <see cref="Gst.GObject.Boxed.Dispose()"/> is idempotent, so a
    /// <c>using</c> declaration around the payload stays correct.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// <paramref name="structure"/> was disposed.
    /// </exception>
    public static Gst.Query NewCustom(Gst.QueryType type, Gst.Structure? structure)
    {
        nint copy = nint.Zero;

        if (structure is not null)
        {
            // The handle is read before anything is copied, so that a disposed
            // wrapper throws before a copy exists that nobody would free.
            nint owned = structure.Handle;
            nuint boxedType = structure.BoxedType.Value;

            // The value the call consumes.
            copy = GObjectNative.BoxedCopy(boxedType, owned);
        }

        nint query = GstQueryNewCustom((int)type, copy);

        // And the structure of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing. A query without
        // a payload has nothing to consume.
        structure?.Dispose();

        return Gst.Query.FromNative(query, Transfer.Full)
            ?? throw new InvalidOperationException("gst_query_new_custom returned no query.");
    }

    /// <summary>
    /// Reads one entry of the allocator array of an allocation query.
    /// </summary>
    /// <param name="index">
    /// Which entry to read, below <see cref="GetNAllocationParams"/>.
    /// </param>
    /// <param name="allocator">
    /// The allocator of the entry, which may be <see langword="null"/> when the
    /// element that answered named none. The caller owns it and disposes it.
    /// </param>
    /// <param name="params">
    /// The allocation parameters of the entry. The binding allocates the
    /// storage; on return the caller owns it and disposes it.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_query_parse_nth_allocation_param</c>, which is hand
    /// written for its range behaviour. The C function returns <c>void</c> and
    /// leaves both of its out parameters exactly as it found them when
    /// <paramref name="index"/> is past the end of the array, so a caller has
    /// no way of telling an empty entry from one that was never read. The range
    /// is checked here instead and answered with an exception, which is the
    /// contract the hand written surface of the binding keeps.
    /// </para>
    /// <para>
    /// The parameters are storage the C function fills rather than a value it
    /// hands out, and the binding allocates it with
    /// <c>gst_allocation_params_new</c>: the library sizes and zeroes the
    /// record, and disposing the wrapper is the matching free.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not below <see cref="GetNAllocationParams"/>.
    /// </exception>
    public unsafe void ParseNthAllocationParam(uint index, out Gst.Allocator? allocator, out AllocationParams @params)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, GetNAllocationParams());

        nint handle = Handle;
        nint allocatorNative = 0;
        nint paramsNative = GstAllocationParamsNew();
        GstQueryParseNthAllocationParam(handle, index, &allocatorNative, paramsNative);
        GC.KeepAlive(this);

        allocator = Gst.GObject.Object.FromNative<Gst.Allocator>(allocatorNative, Transfer.Full);
        @params = AllocationParams.FromNative(paramsNative, Transfer.Full)
            ?? throw new InvalidOperationException("gst_allocation_params_new returned no value.");
    }

    /// <summary>
    /// Creates a copy of this query.
    /// </summary>
    /// <returns>
    /// The copy, which the caller owns, or <see langword="null"/> when the type
    /// of the object has no copy function.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_query_copy</c>, hand written for the reason
    /// <see cref="Gst.Buffer.Copy"/> is: the gir marks the function
    /// <c>introspectable="0"</c>, so the generator skips it and no overlay can
    /// bring it back. For C consumers it is a static inline function of
    /// <c>gst/gstquery.h</c>, and the entry point called here is the
    /// <c>gst_mini_object_copy</c> it forwards to, which the library exports as
    /// a real symbol. That call answers NULL for a type that installed no copy
    /// function, which a query never is; the nullable return states what the C
    /// promises rather than a narrower promise this binding cannot take back.
    /// </para>
    /// <para>
    /// The copy is a query of the same type carrying a copy of the structure of
    /// the original rather than the same one (<c>_gst_query_copy</c>,
    /// gstquery.c:204-217), which is what lets a query outlive the call it was
    /// handed to. It holds the only reference to itself and is writable as a
    /// mini object, so an answer may be written into the copy.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.Query? Copy()
    {
        nint nativeResult = GstNative.MiniObjectCopy(Handle);

        // The query has to outlive the call that reads it: reading Handle is
        // the last use of this wrapper, and a finalizer that runs in between
        // would release the query being copied.
        GC.KeepAlive(this);
        return Gst.Query.FromNative(nativeResult, Gst.Interop.Transfer.Full);
    }

    /// <summary>The <c>gst_query_new_custom</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_query_new_custom")]
    private static partial nint GstQueryNewCustom(int type, nint structure);

    /// <summary>The <c>gst_query_parse_nth_allocation_param</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_query_parse_nth_allocation_param")]
    private static unsafe partial void GstQueryParseNthAllocationParam(
        nint query,
        uint index,
        nint* allocator,
        nint @params);

    /// <summary>
    /// The <c>gst_allocation_params_new</c> entry point, which allocates the
    /// storage the parse above fills.
    /// </summary>
    /// <returns>A new, zeroed instance the caller owns.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_allocation_params_new")]
    private static partial nint GstAllocationParamsNew();
}
