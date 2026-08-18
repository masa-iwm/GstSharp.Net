using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The producer half of a promise: the call that answers it, whose reply the
/// call takes over.
/// </content>
/// <remarks>
/// The <c>s</c> parameter of <c>gst_promise_reply</c> is
/// <c>transfer-ownership="full"</c>, and the generator emits no call that takes
/// a wrapper over, because handing the only value of a wrapper to the library
/// would let both of them release it. See
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#calls-that-consume-their-argument">Calls that consume their argument</see>.
/// Without it a promise could be created, waited on, interrupted and read, and
/// never answered: <see cref="Interrupt"/> and <see cref="Expire"/> were the
/// only ways out of <see cref="PromiseResult.Pending"/>.
/// </remarks>
public sealed partial class Promise
{
    /// <summary>
    /// Answers this promise, taking the reply over and waking everything that
    /// waits on it.
    /// </summary>
    /// <param name="s">
    /// The reply. The call consumes it: <paramref name="s"/> is disposed when
    /// this method returns, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>. It may be
    /// <see langword="null"/>, which answers the promise with no reply at all
    /// and leaves nothing to consume.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_promise_reply</c>, the call the producer of a value makes.
    /// It moves the promise to <see cref="PromiseResult.Replied"/>, so a
    /// <see cref="Wait"/> that is blocked on it returns and
    /// <see cref="GetReply"/> hands out what was put in; a promise that the
    /// consumer has already interrupted takes the reply and keeps the
    /// interrupted result, because nobody is left to read it.
    /// </para>
    /// <para>
    /// The parameter keeps the name the C function gives it. It is the same
    /// structure the consumer reads back, which is how the WebRTC calls that a
    /// promise exists for — <c>create-offer</c>, <c>create-answer</c>,
    /// <c>set-local-description</c> — carry their result.
    /// </para>
    /// <code>
    /// using Promise promise = Promise.New();
    ///
    /// using (Structure reply = Structure.NewEmpty("GstSharpAnswer"))
    /// {
    ///     promise.Reply(reply);   // consumes the reply
    /// }
    ///
    /// if (promise.Wait() == PromiseResult.Replied)
    /// {
    ///     using Structure? answer = promise.GetReply();
    /// }
    /// </code>
    /// <para>
    /// The reply is taken over along the lines of
    /// <see cref="Message.NewApplication"/>: the call is handed a value of its
    /// own and the wrapper is disposed afterwards, which leaves the caller with
    /// exactly what the C call leaves it with. That also matches what
    /// <see cref="GetReply"/> does on the other side — it hands out a copy, so
    /// nothing is aliased in either direction.
    /// <see cref="Gst.GObject.Boxed.Dispose()"/> is idempotent, so a
    /// <c>using</c> declaration around the reply stays correct.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper was disposed, or <paramref name="s"/> was.
    /// </exception>
    public void Reply(Gst.Structure? s)
    {
        // Both handles are read before anything is copied, so that a disposed
        // wrapper throws before a copy exists that nobody would free.
        nint promise = Handle;
        nint copy = nint.Zero;

        if (s is not null)
        {
            nint owned = s.Handle;
            nuint boxedType = s.BoxedType.Value;

            // The value the call consumes.
            copy = GObjectNative.BoxedCopy(boxedType, owned);
        }

        GstPromiseReply(promise, copy);

        // The handle was read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        GC.KeepAlive(this);

        // And the structure of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing. A reply of
        // nothing has nothing to consume.
        s?.Dispose();
    }

    /// <summary>The <c>gst_promise_reply</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_promise_reply")]
    private static partial void GstPromiseReply(nint promise, nint s);
}
