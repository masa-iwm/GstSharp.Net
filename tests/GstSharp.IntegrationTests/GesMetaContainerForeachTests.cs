using System.Runtime.CompilerServices;
using GES;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>GES.MetaContainerExtensions.Foreach</c>: the walk that is handed the
/// container it was started on.
/// </summary>
/// <remarks>
/// Everything here runs on the thread of the test, which is what the editing
/// services assert for a timeline.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesMetaContainerForeachTests
{
    /// <summary>
    /// Every field that was set is shown with its value, and the function is
    /// handed the very container the walk was started on.
    /// </summary>
    /// <remarks>
    /// A container carries metadata of its own, so the fields this test set are
    /// asserted by containment rather than by counting.
    /// </remarks>
    [Fact]
    public void EverySetMetaIsShownWithItsValue()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();

        Assert.True(timeline.SetString("gstsharp-string", "here"));
        Assert.True(timeline.SetInt("gstsharp-int", 42));
        Assert.True(timeline.SetBoolean("gstsharp-bool", true));

        Dictionary<string, object?> seen = [];
        IMetaContainer? shown = null;

        timeline.Foreach((container, key, value) =>
        {
            shown = container;
            switch (key)
            {
                case "gstsharp-string":
                    seen[key] = value.GetString();
                    break;
                case "gstsharp-int":
                    seen[key] = value.GetInt();
                    break;
                case "gstsharp-bool":
                    seen[key] = value.GetBoolean();
                    break;
                default:
                    break;
            }
        });

        Assert.Same(timeline, shown);
        Assert.Equal("here", Assert.Contains("gstsharp-string", seen));
        Assert.Equal(42, Assert.Contains("gstsharp-int", seen));
        Assert.Equal(true, Assert.Contains("gstsharp-bool", seen));
    }

    /// <summary>
    /// An exception the function throws is reported through the trap, and the
    /// walk goes on, because the C callback cannot end it.
    /// </summary>
    [Fact]
    public void AThrowingHandlerIsTrappedAndTheWalkGoesOn()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();

        Assert.True(timeline.SetString("gstsharp-string", "here"));
        Assert.True(timeline.SetInt("gstsharp-int", 42));
        Assert.True(timeline.SetBoolean("gstsharp-bool", true));

        InvalidOperationException thrown = new("The walk of the metadata threw.");
        int reports = 0;

        void OnFailure(Exception exception)
        {
            // The event is process wide, so only the instance this test threw
            // says anything about this test.
            if (ReferenceEquals(exception, thrown))
            {
                Interlocked.Increment(ref reports);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            timeline.Foreach((container, key, value) => throw thrown);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.True(reports >= 3, $"The trap saw {reports} reports.");
    }

    /// <summary>
    /// The state of the walk is rooted for as long as the library may call it,
    /// and released once the call returns.
    /// </summary>
    [Fact]
    public void TheDelegateSurvivesACollectionDuringTheWalk()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();

        Assert.True(timeline.SetString("gstsharp-string", "here"));
        Assert.True(timeline.SetInt("gstsharp-int", 42));
        Assert.True(timeline.SetBoolean("gstsharp-bool", true));

        (WeakReference Captured, int Calls) walk = RunCollectingWalk(timeline);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(walk.Calls >= 3, $"The walk called the function {walk.Calls} times.");
        Assert.False(walk.Captured.IsAlive);
    }

    /// <summary>
    /// Walks the container with a fresh function that collects while it runs,
    /// and answers a weak reference to the object the function captured.
    /// </summary>
    /// <param name="container">The container to walk.</param>
    /// <returns>The weak reference and the number of invocations.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Captured, int Calls) RunCollectingWalk(IMetaContainer container)
    {
        int[] calls = new int[1];
        WeakReference captured = new(calls);

        container.Foreach((c, key, value) =>
        {
            if (calls[0] == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            calls[0]++;
        });

        return (captured, calls[0]);
    }
}
