using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="HashTableMarshal.ToValueDictionary"/> over tables built here, so
/// that the paths the GES time effect never takes are reached as well: a
/// string entry, an uninitialised entry, a duplicate name and an entry with no
/// key or no value.
/// </summary>
/// <remarks>
/// The tables own neither keys nor values; every test frees what it made
/// after the table is gone. The source values are plain locals, so that their
/// address is stable and their setters change them rather than a copy.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class HashTableValueDictionaryTests
{
    [Fact]
    public void EveryEntryIsCopiedAndTheCopiesDisposeCleanly()
    {
        nint rateKey = GMarshal.StringToUtf8Ptr("rate");
        nint nameKey = GMarshal.StringToUtf8Ptr("name");
        nint emptyKey = GMarshal.StringToUtf8Ptr("empty");
        Value rate = Value.New(GType.Double);
        Value name = Value.New(GType.String);
        GValueNative empty = default;
        nint table = HashTableMarshal.NewFull(HashTableMarshal.StringHash, HashTableMarshal.StringEqual, 0, 0);
        try
        {
            rate.SetDouble(1.5);
            name.SetString("scaletempo");
            _ = HashTableMarshal.Insert(table, rateKey, (nint)(&rate.NativeValue));
            _ = HashTableMarshal.Insert(table, nameKey, (nint)(&name.NativeValue));
            _ = HashTableMarshal.Insert(table, emptyKey, (nint)(&empty));

            Dictionary<string, Value> values = HashTableMarshal.ToValueDictionary(table);
            try
            {
                Assert.Equal(3, values.Count);
                Assert.Equal(1.5, values["rate"].GetDouble());
                Assert.Equal("scaletempo", values["name"].GetString());
                Assert.True(values["empty"].IsEmpty);

                // The string is a copy, not the payload of the source.
                name.SetString("pitch");
                Assert.Equal("scaletempo", values["name"].GetString());
            }
            finally
            {
                DisposeAll(values);
            }
        }
        finally
        {
            HashTableMarshal.Unref(table);
            rate.Dispose();
            name.Dispose();
            GMarshal.Free(rateKey);
            GMarshal.Free(nameKey);
            GMarshal.Free(emptyKey);
        }
    }

    [Fact]
    public void AZeroTableReadsAsTheEmptyDictionary()
    {
        Assert.Empty(HashTableMarshal.ToValueDictionary(nint.Zero));
    }

    [Fact]
    public void ADuplicateNameKeepsOneCopy()
    {
        // A direct hash tells two keys of the same text apart, which a table of
        // g_str_hash never does; the dictionary keeps one of them.
        nint first = GMarshal.StringToUtf8Ptr("rate");
        nint second = GMarshal.StringToUtf8Ptr("rate");
        Value one = Value.New(GType.String);
        Value two = Value.New(GType.String);
        nint table = HashTableMarshal.NewFull(0, 0, 0, 0);
        try
        {
            one.SetString("one");
            two.SetString("two");
            _ = HashTableMarshal.Insert(table, first, (nint)(&one.NativeValue));
            _ = HashTableMarshal.Insert(table, second, (nint)(&two.NativeValue));

            Dictionary<string, Value> values = HashTableMarshal.ToValueDictionary(table);
            try
            {
                Value kept = Assert.Single(values).Value;
                Assert.Contains(kept.GetString(), new[] { "one", "two" });
            }
            finally
            {
                DisposeAll(values);
            }
        }
        finally
        {
            HashTableMarshal.Unref(table);
            one.Dispose();
            two.Dispose();
            GMarshal.Free(first);
            GMarshal.Free(second);
        }
    }

    [Fact]
    public void AnEntryWithNoValueIsRefusedAndTheCopiesMadeBeforeItAreDisposed()
    {
        // Six entries share one object value, so every copy made takes a
        // reference of the element and every copy disposed gives it back. The
        // walk order of the table is checked first: at least one entry with a
        // value comes before the one without, so the refusal always happens
        // after copies were made, and the reference count proves they were
        // disposed on the way out.
        string[] names = ["a", "b", "c", "d", "e", "f"];
        nint[] keys = new nint[names.Length];
        nint missingKey = GMarshal.StringToUtf8Ptr("missing");
        using Gst.Element sink = Gst.ElementFactory.Make("fakesink", null)
            ?? throw new InvalidOperationException("fakesink is not available.");
        Value shared = Value.New(GType.Object);
        nint table = HashTableMarshal.NewFull(HashTableMarshal.StringHash, HashTableMarshal.StringEqual, 0, 0);
        try
        {
            shared.SetObject(sink);
            for (int i = 0; i < names.Length; i++)
            {
                keys[i] = GMarshal.StringToUtf8Ptr(names[i]);
                _ = HashTableMarshal.Insert(table, keys[i], (nint)(&shared.NativeValue));
            }

            _ = HashTableMarshal.Insert(table, missingKey, nint.Zero);

            Assert.True(
                EntriesWalkedBefore(table, missingKey) > 0,
                "The table walks the entry with no value first, so no copy would be made before the refusal.");
            uint before = RefCountOf(sink.Handle);

            _ = Assert.Throws<InvalidOperationException>(() => HashTableMarshal.ToValueDictionary(table));

            Assert.Equal(before, RefCountOf(sink.Handle));
        }
        finally
        {
            HashTableMarshal.Unref(table);
            shared.Dispose();
            foreach (nint key in keys)
            {
                GMarshal.Free(key);
            }

            GMarshal.Free(missingKey);
        }
    }

    [Fact]
    public void AnEntryWithNoKeyIsRefused()
    {
        Value rate = Value.New(GType.Double);
        nint table = HashTableMarshal.NewFull(0, 0, 0, 0);
        try
        {
            rate.SetDouble(1.5);
            _ = HashTableMarshal.Insert(table, nint.Zero, (nint)(&rate.NativeValue));

            _ = Assert.Throws<InvalidOperationException>(() => HashTableMarshal.ToValueDictionary(table));
        }
        finally
        {
            HashTableMarshal.Unref(table);
            rate.Dispose();
        }
    }

    /// <summary>Counts the entries the table walks before the given key.</summary>
    /// <param name="table">The table.</param>
    /// <param name="key">The key to stop at, compared by address.</param>
    /// <returns>The number of keys walked before <paramref name="key"/>.</returns>
    private static int EntriesWalkedBefore(nint table, nint key)
    {
        // The same iterator ToValueDictionary walks the table with.
        HashTableIter iterator = default;
        IterInit(&iterator, table);
        int walked = 0;
        nint current;
        while (IterNext(&iterator, &current, null) != 0)
        {
            if (current == key)
            {
                return walked;
            }

            walked++;
        }

        throw new InvalidOperationException("The key is not in the table.");
    }

    /// <summary>Reads the <c>ref_count</c> of a GObject.</summary>
    /// <param name="handle">The instance.</param>
    /// <returns>The reference count.</returns>
    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    [LibraryImport("GLib", EntryPoint = "g_hash_table_iter_init")]
    private static partial void IterInit(HashTableIter* iterator, nint table);

    [LibraryImport("GLib", EntryPoint = "g_hash_table_iter_next")]
    private static partial int IterNext(HashTableIter* iterator, nint* key, nint* value);

    private static void DisposeAll(Dictionary<string, Value> values)
    {
        foreach (Value value in values.Values)
        {
            value.Dispose();
        }
    }
}
