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

    /// <summary>The <c>gst_query_new_custom</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_query_new_custom")]
    private static partial nint GstQueryNewCustom(int type, nint structure);
}
