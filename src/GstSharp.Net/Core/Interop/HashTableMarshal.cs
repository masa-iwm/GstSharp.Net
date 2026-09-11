using System.Runtime.InteropServices;

namespace Gst.Interop;

/// <summary>
/// Reads the <c>GHashTable</c> that a native call returned into a managed
/// dictionary, and builds the temporary table that a native call is given.
/// </summary>
/// <remarks>
/// <para>
/// A <c>GHashTable</c> never reaches managed code as a table. Every entry is
/// copied out while the handle is still valid and the handle is gone before the
/// member returns, so no managed value can outlive the memory it points at.
/// That copy is what keeps the contract of the GStreamer getters honest:
/// <c>gst_uri_get_query_table</c> hands out the live internal table of a
/// possibly shared URI, and a wrapper that stayed live would let managed code
/// edit a URI that C considers immutable.
/// </para>
/// <para>
/// The same holds in the other direction. The table a call is given is built
/// here in the shape GStreamer expects — hashed and compared as strings, with
/// both halves of every entry released by <c>g_free</c> — lives for the length
/// of one call, and is released by the scope that built it. The callee that
/// keeps it takes a reference of its own first, which is why releasing ours
/// straight away is right.
/// </para>
/// <para>
/// The hash of a key, the equality of two keys and the destructor of an entry
/// are addresses rather than calls, so they are resolved out of the same
/// library every other entry point comes from. Handing GLib a null hash instead
/// would select <c>g_direct_hash</c>, which compares the key pointers and would
/// make every lookup of a string key miss.
/// </para>
/// </remarks>
internal static unsafe partial class HashTableMarshal
{
    private static readonly Lazy<nint> StringHashPointer = new(
        static () => Resolve("g_str_hash"),
        isThreadSafe: true);

    private static readonly Lazy<nint> StringEqualPointer = new(
        static () => Resolve("g_str_equal"),
        isThreadSafe: true);

    private static readonly Lazy<nint> FreePointer = new(
        static () => Resolve("g_free"),
        isThreadSafe: true);

    /// <summary>Gets the address of <c>g_str_hash</c>.</summary>
    internal static nint StringHash => StringHashPointer.Value;

    /// <summary>Gets the address of <c>g_str_equal</c>.</summary>
    internal static nint StringEqual => StringEqualPointer.Value;

    /// <summary>Gets the address of <c>g_free</c>.</summary>
    internal static nint FreeFunction => FreePointer.Value;

    /// <summary>
    /// Copies a table of strings into a dictionary.
    /// </summary>
    /// <param name="table">The table to read, may be <see cref="nint.Zero"/>.</param>
    /// <param name="unref">
    /// <see langword="true"/> to release the reference the call handed over,
    /// once the copy has been made.
    /// </param>
    /// <returns>
    /// The entries, or <see langword="null"/> when <paramref name="table"/> is
    /// <see cref="nint.Zero"/>.
    /// </returns>
    /// <remarks>
    /// An absent table and an empty one are different answers and stay
    /// different: a URI without a query answers <see langword="null"/>, and one
    /// whose query is empty answers an empty dictionary. A key is always a
    /// string, because nothing can hash a null key; a value may be
    /// <see langword="null"/>, which is how C spells a query key that carries no
    /// value at all.
    /// </remarks>
    internal static Dictionary<string, string?>? ToStringDictionary(nint table, bool unref)
    {
        if (table == nint.Zero)
        {
            return null;
        }

        try
        {
            Dictionary<string, string?> result = new((int)Size(table), StringComparer.Ordinal);
            HashTableIter iterator = default;
            IterInit(&iterator, table);

            nint key;
            nint value;
            while (IterNext(&iterator, &key, &value) != 0)
            {
                result[GMarshal.PtrToStringUtf8(key) ?? string.Empty] = GMarshal.PtrToStringUtf8(value);
            }

            return result;
        }
        finally
        {
            if (unref)
            {
                Unref(table);
            }
        }
    }

    /// <summary>
    /// Copies a table of GObject values into a dictionary of wrappers.
    /// </summary>
    /// <typeparam name="T">The wrapper type of a value.</typeparam>
    /// <param name="table">The table to read, may be <see cref="nint.Zero"/>.</param>
    /// <param name="wrap">Builds the wrapper of one value.</param>
    /// <returns>The entries.</returns>
    /// <remarks>
    /// Nothing is released: this reads a table the library keeps owning, which
    /// is the only shape of a GObject valued table the generator plans. A
    /// <see cref="nint.Zero"/> table reads as the empty dictionary rather than
    /// as <see langword="null"/>, because the member that reaches this answers a
    /// table its own initializer created and never an absent one.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// An entry carries no value, or one that is not a <typeparamref name="T"/>.
    /// Neither can be put in a dictionary whose values are not nullable, and
    /// both mean the table is not the one the member was planned for.
    /// </exception>
    internal static Dictionary<string, T> ToObjectDictionary<T>(nint table, Func<nint, T?> wrap)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(wrap);

        if (table == nint.Zero)
        {
            return new Dictionary<string, T>(StringComparer.Ordinal);
        }

        // The entries are read out in one pass and wrapped in a second one.
        // Wrapping interns a GObject, which releases the wrappers that were
        // dropped since the last one was made, and a release that ran a
        // disposal handler of the library could edit the very table this reads;
        // an edit while an iterator is open makes GLib abandon the walk and the
        // dictionary would come out short with nothing said. Nothing in the
        // corpus does that today, and closing the iterator first costs one
        // array.
        int count = (int)Size(table);
        (string Name, nint Value)[] entries = new (string, nint)[count];
        int taken = 0;

        HashTableIter iterator = default;
        IterInit(&iterator, table);

