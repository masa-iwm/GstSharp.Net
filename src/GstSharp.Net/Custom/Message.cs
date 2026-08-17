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
    /// Creates a copy of the message that carries the same type, source,
    /// timestamp, sequence number and payload.
    /// </summary>
    /// <returns>
    /// The copy, which the caller owns and has to dispose, or
    /// <see langword="null"/> when the message could not be copied.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_message_copy</c>. It is hand written for the reason
    /// <see cref="Gst.Buffer.Copy"/> is: the gir marks the function
    /// <c>introspectable="0"</c>, so the generator skips it and no overlay can
    /// bring it back. For C consumers it is a static inline function of
    /// <c>gst/gstmessage.h</c> that forwards to <c>gst_mini_object_copy</c>,
    /// and the library exports it as a real symbol nonetheless, which is what
    /// makes it importable; see <c>MessageNative</c>.
    /// </para>
    /// <para>
    /// <b>This is how a message outlives the handler it arrived in.</b> The
    /// wrapper that a sync handler, a <c>message</c> event or a
    /// <c>sync-message</c> event is handed is disposed when the handler
    /// returns, so a message that has to be looked at later — queued for the
    /// user interface thread, kept for a diagnostic — is copied here and the
    /// copy is what is kept. The copy is a message of its own with a reference
    /// count of one; the structure it carries is copied with it, and the source
    /// object is referenced rather than duplicated.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.Message? Copy()
    {
        nint nativeResult = MessageNative.Copy(Handle);

        // The message has to outlive the call that reads it: reading Handle is
        // the last use of this wrapper, and a finalizer that runs in between
        // would release the message being copied.
        GC.KeepAlive(this);
        return Gst.Message.FromNative(nativeResult, Gst.Interop.Transfer.Full);
    }

    /// <summary>
    /// Creates a message that carries whatever an application wants to send
    /// through the bus of its own pipeline, taking the payload over.
    /// </summary>
    /// <param name="src">
    /// The object to post the message as, normally the pipeline. It may be
    /// <see langword="null"/>, which produces a message with no source.
    /// </param>
    /// <param name="structure">
    /// The payload, whose name is what the receiver dispatches on. The call
    /// consumes it: <paramref name="structure"/> is disposed when this method
    /// returns, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <returns>The message, which the caller owns and has to dispose.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_message_new_application</c>. Together with
    /// <see cref="Element.PostMessage"/> it is how something that happens
    /// outside the pipeline — a key press, a timer, a request from a user
    /// interface — reaches the loop that reads the bus, on the thread that
    /// loop runs on and in the order the pipeline's own messages arrive in.
    /// That is the shape <c>gst-launch-1.0</c> uses to turn Ctrl+C into a
    /// shutdown, and <c>samples/GstLaunch</c> uses it the same way.
    /// </para>
    /// <code>
    /// using Structure structure = Structure.NewEmpty("GstLaunchInterrupt");
    /// using (Message message = Message.NewApplication(pipeline, structure))
    /// {
    ///     pipeline.PostMessage(message);
    /// }
    /// </code>
    /// <para>
    /// The <c>structure</c> parameter is <c>transfer-ownership="full"</c>, so
    /// this is written by hand along the lines of
    /// <see cref="BufferPool.SetConfig"/>: the call is handed a value of its
    /// own and the wrapper is disposed afterwards, which leaves the caller with
    /// exactly what the C call leaves it with.
    /// <see cref="Gst.GObject.Boxed.Dispose()"/> is idempotent, so a
    /// <c>using</c> declaration around the payload stays correct.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="structure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// <paramref name="structure"/> was disposed, or <paramref name="src"/> was.
    /// </exception>
    public static Gst.Message NewApplication(Gst.Object? src, Gst.Structure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        // Both handles are read before anything is copied, so that a disposed
        // wrapper throws before a copy exists that nobody would free.
        nint source = src?.Handle ?? nint.Zero;
        nint owned = structure.Handle;
        nuint boxedType = structure.BoxedType.Value;

        // The value the call consumes.
        nint copy = Gst.Interop.GObjectNative.BoxedCopy(boxedType, owned);

        nint message = MessageNative.NewApplication(source, copy);

        // The handles were read before the call, so nothing keeps either
        // wrapper alive across it on its own.
        GC.KeepAlive(src);

        // And the structure of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing.
        structure.Dispose();

        return Gst.Message.FromNative(message, Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException("gst_message_new_application returned no message.");
    }

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
    /// Reads the object, the property name and the new value of a
    /// <see cref="MessageType.PropertyNotify"/> message.
    /// </summary>
    /// <returns>
    /// The object whose property changed, the name of that property, and its
    /// new value. The value has to be disposed; it is empty —
    /// <see cref="Gst.GObject.Value.IsEmpty"/> is <see langword="true"/> — when
    /// the watch that posted the message was installed without values.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_message_parse_property_notify</c>, the reader of the
    /// messages that <see cref="Element.AddPropertyDeepNotifyWatch"/> posts. A
    /// watch turns the <c>deep-notify</c> signal of a bin, which is emitted on
    /// whichever thread wrote the property, into bus messages that the
    /// application reads on its own thread — which is what makes it, rather
    /// than the signal, the way to watch properties of a running pipeline.
    /// </para>
    /// <para>
    /// The object is the source of the message, so this returns what
    /// <see cref="Src"/> returns and follows the same ownership rule: an
    /// interned wrapper that is normally not disposed. The name and the value
    /// are borrowed from the structure of the message in C; here the name is a
    /// managed string and the value is a copy that the caller owns, so both
    /// outlive the message.
    /// </para>
    /// <para>
    /// A watch installed with <c>includeValue</c> set to
    /// <see langword="false"/> posts messages without a value, which is the
    /// empty result rather than an error: reading a property costs a lock on
    /// the element, and an application that only wants to know <em>that</em>
    /// something changed does not pay it.
    /// </para>
    /// <code>
    /// (Gst.Object? source, string name, Gst.GObject.Value value) = message.ParsePropertyNotify();
    /// using (value)
    /// {
    ///     Console.WriteLine($"{source?.Name}: {name} = {Global.ValueSerialize(value)}");
    /// }
    /// </code>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The message is not a <see cref="MessageType.PropertyNotify"/> message.
    /// </exception>
    public (Gst.Object? Source, string PropertyName, Gst.GObject.Value Value) ParsePropertyNotify()
    {
        EnsureType(MessageType.PropertyNotify, "gst_message_parse_property_notify");

        nint source = nint.Zero;
        nint name = nint.Zero;
        nint value = nint.Zero;
        MessageNative.ParsePropertyNotify(Handle, &source, &name, &value);

        Gst.Object? owner = Gst.GObject.Object.FromNative<Gst.Object>(source, Gst.Interop.Transfer.None);

        // Both pointers belong to the structure of the message, so the string
        // is copied and the value is copied, while the message is still alive.
        string propertyName = GMarshal.PtrToStringUtf8(name) ?? string.Empty;
        Gst.GObject.Value copy = Gst.GObject.Value.CopyFrom(value);

        GC.KeepAlive(this);
        return (owner, propertyName, copy);
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
