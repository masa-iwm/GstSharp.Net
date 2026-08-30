using Gst;
using Xunit;
using GType = Gst.GObject.GType;
using Quark = Gst.GLib.Quark;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The holders of the fundamental value containers against the installed
/// library: <see cref="ValueArray"/>, <see cref="ValueList"/> and
/// <see cref="ValueUniqueList"/>.
/// </summary>
/// <remarks>
/// <para>
/// These are what makes an <c>array</c> or <c>list</c> typed caps field
/// readable in place. Such a field could already be reached through
/// <see cref="Structure.GetArray"/> and <see cref="Structure.GetList"/>, but
/// only as a converted <c>GValueArray</c> copy the caller owns; the holders
/// look into the container value itself, without the conversion. That is why
/// <see cref="AListFieldOfCapsIsReadThroughTheHolder"/> is the case the whole
/// item exists for and is asserted as such.
/// </para>
/// <para>
/// Everything but the <see cref="ValueUniqueList"/> tests is available since
/// GStreamer 1.18 at the latest and runs on the 1.24 floor of the Linux leg.
/// <c>GST_TYPE_UNIQUE_LIST</c> arrived in 1.28 as a whole, so those tests are
/// gated on the shared probe.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ValueContainerTests
{
    /// <summary>
    /// The life of an array value: initialized on a zeroed value, filled with
    /// two integers, counted, and indexed. The value an index hands back is a
    /// deep copy, so disposing it leaves the container it came from whole.
    /// </summary>
    [Fact]
    public void AnArrayIsInitializedFilledAndIndexed()
    {
        Value array = default;
        ValueArray.Init(ref array, 2);
        try
        {
            using (Value one = Value.CreateFor(1, GType.Int))
            {
                ValueArray.AppendValue(ref array, in one);
            }

            using (Value two = Value.CreateFor(2, GType.Int))
            {
                ValueArray.AppendValue(ref array, in two);
            }

            Assert.Equal(2u, ValueArray.GetSize(in array));

            using (Value second = ValueArray.GetValue(in array, 1))
            {
                Assert.Equal(GType.Int, second.Type);
                Assert.Equal(2, second.GetInt());
            }

            // The copy is disposed and the container is untouched by it: the
            // same index reads the same value again.
            Assert.Equal(2u, ValueArray.GetSize(in array));
            using Value again = ValueArray.GetValue(in array, 1);
            Assert.Equal(2, again.GetInt());
        }
        finally
        {
            array.Dispose();
        }
    }

    /// <summary>
    /// A prepended value goes in front, which is what tells the ordered array
    /// from the set.
    /// </summary>
    [Fact]
    public void APrependedValueGoesInFront()
    {
        Value array = default;
        ValueArray.Init(ref array, 0);
        try
        {
            using (Value tail = Value.CreateFor(2, GType.Int))
            {
                ValueArray.AppendValue(ref array, in tail);
            }

            using (Value head = Value.CreateFor(1, GType.Int))
            {
                ValueArray.PrependValue(ref array, in head);
            }

            using Value first = ValueArray.GetValue(in array, 0);
            Assert.Equal(1, first.GetInt());
        }
        finally
        {
            array.Dispose();
        }
    }

    /// <summary>
    /// <c>Init</c> wants an empty value. On a live one the C function refuses
    /// with a critical and writes nothing, so the container it was pointed at
    /// survives unchanged; the documentation of the member says as much.
    /// </summary>
    [Fact]
    public void InitOnALiveValueLeavesItUntouched()
    {
        Value list = default;
        ValueList.Init(ref list, 1);
        try
        {
            using (Value one = Value.CreateFor(1, GType.Int))
            {
                ValueList.AppendValue(ref list, in one);
            }

            Assert.Equal(1u, ValueList.GetSize(in list));

            // Logs a GLib critical, which is the C behaviour this is asserting.
            ValueList.Init(ref list, 8);

            Assert.Equal(1u, ValueList.GetSize(in list));
            using Value survivor = ValueList.GetValue(in list, 0);
            Assert.Equal(1, survivor.GetInt());
        }
        finally
        {
            list.Dispose();
        }
    }

    /// <summary>
    /// Concatenating two scalars builds a list of both: a value that is not a
    /// list counts as a list of one.
    /// </summary>
    [Fact]
    public void ConcatOfTwoScalarsIsAListOfTwo()
    {
        using Value one = Value.CreateFor(1, GType.Int);
        using Value two = Value.CreateFor(2, GType.Int);

        ValueList.Concat(out Value list, in one, in two);
        try
        {
            Assert.Equal(2u, ValueList.GetSize(in list));
            using Value second = ValueList.GetValue(in list, 1);
            Assert.Equal(2, second.GetInt());
        }
        finally
        {
            list.Dispose();
        }
    }

    /// <summary>
    /// Merging drops duplicates, and merging two values that compare equal
    /// leaves a single scalar rather than a list of one — which is why the
    /// destination has to be typed before it is treated as a container.
    /// </summary>
    [Fact]
    public void MergeOfTwoEqualScalarsIsTheScalarItself()
    {
        using Value one = Value.CreateFor(7, GType.Int);
        using Value same = Value.CreateFor(7, GType.Int);

        ValueList.Merge(out Value merged, in one, in same);
        try
        {
            Assert.Equal(GType.Int, merged.Type);
            Assert.Equal(7, merged.GetInt());
        }
        finally
        {
            merged.Dispose();
        }

        using Value other = Value.CreateFor(8, GType.Int);
        ValueList.Merge(out Value both, in one, in other);
        try
        {
            Assert.NotEqual(GType.Int, both.Type);
            Assert.Equal(2u, ValueList.GetSize(in both));
        }
        finally
        {
            both.Dispose();
        }
    }

    /// <summary>
    /// The case the holders exist for: a caps field that holds a list of rates
    /// is read out as a value and looked into through
    /// <see cref="ValueList.GetSize"/> and <see cref="ValueList.GetValue"/>.
    /// Before the holders the same field could only be read by letting
    /// <see cref="Structure.GetList"/> convert it into a <c>GValueArray</c>
    /// copy.
    /// </summary>
    [Fact]
    public void AListFieldOfCapsIsReadThroughTheHolder()
    {
        using Caps? caps = Caps.FromString("audio/x-raw, rate={ 44100, 48000 }");
        Assert.NotNull(caps);

        using Structure structure = caps.GetStructure(0);
        using Value rate = structure.IdGetValue(Quark.FromString("rate"));

        Assert.False(rate.IsEmpty);
        Assert.Equal(2u, ValueList.GetSize(in rate));

        using (Value first = ValueList.GetValue(in rate, 0))
        {
            Assert.Equal(44100, first.GetInt());
        }

        using Value second = ValueList.GetValue(in rate, 1);
        Assert.Equal(48000, second.GetInt());
    }

    /// <summary>
    /// The set: concatenating two different scalars keeps both, appending a
    /// value it already holds is dropped without a word, and appending a new
    /// one grows it.
    /// </summary>
    [RequiresGStreamerFact(28)]
    public void AUniqueListDropsTheValuesItAlreadyHolds()
    {
        using Value one = Value.CreateFor(1, GType.Int);
        using Value two = Value.CreateFor(2, GType.Int);

        ValueUniqueList.Concat(out Value set, in one, in two);
        try
        {
            Assert.Equal(2u, ValueUniqueList.GetSize(in set));

            using (Value duplicate = Value.CreateFor(1, GType.Int))
            {
                ValueUniqueList.AppendValue(ref set, in duplicate);
            }

            Assert.Equal(2u, ValueUniqueList.GetSize(in set));

            using (Value three = Value.CreateFor(3, GType.Int))
            {
                ValueUniqueList.AppendValue(ref set, in three);
            }

            Assert.Equal(3u, ValueUniqueList.GetSize(in set));
            using Value last = ValueUniqueList.GetValue(in set, 2);
            Assert.Equal(3, last.GetInt());
        }
        finally
        {
            set.Dispose();
        }
    }

    /// <summary>
    /// Concatenating a set with a value it already holds does not repeat it,
    /// which is the difference from <see cref="ValueList.Concat"/>.
    /// </summary>
    [RequiresGStreamerFact(28)]
    public void ConcatOfAUniqueListDeduplicates()
    {
        using Value one = Value.CreateFor(1, GType.Int);
        using Value same = Value.CreateFor(1, GType.Int);

        ValueUniqueList.Concat(out Value set, in one, in same);
        try
        {
            Assert.Equal(1u, ValueUniqueList.GetSize(in set));
            using Value only = ValueUniqueList.GetValue(in set, 0);
            Assert.Equal(1, only.GetInt());
        }
        finally
        {
            set.Dispose();
        }
    }
}
