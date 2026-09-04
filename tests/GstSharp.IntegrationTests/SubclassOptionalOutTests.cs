using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The out parameters a caller of a slot is allowed to leave out.
/// </summary>
/// <remarks>
/// <c>gst_element_get_state (element, NULL, NULL, timeout)</c> is the ordinary
/// way to wait for an element, and gstelement.c forwards the two pointers to
/// the slot exactly as it was given them, so a managed element that overrides
/// <c>get_state</c> is called with NULL by application code it never sees. The
/// slot is called through the class struct here, the way GStreamer calls it.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class SubclassOptionalOutTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassOptionalOutTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Both pointers NULL: the values the override produced have nowhere to go
    /// and are dropped, and the answer of the slot still arrives.
    /// </summary>
    [Fact]
    public void TheGetStateOverrideToleratesTwoNullOutPointers()
    {
        using ProbeStateElement element = new();

        StateChangeReturn answered = CallGetState(element, null, null);

        _output.WriteLine(FormattableString.Invariant($"get_state (NULL, NULL) answered {answered}"));
        Assert.Equal(StateChangeReturn.NoPreroll, answered);
        Assert.Equal(1, element.Calls);
    }

    /// <summary>
    /// One pointer of the two, which is the other half of the guard: the
    /// storage that was passed is written and the missing one is not.
    /// </summary>
    [Fact]
    public void TheGetStateOverrideWritesTheStorageItWasGiven()
    {
        using ProbeStateElement element = new();

        int state = -1;
        int pending = -1;
        StateChangeReturn both = CallGetState(element, &state, &pending);

        Assert.Equal(StateChangeReturn.NoPreroll, both);
        Assert.Equal((int)State.Paused, state);
        Assert.Equal((int)State.Playing, pending);

        state = -1;
        pending = -1;
        StateChangeReturn one = CallGetState(element, &state, null);

        Assert.Equal(StateChangeReturn.NoPreroll, one);
        Assert.Equal((int)State.Paused, state);
        Assert.Equal(-1, pending);
        Assert.Equal(2, element.Calls);
    }

    private static StateChangeReturn CallGetState(Element element, int* state, int* pending)
    {
        Gst.ElementClassRaw* klass = (Gst.ElementClassRaw*)ClassOf(element);

        Assert.NotEqual(nint.Zero, klass->GetState);
        return (StateChangeReturn)
            ((delegate* unmanaged[Cdecl]<nint, int*, int*, ulong, int>)klass->GetState)(
                element.Handle, state, pending, ClockTime.NoneValue);
    }

    private static nint ClassOf(Gst.GObject.Object instance) => *(nint*)instance.Handle;
}

/// <summary>
/// A managed element whose <c>get_state</c> override answers two states of its
/// own without chaining up.
/// </summary>
internal sealed class ProbeStateElement : Element
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeStateElement";

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        config => config.SetMetadata(
            "GstSharp probe state element",
            "Generic/Testing",
            "Answers a fixed pair of states",
            "GstSharp.Net integration tests"),
        GetStateOverride);

    /// <summary>Creates a managed element.</summary>
    internal ProbeStateElement()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how often the slot was called.</summary>
    internal int Calls { get; private set; }

    /// <inheritdoc/>
    protected override StateChangeReturn OnGetState(out State state, out State pending, ClockTime timeout)
    {
        Calls++;
        state = State.Paused;
        pending = State.Playing;
        return StateChangeReturn.NoPreroll;
    }
}
