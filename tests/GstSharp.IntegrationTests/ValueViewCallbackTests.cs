using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The eight members that hand a <c>GValue</c> to a managed callback, against
/// the library that is installed: the three structure walks in both their
/// <c>GQuark</c> and their <c>GstIdStr</c> spelling, and the two iterator walks.
/// </summary>
/// <remarks>
/// <para>
/// What is under test is the projection rather than the walk. A
/// <c>const GValue*</c> argument arrives as a <see cref="ValueView"/> and a
/// writable one as a <see cref="ValueRef"/>; both point into storage the caller
/// of the callback owns, both read what <see cref="Value"/> reads, and a write
/// through the writable one has to be visible in the structure afterwards —
/// that is the whole claim of <c>map_in_place</c>, and a projection that copied
/// instead of pointing would pass every read and lose every write.
/// </para>
/// <para>
/// The <c>_id_str</c> half of each pair arrived in 1.26, so its tests are gated
/// with <see cref="RequiresGStreamerFactAttribute"/> and skipped whole on the
/// 1.24 floor of the CI matrix; <see cref="NativeAvailability.Has126"/> is the
/// probe that pins that gate to a real export, exactly as
/// <see cref="NativeAvailability.Has128"/> does for 1.28. The <c>GQuark</c> half
/// exists on the floor and is deprecated since 1.26, which is why the calls to
/// it are the only place in this file where CS0618 is suppressed. Both halves
/// are covered on purpose: the deprecated one is what the floor can run.
/// </para>
/// <para>
/// An exception a handler throws is not visible to the caller of the walk: a
/// trampoline reports it through <c>Gst.Interop.ExceptionTrap</c> and answers the
/// failure value, which is the contract of every <c>scope=call</c> callback of
/// this binding. Everything a handler wants to assert is therefore recorded in a
/// captured local and asserted after the walk. The view itself is never
/// captured, and could not be: it is a <c>ref struct</c>.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ValueViewCallbackTests
{
    [Fact]
    public void TheDeprecatedForeachReadsEveryFieldThroughAView()
    {
        using Structure structure = Structure.NewEmpty("probe");
        SetInt(structure, "count", 7);
        SetString(structure, "label", "seven");

        List<string> seen = [];
        string? label = null;
        int count = 0;

        // gst_structure_foreach is deprecated since 1.26 in favour of the
        // _id_str spelling, and is deliberately under test: it is the only one
        // of the pair that exists on the 1.24 floor of the CI matrix.
#pragma warning disable CS0618 // gst_structure_foreach, deprecated in 1.26 in favour of gst_structure_foreach_id_str
        Assert.True(structure.Foreach((fieldId, value) =>
        {
            seen.Add(fieldId.ToString());
            if (value.Type == GType.Int)
            {
                count = value.GetInt();
            }
            else if (value.Type == GType.String)
            {
                label = value.GetString();
            }

            return true;
        }));
#pragma warning restore CS0618

        Assert.Equal(new[] { "count", "label" }, seen.ToArray());
        Assert.Equal(7, count);
        Assert.Equal("seven", label);
    }

    [Fact]
    public void AHandlerThatAnswersFalseStopsTheWalk()
    {
        using Structure structure = Structure.NewEmpty("stopped");
        SetInt(structure, "first", 1);
        SetInt(structure, "second", 2);

        int visits = 0;

#pragma warning disable CS0618 // gst_structure_foreach, deprecated in 1.26 in favour of gst_structure_foreach_id_str
        Assert.False(structure.Foreach((fieldId, value) =>
        {
            visits++;
            return false;
        }));
#pragma warning restore CS0618

        Assert.Equal(1, visits);
    }

    [RequiresGStreamerFact(26)]
    public void TheIdStrForeachReadsTheSameFields()
    {
        // The attribute gates on the version the library reports; this asks the
        // probe, so that the two mechanisms are seen to agree at the one place
        // where a 1.26 only member is actually called.
        Assert.True(NativeAvailability.Has126);

        using Structure structure = Structure.NewEmpty("probe");
        SetInt(structure, "count", 7);

        List<string> seen = [];
        object? content = null;

        Assert.True(structure.ForeachIdStr((fieldname, value) =>
        {
            seen.Add(fieldname.AsStr());
            content = value.GetContent();
            return true;
        }));

        Assert.Equal(new[] { "count" }, seen.ToArray());
        Assert.Equal(7, content);
    }

    [Fact]
    public void AWriteThroughTheWritableViewReachesTheStructure()
    {
        using Structure structure = Structure.NewEmpty("mapped");
        SetInt(structure, "count", 1);
        SetString(structure, "label", "one");

        // The claim of map_in_place: the callback is handed the field itself,
        // not a copy of it, so what it writes is what the structure holds
        // afterwards.
#pragma warning disable CS0618 // gst_structure_map_in_place, deprecated in 1.26 in favour of gst_structure_map_in_place_id_str
        Assert.True(structure.MapInPlace((fieldId, value) =>
        {
            if (value.Type == GType.Int)
            {
                value.SetInt(value.GetInt() + 41);
            }
            else if (value.Type == GType.String)
            {
                value.SetString("changed");
            }

            return true;
        }));
#pragma warning restore CS0618

        using Value count = structure.GetValue("count");
        using Value label = structure.GetValue("label");
        Assert.Equal(42, count.GetInt());
        Assert.Equal("changed", label.GetString());
    }

    [Fact]
    public void TheWritableViewRefusesToChangeTheTypeOfAField()
    {
        using Structure structure = Structure.NewEmpty("guarded");
        SetInt(structure, "count", 1);

        bool refused = false;

        // A structure field whose type the callback changed - or unset - is a
        // field the structure keeps and every later reader walks into, because
        // map_in_place writes it back unchecked. The view has no way to do it:
        // a setter of another type throws, and there is no Unset at all.
#pragma warning disable CS0618 // gst_structure_map_in_place, deprecated in 1.26 in favour of gst_structure_map_in_place_id_str
        Assert.True(structure.MapInPlace((fieldId, value) =>
        {
            try
            {
                value.SetString("not an int");
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }

            return true;
        }));
#pragma warning restore CS0618

        Assert.True(refused);

        using Value count = structure.GetValue("count");
        Assert.Equal(GType.Int, count.Type);
        Assert.Equal(1, count.GetInt());
    }

    [Fact]
    public void TheWritableViewRefusesContentOfAnotherBoxedType()
    {
        using Structure structure = Structure.NewEmpty("typed");
        using Caps caps = Assert.IsType<Caps>(Caps.FromString("video/x-raw"));
        using Structure payload = Structure.NewEmpty("payload");

        SetMiniObject(structure, "caps", caps);
        SetBoxed(structure, "payload", payload);

        bool refusedBoxed = false;
        bool refusedMiniObject = false;

        // Asking whether the field holds a boxed value is not enough here.
        // g_value_set_boxed copies what it is handed with the copy function of
        // the type the *value* holds, so a wrapper of another boxed type is not
        // a refused write but the wrong function over a foreign pointer:
        // gst_mini_object_ref over a GstStructure below, and gst_structure_copy
        // over a GstCaps the other way round. GLib says nothing on either path.
#pragma warning disable CS0618 // gst_structure_map_in_place, deprecated in 1.26 in favour of gst_structure_map_in_place_id_str
        Assert.True(structure.MapInPlace((fieldId, value) =>
        {
            if (string.Equals(fieldId.ToString(), "caps", StringComparison.Ordinal))
            {
                try
                {
                    value.SetBoxed(payload);
                }
                catch (InvalidOperationException)
                {
                    refusedBoxed = true;
                }
            }
            else
            {
                try
                {
                    value.SetMiniObject(caps);
                }
                catch (InvalidOperationException)
                {
                    refusedMiniObject = true;
                }
            }

            return true;
        }));
#pragma warning restore CS0618

        Assert.True(refusedBoxed);
        Assert.True(refusedMiniObject);

        // Both fields hold what they always held.
        using Value capsField = structure.GetValue("caps");
        using Caps? heldCaps = capsField.GetMiniObject<Caps>();
        Assert.NotNull(heldCaps);
        Assert.Equal("video/x-raw", heldCaps.ToString());

        using Value payloadField = structure.GetValue("payload");
        using Structure? heldPayload = payloadField.GetBoxed<Structure>();
        Assert.NotNull(heldPayload);
        Assert.Equal("payload", heldPayload.GetName());
    }

    [Fact]
    public void TheWritableViewAcceptsContentOfTheTypeTheFieldHolds()
    {
        using Structure structure = Structure.NewEmpty("typed");
        using Caps before = Assert.IsType<Caps>(Caps.FromString("video/x-raw"));
        using Structure beforePayload = Structure.NewEmpty("before");

        SetMiniObject(structure, "caps", before);
        SetBoxed(structure, "payload", beforePayload);

        using Caps after = Assert.IsType<Caps>(Caps.FromString("audio/x-raw"));
        using Structure afterPayload = Structure.NewEmpty("after");

        // The other half of the guard: content of the type the field holds goes
        // through, and the field holds a copy of it afterwards.
#pragma warning disable CS0618 // gst_structure_map_in_place, deprecated in 1.26 in favour of gst_structure_map_in_place_id_str
        Assert.True(structure.MapInPlace((fieldId, value) =>
        {
            if (string.Equals(fieldId.ToString(), "caps", StringComparison.Ordinal))
            {
                value.SetMiniObject(after);
            }
            else
            {
                value.SetBoxed(afterPayload);
            }

            return true;
        }));
#pragma warning restore CS0618

        using Value capsField = structure.GetValue("caps");
        using Caps? heldCaps = capsField.GetMiniObject<Caps>();
        Assert.NotNull(heldCaps);
        Assert.Equal("audio/x-raw", heldCaps.ToString());

        using Value payloadField = structure.GetValue("payload");
        using Structure? heldPayload = payloadField.GetBoxed<Structure>();
        Assert.NotNull(heldPayload);
        Assert.Equal("after", heldPayload.GetName());
    }

    [RequiresGStreamerFact(26)]
    public void TheIdStrMapWritesThroughTheSameView()
    {
        using Structure structure = Structure.NewEmpty("mapped");
        SetInt(structure, "count", 1);

        Assert.True(structure.MapInPlaceIdStr((fieldname, value) =>
        {
            // AsView is the way a handler hands the value on to code that only
            // reads: it is the same pointer, so it sees the write that follows.
            Assert.Equal(1, value.AsView().GetInt());
            value.SetInt(2);
            return true;
        }));

        using Value count = structure.GetValue("count");
        Assert.Equal(2, count.GetInt());
    }

    [RequiresGStreamerFact(26)]
    public void TheFilteringMapRemovesTheFieldsItAnswersFalseFor()
    {
        using Structure structure = Structure.NewEmpty("filtered");
        SetInt(structure, "keep", 1);
        SetInt(structure, "drop", 2);

        // Answering false is the supported way of removing a field, which is
        // why the view offers no Unset: the structure does the removal itself.
        structure.FilterAndMapInPlaceIdStr((fieldname, value) =>
            !string.Equals(fieldname.AsStr(), "drop", StringComparison.Ordinal));

        Assert.Equal(1, structure.NFields());
        Assert.True(structure.HasField("keep"));
        Assert.False(structure.HasField("drop"));
    }

    [Fact]
    public void TheDeprecatedFilteringMapRemovesTheFieldsItAnswersFalseFor()
    {
        using Structure structure = Structure.NewEmpty("filtered");
        SetInt(structure, "keep", 1);
        SetInt(structure, "drop", 2);

        // The GQuark spelling of the filtering walk is the only one the 1.24
        // floor of the CI matrix can run, and it is the walk whose failure
        // value destroys data, so it is covered in both spellings.
#pragma warning disable CS0618 // gst_structure_filter_and_map_in_place, deprecated in 1.26 in favour of gst_structure_filter_and_map_in_place_id_str
        structure.FilterAndMapInPlace((fieldId, value) =>
            !string.Equals(fieldId.ToString(), "drop", StringComparison.Ordinal));
#pragma warning restore CS0618

        Assert.Equal(1, structure.NFields());
        Assert.True(structure.HasField("keep"));
        Assert.False(structure.HasField("drop"));
    }

    [Fact]
    public void AThrowingHandlerLosesTheFieldTheFilteringMapWasVisiting()
    {
        using Structure structure = Structure.NewEmpty("thrown");
        SetInt(structure, "keep", 1);
        SetInt(structure, "lost", 2);

        List<Exception> reported = [];
        void OnFailure(Exception exception) => reported.Add(exception);

        // The documented cost of throwing out of this one handler, pinned. The
        // trampoline answers false for the handler that threw, and false is
        // what removes the field being visited: a handler that has to fail
        // without losing data has to catch its own exceptions. Subscribing also
        // keeps the report off the standard error stream of the run.
        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
#pragma warning disable CS0618 // gst_structure_filter_and_map_in_place, deprecated in 1.26 in favour of gst_structure_filter_and_map_in_place_id_str
            structure.FilterAndMapInPlace((fieldId, value) =>
                string.Equals(fieldId.ToString(), "keep", StringComparison.Ordinal)
                    ? true
                    : throw new InvalidOperationException("the handler failed"));
#pragma warning restore CS0618
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Exception failure = Assert.Single(reported);
        Assert.Equal("the handler failed", failure.Message);

        Assert.Equal(1, structure.NFields());
        Assert.True(structure.HasField("keep"));
        Assert.False(structure.HasField("lost"));
    }

    [Fact]
    public void ACopyTakenThroughTheViewOutlivesTheWalk()
    {
        using Structure structure = Structure.NewEmpty("copied");
        SetString(structure, "label", "kept");

        Value copy = default;

#pragma warning disable CS0618 // gst_structure_foreach, deprecated in 1.26 in favour of gst_structure_foreach_id_str
        Assert.True(structure.Foreach((fieldId, value) =>
        {
            copy = value.ToValue();
            return true;
        }));
#pragma warning restore CS0618

        try
        {
            // The view is gone; the copy owns its own payload, and the
            // structure was left holding what it always held.
            Assert.Equal(GType.String, copy.Type);
            Assert.Equal("kept", copy.GetString());

            using Value original = structure.GetValue("label");
            Assert.Equal("kept", original.GetString());
        }
        finally
        {
            copy.Dispose();
        }
    }

    [Fact]
    public void TheFoldAccumulatesIntoTheValueTheCallerOwns()
    {
        using Bin bin = Bin.New("folded");
        using Element first = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "one"));
        using Element second = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "two"));

        Assert.True(bin.AddMany(first, second));

        using Iterator iterator = bin.IterateElements();

        // The seed has to be initialized: the ret argument is the caller's own
        // storage and the fold neither creates nor releases it.
        Value total = Value.New(GType.Int);
        try
        {
            total.SetInt(0);

            Assert.Equal(
                IteratorResult.Done,
                iterator.Fold(
                    static (item, ret) =>
                    {
                        // The item is a stack GValue the fold resets after
                        // every call, which is the reason the projection is a
                        // ref struct rather than a wrapper.
                        Assert.NotNull(item.GetObject());
                        ret.SetInt(ret.GetInt() + 1);
                        return true;
                    },
                    ref total));

            Assert.Equal(2, total.GetInt());
        }
        finally
        {
            total.Dispose();
        }

        Assert.True(bin.RemoveMany(first, second));
    }

    [Fact]
    public void TheForeachVisitsEveryElement()
    {
        using Bin bin = Bin.New("visited");
        using Element first = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "one"));
        using Element second = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "two"));

        Assert.True(bin.AddMany(first, second));

        List<string> names = [];

        using Iterator iterator = bin.IterateElements();
        Assert.Equal(
            IteratorResult.Done,
            iterator.Foreach(item => names.Add((item.GetObject() as Gst.Object)?.Name ?? "?")));

        // A bin keeps its children most recently added first.
        Assert.Equal(new[] { "two", "one" }, names.ToArray());

        Assert.True(bin.RemoveMany(first, second));
    }

    private static void SetInt(Structure structure, string fieldName, int content)
    {
        using Value value = Value.New(GType.Int);
        value.SetInt(content);
        structure.SetValue(fieldName, value);
    }

    private static void SetBoxed(Structure structure, string fieldName, Gst.GObject.Boxed content)
    {
        using Value value = Value.New(content.BoxedType);
        value.SetBoxed(content);
        structure.SetValue(fieldName, value);
    }

    private static void SetMiniObject(Structure structure, string fieldName, Caps content)
    {
        // Gst.Caps.GetGType is internal to the binding; a test that initialises
        // a value to GST_TYPE_CAPS asks the library, as MiniObjectValueTests
        // does.
        using Value value = Value.New(new GType(TestNatives.CapsGetType()));
        value.SetMiniObject(content);
        structure.SetValue(fieldName, value);
    }

    private static void SetString(Structure structure, string fieldName, string content)
    {
        using Value value = Value.New(GType.String);
        value.SetString(content);
        structure.SetValue(fieldName, value);
    }
}
