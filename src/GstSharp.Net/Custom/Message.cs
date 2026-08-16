using Gst.GLib;
using Gst.Interop;

namespace Gst;

public sealed unsafe partial class Message
{
    /// <summary>
    /// Gets the object that posted the message, or <see langword="null"/> when
    /// the message has no source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source is what the type of a message is usually dispatched on:
    /// <c>message.Src is Gst.Element</c> and a cast to the wrapper of a
    /// concrete element type both go through the type registry, so an element
    /// that a plugin implements arrives as the closest registered base type.
    /// </para>
    /// <para>
    /// Following the ownership policy of the binding, the returned wrapper owns
    /// a reference of its own and stays valid after this message is disposed.
    /// It is the same instance that every other lookup of the object hands out,
    /// so disposing it releases the reference for all of them; leave it to the
    /// garbage collector unless this code created the object.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.Object? Src
    {
        get
        {
            Gst.Object? source = Gst.GObject.Object.FromNative<Gst.Object>(
                ((MessageRaw*)Handle)->Src,
                Gst.Interop.Transfer.None);

            // The source is read out of the message and referenced through it,
            // so the message has to outlive the lookup. Reading Handle is the
            // last use of this wrapper, and without this the collector may
            // finalize it — releasing the message, and with it the source —
            // while the wrapper of the source is still being built.
            GC.KeepAlive(this);
            return source;
        }
    }

    /// <summary>
    /// Gets the name of the object that posted the message, or
    /// <see langword="null"/> when the message has no source.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public string? SourceName => Src?.Name;

    /// <summary>
    /// Reads the error and the debug string of a
    /// <see cref="MessageType.Error"/> message.
    /// </summary>
    /// <returns>
    /// The error, and the debug string of the element that reported it, which
    /// is <see langword="null"/> when the element did not provide one.
    /// </returns>
    /// <remarks>
    /// The error is returned rather than thrown: an error message is a value
    /// that the application inspects, and a bus loop that reads one is not in
    /// an exceptional state. Both the <c>GError</c> and the debug string are
    /// copies that this call owns and releases before it returns.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The message is not a <see cref="MessageType.Error"/> message.
    /// </exception>
    public (GException Error, string? Debug) ParseError()
    {
        EnsureType(MessageType.Error, "gst_message_parse_error");

        nint error = nint.Zero;
        nint debug = nint.Zero;
        MessageNative.ParseError(Handle, &error, &debug);
        return Take(error, debug);
    }

    /// <summary>
    /// Reads the warning and the debug string of a
    /// <see cref="MessageType.Warning"/> message.
    /// </summary>
    /// <returns>
    /// The warning, and the debug string of the element that reported it, which
    /// is <see langword="null"/> when the element did not provide one.
    /// </returns>
    /// <remarks>
    /// See <see cref="ParseError"/>: the warning is returned rather than
    /// thrown, and this call owns and releases both copies.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The message is not a <see cref="MessageType.Warning"/> message.
    /// </exception>
    public (GException Error, string? Debug) ParseWarning()
    {
        EnsureType(MessageType.Warning, "gst_message_parse_warning");

        nint error = nint.Zero;
        nint debug = nint.Zero;
        MessageNative.ParseWarning(Handle, &error, &debug);
        return Take(error, debug);
    }

    /// <summary>
    /// Reads the informational message and the debug string of a
    /// <see cref="MessageType.Info"/> message.
    /// </summary>
    /// <returns>
    /// The report, and the debug string of the element that posted it, which is
    /// <see langword="null"/> when the element did not provide one.
    /// </returns>
    /// <remarks>
    /// See <see cref="ParseError"/>: the report is returned rather than thrown,
    /// and this call owns and releases both copies. An info message carries a
    /// <c>GError</c> like an error message does, which is a quirk of the C API
    /// rather than a sign that something went wrong — it is how an element says
    /// something worth reporting in the shape the bus already has.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The message is not a <see cref="MessageType.Info"/> message.
    /// </exception>
    public (GException Error, string? Debug) ParseInfo()
    {
        EnsureType(MessageType.Info, "gst_message_parse_info");

        nint error = nint.Zero;
        nint debug = nint.Zero;
        MessageNative.ParseInfo(Handle, &error, &debug);
        return Take(error, debug);
    }

    /// <summary>
    /// Converts the two out parameters of a parse call and releases them.
    /// </summary>
    /// <param name="error">The <c>GError</c> copy, or <c>0</c>.</param>
    /// <param name="debug">The debug string, or <c>0</c>.</param>
    /// <returns>The managed error and debug string.</returns>
    private static (GException Error, string? Debug) Take(nint error, nint debug)
    {
        // The debug string is read first: it is freed the same way whether or
        // not the message carried an error.
        string? debugText = GMarshal.PtrToStringUtf8AndFree(debug);
        return (TakeError(error), debugText);
    }

    /// <summary>
    /// Turns a <c>GError</c> that the caller owns into a
    /// <see cref="GException"/> and releases it.
    /// </summary>
    /// <param name="error">The error, or <c>0</c>.</param>
    /// <returns>The managed error.</returns>
    private static GException TakeError(nint error)
    {
        if (error == nint.Zero)
        {
            // gst_message_parse_error only leaves the pointer alone when the
            // message does not carry an error at all, which the type check
            // rules out. Reporting something is still better than a null.
            return new GException("The message carried no error.");
        }

        GErrorNative* native = (GErrorNative*)error;
        Quark domain = native->Domain;
        int code = native->Code;
        string message = GMarshal.PtrToStringUtf8(native->MessagePointer) ?? "The operation failed.";

        // Everything has been copied out of the GError, so the copy that the
        // parse call handed over goes back here rather than at some later
        // point: this runs once per error message of every bus loop.
        MessageNative.ErrorFree(error);

        return new GException(domain, code, message);
    }

    /// <summary>
    /// Rejects a message of the wrong type before it reaches native code.
    /// </summary>
    /// <param name="expected">The type the parse call needs.</param>
    /// <param name="function">The native function that is about to be called.</param>
    /// <exception cref="InvalidOperationException">
    /// The message is of a different type.
    /// </exception>
    private void EnsureType(MessageType expected, string function)
    {
        MessageType actual = Type;
        if (actual != expected)
        {
            // Native code would only log a g_return_if_fail here and leave the
            // out parameters untouched, which turns a misdispatched message
            // into an empty error instead of a failure.
            throw new InvalidOperationException(
                $"{function} needs a {expected} message, but this message is {actual}.");
        }
    }
}
