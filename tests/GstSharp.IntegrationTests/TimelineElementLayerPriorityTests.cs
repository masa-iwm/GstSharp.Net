using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// What a chain-up of <c>GES.TimelineElement::get_layer_priority</c> answers
/// directly below the base class, which leaves the slot empty.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class TimelineElementLayerPriorityTests
{
    /// <summary>
    /// A bare managed timeline element answers its own priority when it chains
    /// up, which is what the library answers for the empty slot, and nothing is
    /// reported through the exception trap on the way.
    /// </summary>
    /// <remarks>
    /// The absence of a report is half the witness: the branch used to throw an
    /// <see cref="InvalidOperationException"/>, which the callback boundary
    /// swallowed into a zero answer. The written priority is the other half,
    /// because zero is a real layer priority and would not tell the two apart.
    /// The value is written and read through the <c>priority</c> GObject
    /// property rather than the typed members, which are deprecated since 1.10
    /// and would fail the build; the property routes through the same slot
    /// (<c>ges-timeline-element.c:373-374</c>, <c>:337-338</c>).
    /// </remarks>
    [Fact]
    public void ADirectTimelineElementAnswersItsOwnPriorityOnChainUp()
    {
        using ProbeTimelineElement element = ProbeTimelineElement.New();

        List<Exception> reported = [];

        void OnFailure(Exception exception)
        {
            lock (reported)
            {
                reported.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            Assert.Equal(0u, element.GetLayerPriority());
            Assert.True(element.ChainedUp);

            element.SetProperty("priority", 7u);

            Assert.Equal(7u, element.GetProperty<uint>("priority"));
            Assert.Equal(7u, element.GetLayerPriority());
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        lock (reported)
        {
            Assert.Empty(reported);
        }
    }
}
