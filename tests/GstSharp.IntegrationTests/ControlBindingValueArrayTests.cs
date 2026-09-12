using Gst;
using Gst.Controller;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The two <c>get_g_value_array</c> members against the library that is
/// installed: a run of samples is taken in one call, into slots the caller
/// allocates and the call initialises.
/// </summary>
/// <remarks>
/// <c>volume</c> is the element under test for the reason
/// <see cref="ControllerModuleTests"/> gives: its <c>volume</c> property is a
/// writable, controllable double that ships in <c>gst-plugins-base</c>.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ControlBindingValueArrayTests
{
    /// <summary>
    /// A run of four samples across a linear ramp comes back as four doubles,
    /// and disposing the slots afterwards releases what the call initialised.
    /// </summary>
    [Fact]
    public void ABindingFillsEverySlotOfTheRun()
    {
        using Element element =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "sampled"));

        ControlBinding binding = NewRamp(element);

        Value[] values = new Value[4];
        try
        {
            Assert.True(binding.GetGValueArray(
                ClockTime.Zero,
                ClockTime.FromMilliseconds(250),
                values));

            for (int index = 0; index < values.Length; index++)
            {
                Assert.False(values[index].IsEmpty);
                Assert.Equal(0.2 + (0.15 * index), values[index].GetDouble(), 6);
            }
        }
        finally
        {
            for (int index = 0; index < values.Length; index++)
            {
                values[index].Dispose();
            }
        }
    }

    /// <summary>
    /// The same run taken off the object names the property instead of the
    /// binding, and answers the same values.
    /// </summary>
    [Fact]
    public void TheObjectTakesTheSameRunByPropertyName()
    {
        using Element element =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "sampled-by-name"));

        _ = NewRamp(element);

        Value[] values = new Value[4];
        try
        {
            Assert.True(element.GetGValueArray(
                "volume",
                ClockTime.Zero,
                ClockTime.FromMilliseconds(250),
                values));

            Assert.Equal(0.2, values[0].GetDouble(), 6);
            Assert.Equal(0.65, values[3].GetDouble(), 6);
        }
        finally
        {
            for (int index = 0; index < values.Length; index++)
            {
                values[index].Dispose();
            }
        }
    }

    /// <summary>
    /// A property that has no control binding answers nothing, and is not an
    /// error: the object looks the binding up and finds none.
    /// </summary>
    [Fact]
    public void APropertyWithNoBindingAnswersFalse()
    {
        using Element element =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "unbound"));

        Value[] values = new Value[2];
        try
        {
            Assert.False(element.GetGValueArray(
                "volume",
                ClockTime.Zero,
                ClockTime.FromMilliseconds(250),
                values));
        }
        finally
        {
            for (int index = 0; index < values.Length; index++)
            {
                values[index].Dispose();
            }
        }
    }

    /// <summary>
    /// A slot that already holds something is refused before the call is made:
    /// the call initialises every slot itself, and initialising a value twice
    /// is an assertion failure in GObject.
    /// </summary>
    [Fact]
    public void ASlotThatHoldsSomethingIsRefused()
    {
        using Element element =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "occupied"));

        ControlBinding binding = NewRamp(element);

        Value[] values = new Value[2];
        values[1] = Value.New(GType.Double);
        try
        {
            Assert.Throws<ArgumentException>(() => binding.GetGValueArray(
                ClockTime.Zero,
                ClockTime.FromMilliseconds(250),
                values));

            Assert.Throws<ArgumentException>(() => element.GetGValueArray(
                "volume",
                ClockTime.Zero,
                ClockTime.FromMilliseconds(250),
                values));
        }
        finally
        {
            for (int index = 0; index < values.Length; index++)
            {
                values[index].Dispose();
            }
        }
    }

    /// <summary>
    /// A time that holds nothing is refused: the C answers
    /// <c>GST_CLOCK_TIME_NONE</c> with an assertion failure on the console.
    /// </summary>
    [Fact]
    public void AClockTimeThatHoldsNothingIsRefused()
    {
        using Element element =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "timeless"));

        ControlBinding binding = NewRamp(element);

        Assert.Throws<ArgumentException>(() => binding.GetGValueArray(
            ClockTime.None,
            ClockTime.FromMilliseconds(250),
            new Value[2]));

        Assert.Throws<ArgumentException>(() => binding.GetGValueArray(
            ClockTime.Zero,
            ClockTime.None,
            new Value[2]));
    }

    /// <summary>
    /// An empty run is a call the C accepts, and the wrapper does not hand it
    /// the null pointer an empty span pins to.
    /// </summary>
    /// <remarks>
    /// The answer is <see langword="false"/>: the sampling loop of the
    /// interpolation source this test builds answers "something was filled
    /// in", and a run of no samples fills nothing. That answer alone proves
    /// nothing about the pointer, because the null one the library refuses is
    /// answered with <see langword="false"/> too — it is refused with an
    /// assertion failure, which is a critical on the log rather than an
    /// exception. The log is therefore what measures it: a call made with an
    /// address the library accepts logs no critical.
    /// </remarks>
    [Fact]
    public void AnEmptyRunIsACallAllTheSame()
    {
        using Element element =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "empty-run"));

        ControlBinding binding = NewRamp(element);

        bool answer = true;

        IReadOnlyList<string> logged = InitializeLogProbe.CaptureWhile(
            () => answer = binding.GetGValueArray(
                ClockTime.Zero,
                ClockTime.FromMilliseconds(250),
                System.Span<Value>.Empty));

        Assert.False(answer);

        if (InitializeLogProbe.IsInstalled)
        {
            Assert.DoesNotContain(
                logged,
                message => message.Contains("CRITICAL", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A name of nothing is refused before the call is made.
    /// </summary>
    [Fact]
    public void ANameOfNothingIsRefused()
    {
        using Element element =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "nameless"));

        Assert.Throws<ArgumentNullException>(() => element.GetGValueArray(
            null!,
            ClockTime.Zero,
            ClockTime.FromMilliseconds(250),
            new Value[2]));
    }

    /// <summary>
    /// Attaches an absolute binding that ramps <c>volume</c> from 0.2 to 0.8
    /// over one second, and answers it.
    /// </summary>
    private static ControlBinding NewRamp(Element element)
    {
        InterpolationControlSource source = InterpolationControlSource.New();
        source.Mode = InterpolationMode.Linear;

        Assert.True(source.Set(ClockTime.Zero, 0.2));
        Assert.True(source.Set(ClockTime.FromSeconds(1), 0.8));

        ControlBinding binding = DirectControlBinding.NewAbsolute(element, "volume", source);
        Assert.True(element.AddControlBinding(binding));

        return binding;
    }
}
