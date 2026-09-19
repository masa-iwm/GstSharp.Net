namespace Gst.RtspServer;

/// <content>
/// The request of a context, lent rather than copied, so that a handler of one
/// of the <c>pre-*-request</c> signals can edit the request the server is about
/// to serve.
/// </content>
/// <remarks>
/// <para>
/// <see cref="GetRequest()"/> answers a copy of the message, which is the safe
/// shape and the useless one for a handler that wants to change what the server
/// reads: the C reads <c>ctx-&gt;request</c> itself once the handler has
/// returned - the body of a <c>GET_PARAMETER</c> request
/// (<c>rtsp-client.c:1636-1643</c>), the <c>Accept</c> header of a
/// <c>DESCRIBE</c> (<c>:3339-3351</c>), the <c>KeyMgmt</c> and
/// <c>Accept-Ranges</c> headers of a <c>SETUP</c> (<c>:2926-2984</c>), and the
/// <c>CSeq</c> and <c>Session</c> every response is initialised from - and
/// never looks at a copy. Editing the request in place is the native idiom:
/// the C does it to its own request as well (<c>:3101</c>).
/// </para>
/// </remarks>
public unsafe partial struct RTSPContext
{
    /// <summary>
    /// Lends the request of this context for the length of the handler that was
    /// handed the context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wrapper borrows: it holds no reference and no copy, so what it
    /// writes is written into the very message the server goes on to read, and
    /// disposing it frees nothing. Edits are therefore observable, which is the
    /// whole point of this member -
    /// <see cref="GetRequest()"/> answers a deep copy instead, and what a
    /// handler writes into that copy is read by nobody.
    /// </para>
    /// <para>
    /// The answer is <see langword="null"/> where the context carries no
    /// request, which is a legitimate state rather than a failure:
    /// <c>handle-response</c> always builds its context with a NULL request
    /// (<c>rtsp-client.c:4239-4281</c>), and so does a
    /// <c>send-message</c> the server originates through
    /// <c>gst_rtsp_client_send_message</c> outside a request
    /// (<c>:4883-4907</c>). Every <c>pre-*-request</c> signal, its
    /// <c>*-request</c> twin and <c>check-requirements</c> carry a request that
    /// is present and parsed.
    /// </para>
    /// <para>
    /// The borrow is valid only until the handler returns. The C pops the
    /// context and hands the request back to the watch at that point
    /// (<c>rtsp-client.c:4169-4176</c>), so the wrapper has to be disposed -
    /// <c>using</c> - before the handler returns, and neither it nor anything
    /// read out of it by reference may be stored.
    /// </para>
    /// <para>
    /// Never call this on a stored <see cref="RTSPContext"/>. A managed context
    /// is a value snapshot of the structure the emitter holds, so a copy kept
    /// past the emission carries a pointer that dangles, and no managed check
    /// can tell that apart from a live one. Copy out of the borrowed message
    /// whatever has to outlive the handler.
    /// </para>
    /// <para>
    /// <c>check-requirements</c> is the one signal where an edit is too late
    /// for the header it is about: the C collects the <c>Require</c> headers
    /// before it emits (<c>rtsp-client.c:3965</c>), so what the handler is
    /// asked about is what the request said on arrival.
    /// </para>
    /// <para>
    /// There is no response twin. The response of a context is unset by the
    /// time a <c>*-request</c> signal runs - the C sends it and unsets it
    /// before it emits (<c>rtsp-client.c:946</c>) - and one failure path of
    /// <c>OPTIONS</c> frees the stack response outright (<c>:3869</c>), so a
    /// borrowed response would be a pointer to freed storage on a path no
    /// handler can recognise. The message a handler may edit on the way out is
    /// the one <see cref="RTSPClient.SendingMessage"/> lends it.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The request of this context, borrowed for the length of the handler, or
    /// <see langword="null"/> where the context carries none.
    /// </returns>
    public readonly Gst.Rtsp.RTSPMessage? BorrowRequest() =>
        RequestPtr == nint.Zero ? null : Gst.Rtsp.RTSPMessage.Borrow(RequestPtr);
}
