using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;
using SystemTask = System.Threading.Tasks.Task;

namespace GstSharp.IntegrationTests;

/// <summary>
/// What happens when several threads register managed subclasses and wrap
/// native objects at the same time.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class SubclassStressTests
{
    private static int _stressCounter;

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassStressTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Registrations and wrappings that run at the same time do not deadlock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the stress test §9, risk 2 of <c>docs/subclassing.md</c> asks
    /// for. Two locks are involved and they are taken in opposite directions by
    /// the two halves of this test: registering takes the lock of the registry
    /// and then, through <c>g_type_class_ref</c>, the type lock of GObject,
    /// with the class initialiser running under both; wrapping takes the
    /// interning lock of <c>Gst.GObject.Object</c> and calls into GObject under
    /// it. It is safe only because the class initialiser touches no managed
    /// lock — <see cref="ClassConfig"/> speaks to raw handles and never creates
    /// a wrapper — and a run that hangs is what would say otherwise.
    /// </para>
    /// <para>
    /// The types registered here stay registered for the process, which is why
    /// their names come from a counter.
    /// </para>
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async SystemTask ConcurrentRegistrationsAndWrappingsDoNotDeadlock()
    {
        const int Threads = 8;
        const int Rounds = 6;

        using Barrier start = new(Threads * 2);
        List<SystemTask> work = [];

        for (int i = 0; i < Threads; i++)
        {
            work.Add(SystemTask.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait();

                    for (int round = 0; round < Rounds; round++)
                    {
                        string name = FormattableString.Invariant(
                            $"GstSharpTestStress{Interlocked.Increment(ref _stressCounter)}");

                        SubclassType type = Element.DefineSubclass(
                            name,
                            config => config.SetMetadata(name, "Testing", "A registration under load", "tests"),
                            Element.ChangeStateOverride);

                        using StressElement element = new(type);
                        Assert.Equal(StateChangeReturn.Success, element.SetState(State.Ready));
                        Assert.Equal(StateChangeReturn.Success, element.SetState(State.Null));
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));

            work.Add(SystemTask.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait();

                    for (int round = 0; round < Rounds * 4; round++)
                    {
                        // Wrapping takes the interning lock and calls into
                        // GObject under it, which is the other half of the
                        // lock order this test is about.
                        using Pipeline pipeline = Pipeline.New(null);
                        Bus bus = pipeline.GetBus();
                        Assert.NotNull(bus);

                        using ProbeElement probe = new();
                        Assert.Equal(StateChangeReturn.Success, probe.SetState(State.Ready));
                        Assert.Equal(StateChangeReturn.Success, probe.SetState(State.Null));
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));
        }

        SystemTask all = SystemTask.WhenAll(work);
        bool finished = await SystemTask.WhenAny(all, SystemTask.Delay(TimeSpan.FromSeconds(60))) == all;

        _output.WriteLine(FormattableString.Invariant(
            $"registered {_stressCounter} types under load, finished={finished}"));

        Assert.True(finished, "Concurrent registrations and wrappings did not finish, which is a deadlock.");

        // Awaiting the completed task is what surfaces whatever the workers
        // threw, rather than leaving it on a faulted task nobody looks at.
        await all;
    }

    /// <summary>
    /// A managed element whose registration is handed to it, so that the stress
    /// test can build one per type it registers.
    /// </summary>
    /// <remarks>
    /// It declares <c>change_state</c> and overrides nothing, which is the
    /// harmless half of the drift of §9, risk 1: the base implementation of
    /// <see cref="Element.OnChangeState"/> chains up, so the element behaves
    /// exactly as it would without the declaration and only pays a managed
    /// transition for it.
    /// </remarks>
    private sealed class StressElement : Element
    {
        internal StressElement(SubclassType type)
            : base(type.NewInstance())
        {
        }
    }
}