        nint key;
        nint value;
        while (taken < count && IterNext(&iterator, &key, &value) != 0)
        {
            entries[taken++] = (GMarshal.PtrToStringUtf8(key) ?? string.Empty, value);
        }

        Dictionary<string, T> result = new(taken, StringComparer.Ordinal);
        for (int i = 0; i < taken; i++)
        {
            (string name, nint entry) = entries[i];
            if (entry == nint.Zero || wrap(entry) is not { } wrapped)
            {
                throw new InvalidOperationException(
                    $"The entry named {name} of the table carries no value of the expected type, " +
                    "so the table cannot be read into a dictionary.");
            }

            result[name] = wrapped;
        }

        return result;
    }

    /// <summary>
    /// Builds the table a native call is given, for the length of that call.
    /// </summary>
    /// <param name="values">The entries to copy, may be <see langword="null"/>.</param>
    /// <returns>
    /// A scope that has to be disposed once the call has returned, and whose
    /// <see cref="HashTableScope.Handle"/> is <see cref="nint.Zero"/> when
    /// <paramref name="values"/> is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// An empty dictionary is not a <see langword="null"/> one: it becomes an
    /// empty table, which is a query with no keys at all, while
    /// <see langword="null"/> is the absence of a query, and the callee tells
    /// the two apart.
    /// </para>
    /// <para>
    /// The keys and the values are copied with the allocator of GLib and the
    /// table releases both halves of every entry with <c>g_free</c>, which is
    /// the shape GStreamer requires of a table it keeps: it inserts entries of
    /// its own in it, and removes entries from it, long after the call that
    /// handed it over returned.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A key is <see langword="null"/>, or a key or a value contains a null
    /// character.
    /// </exception>
    internal static HashTableScope Alloc(IReadOnlyDictionary<string, string?>? values)
    {
        if (values is null)
        {
            return default;
        }

        nint table = NewFull(StringHash, StringEqual, FreeFunction, FreeFunction);

        try
        {
            foreach (KeyValuePair<string, string?> entry in values)
            {
                // A Dictionary cannot hold one, but the parameter is the
                // interface: an implementation of its own may answer a null key
                // where a null value is legal. GLib would take it and
                // g_str_hash would read through it, so it is refused here
                // rather than in the process.
                if (entry.Key is null)
                {
                    throw new ArgumentException(
                        "A key of the table must not be null; only a value may be.",
                        nameof(values));
                }

                nint key = GMarshal.StringToUtf8Ptr(entry.Key);
                nint value;

                try
                {
                    value = GMarshal.StringToUtf8Ptr(entry.Value);
                }
                catch
                {
                    GMarshal.Free(key);
                    throw;
                }

                // The table owns both halves from here on, a duplicate key
                // included: an insert that meets one keeps the key it already
                // has and frees the copy it was handed.
                Insert(table, key, value);
            }
        }
        catch
        {
            Unref(table);
            throw;
        }

        return new HashTableScope(table);
    }

    /// <summary>Builds an empty table.</summary>
    /// <param name="hash">The hash of a key.</param>
    /// <param name="equal">The equality of two keys.</param>
    /// <param name="keyDestroy">The destructor of a key, may be <see cref="nint.Zero"/>.</param>
    /// <param name="valueDestroy">The destructor of a value, may be <see cref="nint.Zero"/>.</param>
    /// <returns>The new table, which the caller owns.</returns>
    [LibraryImport("GLib", EntryPoint = "g_hash_table_new_full")]
    internal static partial nint NewFull(nint hash, nint equal, nint keyDestroy, nint valueDestroy);

    /// <summary>Puts one entry in a table.</summary>
    /// <param name="table">The table to insert into.</param>
    /// <param name="key">The key, which the table takes over.</param>
    /// <param name="value">The value, which the table takes over.</param>
    /// <returns>Non zero when the key was not in the table already.</returns>
    [LibraryImport("GLib", EntryPoint = "g_hash_table_insert")]
    internal static partial int Insert(nint table, nint key, nint value);

    /// <summary>Releases one reference of a table.</summary>
    /// <param name="table">The table to release.</param>
    [LibraryImport("GLib", EntryPoint = "g_hash_table_unref")]
    internal static partial void Unref(nint table);

    /// <summary>Returns the number of entries of a table.</summary>
    /// <param name="table">The table to measure.</param>
    /// <returns>The number of entries.</returns>
    [LibraryImport("GLib", EntryPoint = "g_hash_table_size")]
    internal static partial uint Size(nint table);

    [LibraryImport("GLib", EntryPoint = "g_hash_table_iter_init")]
    private static partial void IterInit(HashTableIter* iterator, nint table);

    [LibraryImport("GLib", EntryPoint = "g_hash_table_iter_next")]
    private static partial int IterNext(HashTableIter* iterator, nint* key, nint* value);

    private static nint Resolve(string symbol)
    {
        nint module = NativeLoader.Load("GLib");

        if (!NativeLibrary.TryGetExport(module, symbol, out nint address))
        {
            throw new InvalidOperationException(
                $"The running GLib does not export '{symbol}', so no hash table can be built.");
        }

        return address;
    }
}

/// <summary>
/// The iteration state of a <c>GHashTable</c>, which the caller allocates.
/// </summary>
/// <remarks>
/// It mirrors <c>GHashTableIter</c> field for field — three pointers, two
/// integers and a pointer — so that it is exactly as large as the state GLib
/// writes into it. The fields are private state of GLib and are never read
/// here; the structure owns nothing and needs no release.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct HashTableIter
{
    private nint _first;
    private nint _second;
    private nint _third;
    private int _fourth;
    private int _fifth;
    private nint _sixth;
}
