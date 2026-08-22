using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Gst.Interop;

/// <summary>
/// Manual marshalling helpers for the string and memory conventions of GLib.
/// </summary>
/// <remarks>
/// The bindings never use the string marshalling of
/// <see cref="LibraryImportAttribute"/>: every entry point takes raw
/// <c>byte*</c> pointers and the conversion happens here, so that the ownership
/// of each buffer is explicit at the call site.
/// </remarks>
public static unsafe class GMarshal
{
    /// <summary>
    /// Recommended size, in bytes, of the buffer that is passed to
    /// <see cref="StackUtf8"/>.
    /// </summary>
    public const int StackBufferSize = 256;

    /// <summary>
    /// Encodes <paramref name="value"/> as null terminated UTF-8 for the
    /// duration of a single native call.
    /// </summary>
    /// <param name="value">The string to encode, may be <see langword="null"/>.</param>
    /// <param name="buffer">
    /// A stack allocated scratch buffer, normally
    /// <c>stackalloc byte[GMarshal.StackBufferSize]</c>. Strings that do not
    /// fit are encoded into a native allocation instead.
    /// </param>
    /// <returns>A scope that has to be disposed once the call has returned.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> contains a null character.
    /// </exception>
    public static Utf8Scope StackUtf8(string? value, Span<byte> buffer)
    {
        if (value is null)
        {
            return new Utf8Scope(null, owned: false);
        }

        ThrowIfEmbeddedNull(value);

        int length = Encoding.UTF8.GetByteCount(value);
        if (length < buffer.Length)
        {
            int written = Encoding.UTF8.GetBytes(value, buffer);
            buffer[written] = 0;
            return new Utf8Scope((byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer)), owned: false);
        }

