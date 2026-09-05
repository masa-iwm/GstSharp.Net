using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GLib;
using Gst.GObject;
using Gst.Interop;

namespace Gst;

/// <summary>
/// Declares that a managed element implements <c>GstURIHandler</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="For{TSelf}"/> reads the two static answers off the type and pins
/// the protocol list; the entry it returns goes into
/// <c>SubclassOptions.Interfaces</c>, and the registration attaches the
/// interface and fills its vtable. There is no other way in: GObject refuses to
/// attach an interface once the class of the type is being initialised, so a
/// type that was defined without this is not a URI handler and never becomes
/// one.
/// </para>
/// </remarks>
public static unsafe class URIHandlerImplementation
{
    /// <summary>
    /// The message of the error the runtime writes when a refusal left none.
    /// </summary>
    private const string RefusedFormat = "\"{0}\" refused the URI \"{1}\".";

    private static GType _interfaceType;

    /// <summary>
    /// Declares that <typeparamref name="TSelf"/> implements
    /// <c>GstURIHandler</c>, for the <c>SubclassOptions</c> of its
    /// registration.
    /// </summary>
    /// <typeparam name="TSelf">The managed element type.</typeparam>
    /// <returns>The entry to put into <c>SubclassOptions.Interfaces</c>.</returns>
    /// <remarks>
    /// <para>
    /// The type does not exist yet when this runs - it is being defined - so
    /// nothing here touches a <c>GType</c> of it. What it does is validate the
    /// two static answers and copy the protocol list into unmanaged memory that
    /// is <b>never released</b>: <c>gst_uri_handler_get_protocols</c> returns
    /// the array to its callers as it is, so it has to stay valid for as long
    /// as the process can ask, and the type itself is equally permanent.
    /// </para>
    /// <para>
    /// <b>One pin per type for the lifetime of the process.</b> The vector is
    /// cached per <typeparamref name="TSelf"/> and the first call is the one
    /// that wins, so asking twice for one type costs nothing beyond the
    /// validation - which does run every time, so a declaration that is wrong
    /// is refused however often it is asked for.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The type answers <see cref="URIType.Unknown"/>, or its protocol list is
    /// null, empty, or holds an empty entry.
    /// </exception>
    public static InterfaceImplementation For<TSelf>()
        where TSelf : Element, IURIHandlerImplementation, IManagedSubclass<TSelf>
    {
        URIType uriType = TSelf.UriType;

        // GST_URI_TYPE_IS_VALID: gst_element_register refuses a handler that
        // says neither, and gst_element_make_from_uri would never pick it.
        if (uriType is not (URIType.Src or URIType.Sink))
        {
            throw new ArgumentException(
                $"{typeof(TSelf)} answers {uriType} for UriType. A URI handler is either a source or a sink.",
                nameof(TSelf));
        }

        IReadOnlyList<string> protocols = TSelf.Protocols
            ?? throw new ArgumentException($"{typeof(TSelf)} answers null for Protocols.", nameof(TSelf));

        if (protocols.Count == 0)
        {
            throw new ArgumentException(
                $"{typeof(TSelf)} answers an empty list for Protocols. A URI handler handles at least one "
                + "protocol.",
                nameof(TSelf));
        }

        foreach (string protocol in protocols)
        {
            if (string.IsNullOrEmpty(protocol))
            {
                throw new ArgumentException(
                    $"The Protocols of {typeof(TSelf)} hold an empty entry.",
                    nameof(TSelf));
            }
        }

        return new UriHandler(InterfaceType, uriType, ProtocolPin<TSelf>.Vector(protocols), typeof(TSelf));
    }

    /// <summary>Gets the type of <c>GstURIHandler</c>, resolved once.</summary>
    private static GType InterfaceType
    {
        get
        {
            GType cached = _interfaceType;
            if (!cached.IsValid)
            {
                cached = new GType(GstNative.UriHandlerGetType());
                _interfaceType = cached;
            }

            return cached;
        }
    }

