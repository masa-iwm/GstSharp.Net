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
public sealed unsafe class HashTableValueDictionaryTests
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
    public void AnEntryWithNoValueIsRefused()
    {
        nint rateKey = GMarshal.StringToUtf8Ptr("rate");
        nint missingKey = GMarshal.StringToUtf8Ptr("missing");
        Value rate = Value.New(GType.String);
        nint table = HashTableMarshal.NewFull(HashTableMarshal.StringHash, HashTableMarshal.StringEqual, 0, 0);
        try
        {
            rate.SetString("1.5");
            _ = HashTableMarshal.Insert(table, rateKey, (nint)(&rate.NativeValue));
            _ = HashTableMarshal.Insert(table, missingKey, nint.Zero);

            _ = Assert.Throws<InvalidOperationException>(() => HashTableMarshal.ToValueDictionary(table));

            // The source is untouched by the copies disposed on the way out.
            Assert.Equal("1.5", rate.GetString());
        }
        finally
        {
            HashTableMarshal.Unref(table);
            rate.Dispose();
            GMarshal.Free(rateKey);
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

    private static void DisposeAll(Dictionary<string, Value> values)
    {
        foreach (Value value in values.Values)
        {
            value.Dispose();
        }
    }
}
