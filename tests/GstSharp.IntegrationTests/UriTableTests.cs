using Gst;
using Gst.Interop;
using Xunit;
using Uri = Gst.Uri;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The three members of <see cref="Uri"/> that carry a <c>GHashTable</c> of
/// strings, against the library that is installed.
/// </summary>
/// <remarks>
/// <para>
/// Every entry point here is at or below the 1.24 floor of the CI matrix —
/// the query table is 1.6 and the media fragment table is 1.12 — so none of
/// them is gated on <c>NativeAvailability</c>.
/// </para>
/// <para>
/// What the tests are really about is the copy. C hands out the live query
/// table of the URI and adopts the table it is given by reference, and the
/// binding copies in both directions; a test that only round tripped values
/// would pass either way, so each direction is checked by editing the managed
/// dictionary afterwards and reading the URI back.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class UriTableTests
{
    /// <summary>
    /// The three states of a value: a key with a value, a bare key, and a key
    /// written with an <c>=</c> and nothing after it. The last two are
    /// different in C and stay different here.
    /// </summary>
    [Fact]
    public void TheQueryTableCarriesTheThreeStatesOfAValue()
    {
        using Uri uri = Uri.FromString("http://example.test/path?a=1&b&c=")
            ?? throw new InvalidOperationException("A plain http URI always parses.");

        System.Collections.Generic.Dictionary<string, string?> table = uri.GetQueryTable()
            ?? throw new InvalidOperationException("A URI with a query has a query table.");

        Assert.Equal(3, table.Count);
        Assert.Equal("1", table["a"]);
        Assert.Null(table["b"]);
        Assert.Equal(string.Empty, table["c"]);
    }

    /// <summary>
    /// No query at all and an empty query are different answers: the first is
    /// nothing, the second an empty dictionary.
    /// </summary>
    [Fact]
    public void AUriWithoutAQueryAnswersNothingAndAnEmptyQueryAnswersNoEntries()
    {
        using Uri without = Uri.FromString("http://example.test/path")
            ?? throw new InvalidOperationException("A plain http URI always parses.");
        Assert.Null(without.GetQueryTable());

        using Uri empty = Uri.FromString("http://example.test/path?")
            ?? throw new InvalidOperationException("A plain http URI always parses.");
        System.Collections.Generic.Dictionary<string, string?>? table = empty.GetQueryTable();
        Assert.NotNull(table);
        Assert.Empty(table);
    }

    /// <summary>
    /// The copy, read side: two calls answer two dictionaries, and editing one
    /// of them leaves the URI alone. C would hand the same live table out
    /// twice.
    /// </summary>
    [Fact]
    public void TheQueryTableIsASnapshotOfItsOwn()
    {
        using Uri uri = Uri.FromString("http://example.test/path?a=1")
            ?? throw new InvalidOperationException("A plain http URI always parses.");

        System.Collections.Generic.Dictionary<string, string?> first = uri.GetQueryTable()
            ?? throw new InvalidOperationException("A URI with a query has a query table.");
        System.Collections.Generic.Dictionary<string, string?> second = uri.GetQueryTable()
            ?? throw new InvalidOperationException("A URI with a query has a query table.");

        Assert.NotSame(first, second);

        first["a"] = "edited";
        first["added"] = "value";

        Assert.Equal("a=1", uri.GetQueryString());
        Assert.Equal("1", second["a"]);
        Assert.False(uri.QueryHasKey("added"));
    }

    /// <summary>
    /// The write side, round tripped through the members that read one key at a
    /// time: a bare key is present and has no value, and an empty value is a
    /// value.
    /// </summary>
    [Fact]
    public void AQueryTableRoundTripsThroughTheSingleKeyMembers()
    {
        using Uri uri = Uri.FromString("http://example.test/path")
            ?? throw new InvalidOperationException("A plain http URI always parses.");
        Assert.True(uri.IsWritable());

        Assert.True(uri.SetQueryTable(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["a"] = "1",
            ["b"] = null,
            ["c"] = string.Empty,
        }));

        Assert.Equal(["a", "b", "c"], uri.GetQueryKeys().Order(StringComparer.Ordinal));

        Assert.True(uri.QueryHasKey("a"));
        Assert.Equal("1", uri.GetQueryValue("a"));

        // A bare key is present and has no value, which is the one state the
        // value alone cannot tell from an absent key.
        Assert.True(uri.QueryHasKey("b"));
        Assert.Null(uri.GetQueryValue("b"));

        Assert.True(uri.QueryHasKey("c"));
        Assert.Equal(string.Empty, uri.GetQueryValue("c"));
    }

    /// <summary>
    /// The copy, write side: the URI keeps a table of its own, so the
    /// dictionary that was handed over may be edited afterwards with no effect
    /// at all. C would have adopted the very table by reference.
    /// </summary>
    [Fact]
    public void TheTableAQueryWasSetFromIsCopied()
    {
        using Uri uri = Uri.FromString("http://example.test/path")
            ?? throw new InvalidOperationException("A plain http URI always parses.");

        System.Collections.Generic.Dictionary<string, string?> table = new()
        {
            ["a"] = "1",
        };

        Assert.True(uri.SetQueryTable(table));

        table["a"] = "edited";
        table["added"] = "value";

        Assert.Equal("1", uri.GetQueryValue("a"));
        Assert.False(uri.QueryHasKey("added"));
    }

    /// <summary>
    /// Nothing takes the query out of the URI, and an empty dictionary is not
    /// nothing: it is a query with no keys, which the getter answers as an
    /// empty dictionary rather than as nothing.
    /// </summary>
    [Fact]
    public void SettingNothingClearsTheQueryAndAnEmptyTableDoesNot()
    {
        using Uri uri = Uri.FromString("http://example.test/path?a=1")
            ?? throw new InvalidOperationException("A plain http URI always parses.");

        Assert.True(uri.SetQueryTable(null));
        Assert.Null(uri.GetQueryTable());

        Assert.True(uri.SetQueryTable(new System.Collections.Generic.Dictionary<string, string?>()));
        System.Collections.Generic.Dictionary<string, string?>? table = uri.GetQueryTable();
        Assert.NotNull(table);
        Assert.Empty(table);
    }

    /// <summary>
    /// The compile time half. A dictionary spelled with the value type of the
    /// parameter is accepted as it stands: that is the shape a caller writes
    /// who means to pass a bare key.
    /// </summary>
    /// <remarks>
    /// A <c>Dictionary&lt;string, string&gt;</c> is a different matter.
    /// <c>IReadOnlyDictionary&lt;TKey, TValue&gt;</c> is invariant in
    /// <c>TValue</c> — it derives from
    /// <c>IReadOnlyCollection&lt;KeyValuePair&lt;TKey, TValue&gt;&gt;</c>, and a
    /// struct element cannot be covariant — so the conversion is the identity
    /// one the compiler reports as CS8620, a nullability warning that this
    /// repository compiles as an error. Passing one takes a <c>!</c>, which is
    /// what the line below spells and what the documentation of the member
    /// tells the caller.
    /// </remarks>
    [Fact]
    public void ADictionaryOfNullableValuesIsAcceptedAsItStands()
    {
        using Uri uri = Uri.FromString("http://example.test/path")
            ?? throw new InvalidOperationException("A plain http URI always parses.");

        System.Collections.Generic.Dictionary<string, string?> nullable = new()
        {
            ["a"] = "1",
        };

        Assert.True(uri.SetQueryTable(nullable));
        Assert.Equal("1", uri.GetQueryValue("a"));

        System.Collections.Generic.Dictionary<string, string> plain = new()
        {
            ["b"] = "2",
        };

        Assert.True(uri.SetQueryTable(plain!));
        Assert.Equal("2", uri.GetQueryValue("b"));
    }

    /// <summary>
    /// The media fragment table is parsed out of the fragment on every call:
    /// the two keys of a media fragment come back, a percent escape in a value
    /// is decoded, and a URI without a fragment answers nothing.
    /// </summary>
    [Fact]
    public void TheMediaFragmentTableIsParsedFromTheFragment()
    {
        using Uri uri = Uri.FromString("http://example.test/v#t=10,20&xywh=160,120,320,240")
            ?? throw new InvalidOperationException("A plain http URI always parses.");

        System.Collections.Generic.Dictionary<string, string?> table = uri.GetMediaFragmentTable()
            ?? throw new InvalidOperationException("A URI with a fragment has a fragment table.");

        Assert.Equal(2, table.Count);
        Assert.Equal("10,20", table["t"]);
        Assert.Equal("160,120,320,240", table["xywh"]);

        using Uri escaped = Uri.FromStringEscaped("http://example.test/v#title=a%20b")
            ?? throw new InvalidOperationException("A plain http URI always parses.");
        System.Collections.Generic.Dictionary<string, string?> decoded = escaped.GetMediaFragmentTable()
            ?? throw new InvalidOperationException("A URI with a fragment has a fragment table.");
        Assert.Equal("a b", decoded["title"]);

        using Uri without = Uri.FromString("http://example.test/v")
            ?? throw new InvalidOperationException("A plain http URI always parses.");
        Assert.Null(without.GetMediaFragmentTable());
    }

    /// <summary>
    /// The defensive branch of the runtime helper, which no bound member can
    /// reach: a table whose value is <c>NULL</c> has no entry a dictionary of
    /// values that are not nullable could hold, so it is refused rather than
    /// silently dropped. The table is built here by hand, because the one
    /// member that answers a table of GObjects never builds one like it.
    /// </summary>
    [Fact]
    public void ATableOfObjectsWithAMissingValueIsRefused()
    {
        nint table = HashTableMarshal.NewFull(
            HashTableMarshal.StringHash,
            HashTableMarshal.StringEqual,
            HashTableMarshal.FreeFunction,
            nint.Zero);

        try
        {
            HashTableMarshal.Insert(table, GMarshal.StringToUtf8Ptr("orphan"), nint.Zero);
            Assert.Equal(1u, HashTableMarshal.Size(table));

            Assert.Throws<InvalidOperationException>(
                () => HashTableMarshal.ToObjectDictionary<Gst.Object>(
                    table,
                    static handle => Gst.GObject.Object.FromNative<Gst.Object>(handle, Transfer.None)));
        }
        finally
        {
            HashTableMarshal.Unref(table);
        }
    }

    /// <summary>
    /// The other defensive branch, on the way out: a key that is
    /// <see langword="null"/>. A <see cref="Dictionary{TKey, TValue}"/> cannot
    /// hold one, but the parameter is the interface, so the guard is what
    /// stands between an implementation of a caller's own and
    /// <c>g_str_hash</c> reading through a null pointer.
    /// </summary>
    [Fact]
    public void ATableWithAKeyThatIsNothingIsRefused() =>
        Assert.Throws<ArgumentException>(() => HashTableMarshal.Alloc(new KeylessTable()));

    /// <summary>
    /// The one shape a dictionary of the framework cannot take: a single entry
    /// whose key is <see langword="null"/>.
    /// </summary>
    private sealed class KeylessTable : System.Collections.Generic.IReadOnlyDictionary<string, string?>
    {
        /// <inheritdoc/>
        public int Count => 1;

        /// <inheritdoc/>
        public System.Collections.Generic.IEnumerable<string> Keys => [null!];

        /// <inheritdoc/>
        public System.Collections.Generic.IEnumerable<string?> Values => ["value"];

        /// <inheritdoc/>
        public string? this[string key] => "value";

        /// <inheritdoc/>
        public bool ContainsKey(string key) => false;

        /// <inheritdoc/>
        public bool TryGetValue(string key, out string? value)
        {
            value = null;
            return false;
        }

        /// <inheritdoc/>
        public System.Collections.Generic.IEnumerator<
            System.Collections.Generic.KeyValuePair<string, string?>> GetEnumerator()
        {
            yield return new System.Collections.Generic.KeyValuePair<string, string?>(null!, "value");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
