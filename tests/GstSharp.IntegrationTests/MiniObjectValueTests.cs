using Gst;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Reading a mini object out of a <c>GValue</c> as the wrapper of the binding.
/// </summary>
/// <remarks>
/// <para>
/// A mini object is a boxed type as far as GObject is concerned, so a
/// <c>GstCaps</c> arrives in a property, in a structure field and in a signal
/// argument the same way a boxed value does — and its wrapper does not derive
/// from <see cref="Boxed"/>, which is why
/// <see cref="Value.GetMiniObject{T}"/> exists beside
/// <see cref="Value.GetBoxed{T}"/>.
/// </para>
/// <para>
/// The wrapper takes a reference of its own, so what the tests check besides
/// the content is that the value keeps what it owns: the caps stay readable
/// after the wrapper is disposed, and the wrapper stays readable after the
/// value is.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MiniObjectValueTests
{
    private const string CapsText = "audio/x-raw, format=(string)S16LE, rate=(int)44100, channels=(int)2";

    /// <summary>
    /// A property whose type is <c>GstCaps</c> reads as a <see cref="Caps"/>,
    /// and the two owners are independent.
    /// </summary>
    [Fact]
    public void CapsPropertyReadsAsItsWrapper()
    {
        using Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "typed"));

        using (Caps written = Assert.IsType<Caps>(Caps.FromString(CapsText)))
        {
            using Value assigned = Value.New(new GType(TestNatives.CapsGetType()));
            assigned.SetBoxed(written.Handle);
            filter.SetProperty("caps", assigned);
        }

        using Value value = filter.GetProperty("caps");
        Assert.Equal("GstCaps", value.Type.Name);

        // The value still owns its caps once the wrapper is gone.
        using (Caps? caps = value.GetMiniObject<Caps>())
        {
            Assert.NotNull(caps);
            Assert.Equal(CapsText, caps.ToString());
        }

        using Caps? again = value.GetMiniObject<Caps>();
        Assert.NotNull(again);
        Assert.Equal(CapsText, again.ToString());
    }

    /// <summary>
    /// A structure field that holds a caps reads the same way, which is the
    /// route <see cref="Structure.GetValue(string)"/> leaves open.
    /// </summary>
    [Fact]
    public void CapsInAStructureFieldReadsAsItsWrapper()
    {
        using Structure structure = Structure.NewEmpty("test");

        using (Caps written = Assert.IsType<Caps>(Caps.FromString(CapsText)))
        {
            using Value field = Value.New(new GType(TestNatives.CapsGetType()));
            field.SetBoxed(written.Handle);
            structure.SetValue("payload", field);
        }

        using Value read = structure.GetValue("payload");
        using Caps? caps = read.GetMiniObject<Caps>();

        Assert.NotNull(caps);
        Assert.Equal(CapsText, caps.ToString());

        // The structure is a copy of its own and outlives the value that was
        // written into it, so the wrapper is not a window into either. The
        // serialized spelling of the type moved between releases - 1.24 writes
        // (GstCaps) where 1.28 writes (caps) - so the assertion accepts the
        // field with either.
        string serialized = structure.ToString();
        Assert.True(
            serialized.Contains("payload=(caps)", StringComparison.Ordinal)
                || serialized.Contains("payload=(GstCaps)", StringComparison.Ordinal),
            $"the caps field is missing from: {serialized}");
    }

    /// <summary>
    /// The wrapper outlives the value it was read from, because it holds a
    /// reference of its own rather than the value's.
    /// </summary>
    [Fact]
    public void TheWrapperOutlivesTheValue()
    {
        using Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "outlived"));

        using (Caps written = Assert.IsType<Caps>(Caps.FromString(CapsText)))
        {
            using Value assigned = Value.New(new GType(TestNatives.CapsGetType()));
            assigned.SetBoxed(written.Handle);
            filter.SetProperty("caps", assigned);
        }

        Caps? caps;

        using (Value value = filter.GetProperty("caps"))
        {
            caps = value.GetMiniObject<Caps>();
        }

        using (caps)
        {
            Assert.NotNull(caps);
            Assert.Equal(CapsText, caps.ToString());
        }
    }

    /// <summary>
    /// The type argument is checked: a value that holds a caps is not a
    /// <c>Gst.TagList</c>, and a value that holds no boxed value at all is not
    /// a mini object.
    /// </summary>
    [Fact]
    public void TheTypeArgumentIsChecked()
    {
        using Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "checked"));

        using (Caps written = Assert.IsType<Caps>(Caps.FromString(CapsText)))
        {
            using Value assigned = Value.New(new GType(TestNatives.CapsGetType()));
            assigned.SetBoxed(written.Handle);
            filter.SetProperty("caps", assigned);
        }

        using Value value = filter.GetProperty("caps");
        Assert.Throws<InvalidCastException>(() => value.GetMiniObject<TagList>());

        using Value number = Value.New(GType.Int);
        number.SetInt(7);
        Assert.Throws<InvalidCastException>(() => number.GetMiniObject<Caps>());
    }

    /// <summary>
    /// A mini object wrapper goes into a value as well as coming out of one,
    /// which is what lets a dynamic handler hand back what it was given.
    /// </summary>
    [Fact]
    public void AMiniObjectWrapperCanBeWrittenIntoAValue()
    {
        using Caps written = Assert.IsType<Caps>(Caps.FromString(CapsText));

        using (Value value = Value.CreateFor(written, new GType(TestNatives.CapsGetType())))
        {
            using Caps? read = value.GetMiniObject<Caps>();
            Assert.NotNull(read);
            Assert.Equal(CapsText, read.ToString());
        }

        // The value took a reference of its own and released it again, so the
        // wrapper of the caller is untouched by the round trip.
        Assert.False(written.IsDisposed);
        Assert.Equal(CapsText, written.ToString());
    }

    /// <summary>
    /// A mini object of the wrong type is refused rather than written into the
    /// value as if it were the right one.
    /// </summary>
    /// <remarks>
    /// This is the check <see cref="Value.CreateFor"/> already made for a
    /// <see cref="Boxed"/>. Both are boxed values to GObject and every mini
    /// object shares one copy function, so nothing would crash — the value
    /// would simply claim to hold a sample while holding a caps.
    /// </remarks>
    [Fact]
    public void AMiniObjectOfTheWrongTypeIsRefused()
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString(CapsText));

        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "mistyped"));

        // "new-preroll" is not it; "push-buffer" of an appsrc declares a
        // GstBuffer, which a caps is not.
        using Element source = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsrc", "mistyped-source"));

        ArgumentException mismatch = Assert.Throws<ArgumentException>(
            () => source.EmitSignal("push-buffer", caps));

        Assert.Contains("GstBuffer", mismatch.Message, StringComparison.Ordinal);

        // And it was refused before anything was emitted, so the wrapper still
        // owns what it owned.
        Assert.False(caps.IsDisposed);
        Assert.Equal(CapsText, caps.ToString());
    }

    /// <summary>
    /// A wrapper of another boxed type than the one the value holds is refused
    /// by both wrapper setters, and the value keeps what it held.
    /// </summary>
    /// <remarks>
    /// Asking whether the value holds a boxed value at all is not enough:
    /// <c>g_value_set_boxed</c> copies what it is handed with the copy function
    /// of the type the <em>value</em> holds, so a wrapper of another boxed type
    /// is not a refused write but the wrong function over a foreign pointer —
    /// <c>gst_mini_object_ref</c> over a <c>GstStructure</c> one way round and
    /// <c>gst_structure_copy</c> over a <c>GstCaps</c> the other. GLib says
    /// nothing on either path. This is the guard
    /// <see cref="ValueRef.SetBoxed(Boxed?)"/> already made, on the value the
    /// caller owns.
    /// </remarks>
    [Fact]
    public void AValueRefusesContentOfAnotherBoxedType()
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString(CapsText));
        using Structure payload = Structure.NewEmpty("payload");

        using Value capsValue = Value.New(new GType(TestNatives.CapsGetType()));
        capsValue.SetMiniObject(caps);

        InvalidOperationException refusedBoxed = Assert.Throws<InvalidOperationException>(
            () => capsValue.SetBoxed(payload));
        Assert.Contains("GstCaps", refusedBoxed.Message, StringComparison.Ordinal);
        Assert.Contains("GstStructure", refusedBoxed.Message, StringComparison.Ordinal);

        using Value payloadValue = Value.New(payload.BoxedType);
        payloadValue.SetBoxed(payload);

        InvalidOperationException refusedMiniObject = Assert.Throws<InvalidOperationException>(
            () => payloadValue.SetMiniObject(caps));
        Assert.Contains("GstStructure", refusedMiniObject.Message, StringComparison.Ordinal);
        Assert.Contains("GstCaps", refusedMiniObject.Message, StringComparison.Ordinal);

        // Both values hold what they always held, and both wrappers are the
        // caller's still.
        using Caps? heldCaps = capsValue.GetMiniObject<Caps>();
        Assert.NotNull(heldCaps);
        Assert.Equal(CapsText, heldCaps.ToString());

        using Structure? heldPayload = payloadValue.GetBoxed<Structure>();
        Assert.NotNull(heldPayload);
        Assert.Equal("payload", heldPayload.GetName());

        Assert.False(caps.IsDisposed);
        Assert.False(payload.IsDisposed);
    }

    /// <summary>
    /// The other half of the guard: content of the type the value holds goes
    /// through, and <see langword="null"/> clears the value without changing
    /// its type.
    /// </summary>
    [Fact]
    public void AValueAcceptsContentOfItsOwnTypeAndIsClearedByNull()
    {
        GType capsType = new(TestNatives.CapsGetType());

        using Caps written = Assert.IsType<Caps>(Caps.FromString(CapsText));
        using Structure payload = Structure.NewEmpty("payload");

        using Value capsValue = Value.New(capsType);
        capsValue.SetMiniObject(written);

        using (Caps? read = capsValue.GetMiniObject<Caps>())
        {
            Assert.NotNull(read);
            Assert.Equal(CapsText, read.ToString());
        }

        capsValue.SetMiniObject(null);
        Assert.Equal(capsType, capsValue.Type);
        Assert.Null(capsValue.GetMiniObject<Caps>());

        using Value payloadValue = Value.New(payload.BoxedType);
        payloadValue.SetBoxed(payload);

        using (Structure? read = payloadValue.GetBoxed<Structure>())
        {
            Assert.NotNull(read);
            Assert.Equal("payload", read.GetName());
        }

        payloadValue.SetBoxed(null);
        Assert.Equal(payload.BoxedType, payloadValue.Type);
        Assert.Equal(nint.Zero, payloadValue.GetBoxed());
    }

    /// <summary>
    /// A value that holds no boxed value at all refuses both wrapper setters,
    /// and so does one that holds nothing yet.
    /// </summary>
    /// <remarks>
    /// The mirror of the guard <see cref="ValueRef"/> makes before it looks at
    /// the content: a setter whose type the value does not hold throws rather
    /// than writing, which here would be <c>g_value_set_boxed</c> over the
    /// payload of an integer and a GLib critical.
    /// </remarks>
    [Fact]
    public void AValueThatHoldsNoBoxedValueRefusesBothWrapperSetters()
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString(CapsText));
        using Structure payload = Structure.NewEmpty("payload");

        using Value number = Value.New(GType.Int);
        number.SetInt(7);

        Assert.Contains(
            "gint",
            Assert.Throws<InvalidOperationException>(() => number.SetBoxed(payload)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "gint",
            Assert.Throws<InvalidOperationException>(() => number.SetMiniObject(caps)).Message,
            StringComparison.Ordinal);
        Assert.Equal(7, number.GetInt());

        Value empty = default;

        Assert.Contains(
            "not initialized",
            Assert.Throws<InvalidOperationException>(() => empty.SetBoxed(payload)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "not initialized",
            Assert.Throws<InvalidOperationException>(() => empty.SetMiniObject(null)).Message,
            StringComparison.Ordinal);
        Assert.True(empty.IsEmpty);
    }

    /// <summary>
    /// A caps that arrives as the argument of a dynamically connected signal
    /// is the wrapper, and it is borrowed: keeping it means copying it.
    /// </summary>
    /// <remarks>
    /// <c>typefind</c> is a plugin element that no <c>.gir</c> file describes,
    /// so <c>have-type</c> is only reachable through
    /// <see cref="Gst.GObject.Object.ConnectSignal"/>, and its second argument
    /// is a <c>GstCaps</c>. This is the path <c>samples/GstTypefind</c> walks.
    /// </remarks>
    [RequiresElementFact("typefind", "audiotestsrc", "wavenc", "fakesink")]
    public void ACapsSignalArgumentArrivesAsItsWrapper()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "audiotestsrc num-buffers=1 ! wavenc ! typefind name=finder ! fakesink"));

        using Element finder = Assert.IsAssignableFrom<Element>(pipeline.GetByName("finder"));

        string? described = null;
        Caps? kept = null;
        bool disposedInsideHandler = false;
        using ManualResetEventSlim fired = new(initialState: false);

        ulong handler = finder.ConnectSignal("have-type", (sender, args) =>
        {
            _ = sender;

            if (args.Length > 1 && args[1] is Caps caps)
            {
                described = caps.ToString();

                // The argument is lent to the handler, so keeping it is a copy.
                kept = caps.Copy();
                disposedInsideHandler = caps.IsDisposed;
            }

            fired.Set();
            return null;
        });

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Paused));

            // Bounded: ClockTime.None would wait for a preroll that a broken
            // installation never delivers, and hang the whole test run with it.
            StateChangeReturn reached = pipeline.GetState(
                out State _,
                out State _,
                ClockTime.FromSeconds(10));

            Assert.NotEqual(StateChangeReturn.Failure, reached);

            if (reached == StateChangeReturn.Async)
            {
                Assert.Fail("the pipeline did not reach PAUSED within 10 seconds");
            }

            Assert.True(fired.Wait(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            pipeline.SetState(State.Null);
            finder.RemoveHandler(handler);
        }

        Assert.False(disposedInsideHandler);
        Assert.NotNull(described);
        Assert.StartsWith("audio/x-wav", described, StringComparison.Ordinal);

        using (kept)
        {
            // The copy is still usable once the emission is long over, which is
            // what makes it a copy rather than the emission's own caps.
            Assert.NotNull(kept);
            Assert.Equal(described, kept.ToString());
        }
    }
}