        byte* native = (byte*)NativeMemory.Alloc((nuint)length + 1);
        int nativeWritten = Encoding.UTF8.GetBytes(value, new Span<byte>(native, length));
        native[nativeWritten] = 0;
        return new Utf8Scope(native, owned: true);
    }

    /// <summary>
    /// Copies <paramref name="value"/> into a null terminated UTF-8 buffer that
    /// was allocated with <c>g_malloc</c>, for native code that takes ownership
    /// of the string.
    /// </summary>
    /// <param name="value">The string to copy, may be <see langword="null"/>.</param>
    /// <returns>
    /// The new buffer, or <see cref="nint.Zero"/> when <paramref name="value"/>
    /// is <see langword="null"/>. The caller has to release it with
    /// <see cref="Free"/> unless native code took it over.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> contains a null character.
    /// </exception>
    public static nint StringToUtf8Ptr(string? value)
    {
        if (value is null)
        {
            return nint.Zero;
        }

        ThrowIfEmbeddedNull(value);

        int length = Encoding.UTF8.GetByteCount(value);
        nint buffer = GLibNative.Malloc0((nuint)length + 1);
        Encoding.UTF8.GetBytes(value, new Span<byte>((void*)buffer, length));
        return buffer;
    }

    /// <summary>
    /// Copies <paramref name="values"/> into a <c>NULL</c> terminated vector of
    /// UTF-8 strings for the duration of a single native call.
    /// </summary>
    /// <param name="values">The strings to copy, may be <see langword="null"/>.</param>
    /// <returns>
    /// A scope that has to be disposed once the call has returned, and whose
    /// <see cref="StrvScope.Pointer"/> is <see langword="null"/> when
    /// <paramref name="values"/> is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// An empty array is not a <see langword="null"/> one: it becomes a vector
    /// of one zeroed slot, which is an empty list and not the absence of a
    /// list, and several callees tell the two apart.
    /// </para>
    /// <para>
    /// The vector comes from <see cref="NativeMemory"/> and the strings from
    /// <c>g_malloc0</c>, and each is released by the allocator that made it.
    /// That split is only safe because nothing native takes either allocation
    /// over, which is the <c>transfer-ownership="none"</c> contract this helper
    /// exists for.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// One of <paramref name="values"/> is <see langword="null"/> or contains a
    /// null character.
    /// </exception>
    public static StrvScope AllocStrv(string[]? values)
    {
        if (values is null)
        {
            return default;
        }

        // The managed vector of the copies is allocated first: an out of
        // memory throw here has nothing to release, while the reverse order
        // would leak the native vector nothing is holding yet.
        nint[] owned = new nint[values.Length];
        nint* vector = (nint*)NativeMemory.AllocZeroed((nuint)values.Length + 1, (nuint)sizeof(nint));
        StrvScope scope = new(vector, owned);

        try
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is null)
                {
                    throw new ArgumentException(
                        "A null terminated array of strings cannot carry a null element: native code would "
                        + "read it as the end of the array and never see the elements behind it.",
                        nameof(values));
                }

                nint item = StringToUtf8Ptr(values[i]);
                owned[i] = item;
                vector[i] = item;
            }
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        return scope;
    }

    /// <summary>
    /// Reads a null terminated UTF-8 string that stays owned by native code.
    /// </summary>
    /// <param name="pointer">The string to read, may be <see cref="nint.Zero"/>.</param>
    /// <returns>The decoded string, or <see langword="null"/>.</returns>
    public static string? PtrToStringUtf8(nint pointer)
    {
        if (pointer == nint.Zero)
        {
            return null;
        }

        ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)pointer);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Reads a null terminated UTF-8 string that the caller owns, and releases
    /// it with <c>g_free</c>.
    /// </summary>
    /// <param name="pointer">The string to read, may be <see cref="nint.Zero"/>.</param>
    /// <returns>The decoded string, or <see langword="null"/>.</returns>
    public static string? PtrToStringUtf8AndFree(nint pointer)
    {
        if (pointer == nint.Zero)
        {
            return null;
        }

        try
        {
            return PtrToStringUtf8(pointer);
        }
        finally
        {
            GLibNative.Free(pointer);
        }
    }

    /// <summary>
    /// Reads a <c>NULL</c> terminated array of UTF-8 strings.
    /// </summary>
    /// <param name="strv">The array to read, may be <see cref="nint.Zero"/>.</param>
    /// <param name="free">
    /// <see langword="true"/> to release the array and its elements with
    /// <c>g_strfreev</c> afterwards.
    /// </param>
    /// <returns>The decoded strings, or <see langword="null"/>.</returns>
    public static string[]? StrvToArray(nint strv, bool free)
    {
        if (strv == nint.Zero)
        {
            return null;
        }

        try
        {
            nint* items = (nint*)strv;
            int count = 0;
            while (items[count] != nint.Zero)
            {
                count++;
            }

            string[] result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = PtrToStringUtf8(items[i]) ?? string.Empty;
            }

            return result;
        }
        finally
        {
            if (free)
            {
                GLibNative.StrFreeV(strv);
            }
        }
    }

    /// <summary>
    /// Allocates <paramref name="size"/> zero initialised bytes with
    /// <c>g_malloc0</c>.
    /// </summary>
    /// <param name="size">The number of bytes to allocate.</param>
    /// <returns>The new buffer.</returns>
    public static nint Malloc0(nuint size) => GLibNative.Malloc0(size);

    /// <summary>
    /// Releases a buffer that was allocated by GLib.
    /// </summary>
    /// <param name="memory">The buffer, may be <see cref="nint.Zero"/>.</param>
    public static void Free(nint memory)
    {
        if (memory != nint.Zero)
        {
            GLibNative.Free(memory);
        }
    }

    /// <summary>
    /// Rejects a string that native code would only see half of.
    /// </summary>
    /// <param name="value">The string that is about to be encoded.</param>
    /// <remarks>
    /// A C string ends at the first null byte, so a managed string that carries
    /// one arrives in native code truncated at that point. Silently passing
    /// half a caps description, half a property name or half a file path is a
    /// class of bug that is hard to see and easy to refuse, and no GLib or
    /// GStreamer entry point takes a string that is allowed to contain one.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> contains a null character.
    /// </exception>
    private static void ThrowIfEmbeddedNull(string value)
    {
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A string that is passed to native code must not contain a null character: " +
                "native code would only see the part in front of it.",
                nameof(value));
        }
    }

    /// <summary>
    /// Duplicates a native string with <c>g_strdup</c>.
    /// </summary>
    /// <param name="pointer">The string to duplicate, may be <see cref="nint.Zero"/>.</param>
    /// <returns>The copy, or <see cref="nint.Zero"/>.</returns>
    public static nint StrDup(nint pointer) =>
        pointer == nint.Zero ? nint.Zero : GLibNative.StrDup((byte*)pointer);
}