    /// <summary>
    /// Copies a protocol list into a <c>NULL</c> terminated vector that is
    /// never freed.
    /// </summary>
    /// <param name="protocols">The protocols, none of them empty.</param>
    /// <returns>The vector, owned by nobody and released never.</returns>
    private static byte** Pin(IReadOnlyList<string> protocols)
    {
        byte** vector = (byte**)NativeMemory.AllocZeroed((nuint)protocols.Count + 1, (nuint)sizeof(byte*));

        for (int i = 0; i < protocols.Count; i++)
        {
            int length = System.Text.Encoding.UTF8.GetByteCount(protocols[i]);
            byte* text = (byte*)NativeMemory.AllocZeroed((nuint)length + 1, 1);
            System.Text.Encoding.UTF8.GetBytes(protocols[i], new Span<byte>(text, length));
            vector[i] = text;
        }

        return vector;
    }

    /// <summary>
    /// The protocol vector of one type, pinned once.
    /// </summary>
    /// <typeparam name="TSelf">The managed element type it belongs to.</typeparam>
    /// <remarks>
    /// A static field of a generic type is one field per type argument, which
    /// is the whole cache: no dictionary, no key comparison and no reflection,
    /// so it costs nothing under ahead of time compilation. The first caller
    /// wins; one that raced it frees the copy it made, which no slot has been
    /// handed.
    /// </remarks>
    private static class ProtocolPin<TSelf>
    {
        private static nint _vector;

        /// <summary>Answers the pinned vector, pinning it on the first call.</summary>
        /// <param name="protocols">The protocols, already validated.</param>
        /// <returns>The vector, owned by nobody and released never.</returns>
        internal static byte** Vector(IReadOnlyList<string> protocols)
        {
            nint pinned = Volatile.Read(ref _vector);
            if (pinned != nint.Zero)
            {
                return (byte**)pinned;
            }

            byte** made = Pin(protocols);
            nint raced = Interlocked.CompareExchange(ref _vector, (nint)made, nint.Zero);

            if (raced == nint.Zero)
            {
                return made;
            }

            Release(made);
            return (byte**)raced;
        }
    }

    /// <summary>
    /// Frees a vector no slot was ever handed.
    /// </summary>
    /// <param name="vector">The vector to release, with its strings.</param>
    private static void Release(byte** vector)
    {
        for (int i = 0; vector[i] is not null; i++)
        {
            NativeMemory.Free(vector[i]);
        }

        NativeMemory.Free(vector);
    }

    /// <summary>
    /// Returns the implementation a type-keyed slot was called for.
    /// </summary>
    /// <param name="type">The type the slot was handed.</param>
    /// <returns>The implementation, or null when the type is not a managed handler.</returns>
    private static UriHandler? Lookup(nuint type) =>
        SubclassRegistry.TryGetInterface(new GType(type), InterfaceType, out InterfaceImplementation? found)
            ? found as UriHandler
            : null;

    /// <summary>
    /// Names the type whose declaration attached the interface to an instance.
    /// </summary>
    /// <param name="handler">The native instance.</param>
    /// <returns>The managed type the declaration was made for, or a placeholder.</returns>
    private static string DeclaredFor(nint handler)
    {
        if (handler == nint.Zero)
        {
            return "another type";
        }

        UriHandler? implementation = Lookup(TypeRegistry.GetInstanceType(handler).Value);
        return implementation is null ? "another type" : implementation.DeclaredFor.ToString();
    }

    /// <summary>
    /// Says why an instance cannot answer for a URI.
    /// </summary>
    /// <param name="handler">The native instance.</param>
    /// <param name="wrapper">The wrapper, which may be null.</param>
    /// <returns>The reason, ready to be put into a message.</returns>
    /// <remarks>
    /// The two cases are not the same mistake and must not read as if they
    /// were. No wrapper at all is the window of §5.4 - a disposed wrapper, or
    /// an instance of the type being constructed on this thread. A wrapper that
    /// is not an <see cref="IURIHandlerImplementation"/> is a misconfiguration:
    /// the declaration of one type was put into the registration of another, so
    /// the interface hangs on a type that answers none of it.
    /// </remarks>
    private static string ReasonFor(nint handler, Gst.GObject.Object? wrapper) =>
        wrapper is null
            ? string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "\"{0}\" has no managed instance to answer for it.",
                NameOf(handler))
            : string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "\"{0}\" does not implement IURIHandlerImplementation: the GstURIHandler declaration in its "
                    + "registration was made for {1}.",
                NameOf(handler),
                DeclaredFor(handler));

    /// <summary>Names a type for an error message, without assuming a wrapper.</summary>
    /// <param name="handler">The native instance.</param>
    /// <returns>The name of its type, or a placeholder.</returns>
    private static string NameOf(nint handler)
    {
        if (handler == nint.Zero)
        {
            return "a URI handler";
        }

        GType type = TypeRegistry.GetInstanceType(handler);
        return type.IsValid ? type.Name : "a URI handler";
    }

    /// <summary>
    /// Writes the error a refusal has to carry, unless one is there already.
    /// </summary>
    /// <param name="error">The <c>GError**</c> the caller passed, which may be null.</param>
    /// <param name="failure">What the managed side reported, or null.</param>
    /// <param name="fallback">The message of the synthesised error.</param>
    /// <remarks>
    /// <c>gst_uri_handler_set_uri</c> synthesises nothing of its own and
    /// <c>gst_element_make_from_uri</c> reads the error of every candidate that
    /// refused, so a refusal without an error is a crash waiting in the
    /// autoplug path. An error whose domain or message GLib would reject counts
    /// as none.
    /// </remarks>
    private static void WriteError(nint* error, GException? failure, string fallback)
    {
        if (error is null || *error != nint.Zero)
        {
            return;
        }

        uint domain = URIErrorExtensions.Quark().Value;
        int code = (int)URIError.BadUri;
        string message = fallback;

        // The message and the domain are taken separately: a plain
        // GException carries a reason and no domain, and losing the reason
        // because of that would be the worst of both. What GLib would refuse -
        // an empty message, or one with an embedded null - counts as no
        // message at all, because g_error_new_literal answers NULL for it and
        // a null error is the crash this method exists to prevent.
        if (failure is { } reported
            && !string.IsNullOrEmpty(reported.Message)
            && !reported.Message.Contains('\0', StringComparison.Ordinal))
        {
            message = reported.Message;

            if (reported.Domain.Value != 0)
            {
                domain = reported.Domain.Value;
                code = reported.Code;
            }

            // A domain the handler set without a usable message is dropped with
            // the message: g_error_new_literal answers NULL for an empty one, so
            // there would be nothing left to carry the domain, and the
            // synthesised GST_URI_ERROR at least says what happened.
        }

        if (message.Contains('\0', StringComparison.Ordinal))
        {
            message = "The URI was refused.";
        }

        nint text = GMarshal.StringToUtf8Ptr(message);
        try
        {
            *error = GLibNative.ErrorNewLiteral(domain, code, (byte*)text);
        }
        finally
        {
            GMarshal.Free(text);
        }
    }

    /// <summary>
    /// The <c>get_type</c> slot, which GStreamer asks about the type while the
    /// element is registered.
    /// </summary>
    /// <param name="type">The type being asked about.</param>
    /// <returns>What the type answered when it was defined.</returns>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetUriTypeTrampoline(nuint type)
    {
        try
        {
            return (int)(Lookup(type)?.UriType ?? URIType.Unknown);
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            return (int)URIType.Unknown;
        }
    }

    /// <summary>
    /// The <c>get_protocols</c> slot, which hands out the pinned vector.
    /// </summary>
    /// <param name="type">The type being asked about.</param>
    /// <returns>The vector, which the caller neither frees nor copies.</returns>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte** GetProtocolsTrampoline(nuint type)
    {
        try
        {
            UriHandler? implementation = Lookup(type);
            return implementation is null ? null : implementation.Protocols;
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            return null;
        }
    }

    /// <summary>The <c>get_uri</c> slot.</summary>
    /// <param name="handler">The instance.</param>
    /// <returns>The URI in memory the caller releases with <c>g_free</c>, or null.</returns>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nint GetUriTrampoline(nint handler)
    {
        try
        {
            Gst.GObject.Object? wrapper = Gst.GObject.Object.TryGetOrFabricate(handler);

            if (wrapper is not IURIHandlerImplementation managed)
            {
                GLibNative.Warn("GStreamer", "gst_uri_handler_get_uri: " + ReasonFor(handler, wrapper));
                return nint.Zero;
            }

            // g_malloc0 is the allocator g_strdup uses, so the caller's g_free
            // matches; the string is copied out of managed memory either way.
            return GMarshal.StringToUtf8Ptr(managed.GetUri());
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            return nint.Zero;
        }
    }

    /// <summary>The <c>set_uri</c> slot.</summary>
    /// <param name="handler">The instance.</param>
    /// <param name="uri">The URI, already checked against the protocols.</param>
    /// <param name="error">Where a refusal is reported, which may be null.</param>
    /// <returns>Whether the URI was accepted.</returns>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetUriTrampoline(nint handler, byte* uri, nint* error)
    {
        try
        {
            string uriValue = GMarshal.PtrToStringUtf8((nint)uri) ?? string.Empty;

            Gst.GObject.Object? wrapper = Gst.GObject.Object.TryGetOrFabricate(handler);

            if (wrapper is not IURIHandlerImplementation managed)
            {
                WriteError(error, null, ReasonFor(handler, wrapper));
                return 0;
            }

            string refused = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                RefusedFormat,
                NameOf(handler),
                uriValue);

            GException? failure = null;
            bool accepted;

            try
            {
                accepted = managed.SetUri(uriValue, out failure);
            }
            catch (Exception exception)
            {
                ExceptionTrap.Report(exception);
                WriteError(error, null, refused);
                return 0;
            }

            if (accepted)
            {
                return 1;
            }

            WriteError(error, failure, refused);
            return 0;
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            WriteError(error, null, "The URI was refused.");
            return 0;
        }
    }

    /// <summary>
    /// The implementation of <c>GstURIHandler</c> for one managed type.
    /// </summary>
    /// <remarks>
    /// The object holds what the two type-keyed slots answer. They find it
    /// through the registration table rather than through this object, because
    /// they are handed a <c>GType</c> and nothing else.
    /// </remarks>
    private sealed class UriHandler : InterfaceImplementation
    {
        private readonly byte** _protocols;

        /// <summary>Records what one type answers as a URI handler.</summary>
        /// <param name="interfaceType">The type of <c>GstURIHandler</c>.</param>
        /// <param name="uriType">Whether the type is a source or a sink.</param>
        /// <param name="protocols">The pinned protocol vector.</param>
        /// <param name="declaredFor">The managed type the declaration was made for.</param>
        internal UriHandler(GType interfaceType, URIType uriType, byte** protocols, Type declaredFor)
            : base(interfaceType)
        {
            UriType = uriType;
            _protocols = protocols;
            DeclaredFor = declaredFor;
        }

        /// <summary>Gets whether the type is a source or a sink.</summary>
        internal URIType UriType { get; }

        /// <summary>
        /// Gets the managed type <c>For</c> was called on, which is the one
        /// whose static answers this carries.
        /// </summary>
        internal Type DeclaredFor { get; }

        /// <summary>Gets the pinned protocol vector.</summary>
        internal byte** Protocols => _protocols;

        /// <inheritdoc/>
        internal override void InitializeVTable(void* iface, GType instanceType)
        {
            _ = instanceType;

            byte* vtable = (byte*)iface;
            *(nint*)(vtable + GstURIHandlerInterfaceRaw.GetUriTypeOffset) =
                (nint)(delegate* unmanaged[Cdecl]<nuint, int>)&GetUriTypeTrampoline;
            *(nint*)(vtable + GstURIHandlerInterfaceRaw.GetProtocolsOffset) =
                (nint)(delegate* unmanaged[Cdecl]<nuint, byte**>)&GetProtocolsTrampoline;
            *(nint*)(vtable + GstURIHandlerInterfaceRaw.GetUriOffset) =
                (nint)(delegate* unmanaged[Cdecl]<nint, nint>)&GetUriTrampoline;
            *(nint*)(vtable + GstURIHandlerInterfaceRaw.SetUriOffset) =
                (nint)(delegate* unmanaged[Cdecl]<nint, byte*, nint*, int>)&SetUriTrampoline;
        }
    }
}
