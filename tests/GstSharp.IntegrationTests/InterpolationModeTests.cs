using Gst;
using Gst.Controller;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The two cubic interpolation modes read directly off an
/// <see cref="InterpolationControlSource"/>, without a binding or an element
/// in between: what they answer at the control points, what the monotone one
/// guarantees between them, and what either answers before the first point.
/// </summary>
/// <remarks>
/// The difference between the two is the whole reason the monotone mode
/// exists. An ordinary cubic spline through points that only ever rise may
/// still dip below or shoot above them between two of them; the monotone one
/// chooses its tangents so that it cannot
/// (gstinterpolationcontrolsource.c:520, <c>_interpolate_cubic_monotonic</c>,
/// over the cache <c>_interpolate_cubic_monotonic_update_cache</c> builds at
/// :444). That is a property a test can state exactly, so it is the one that
/// is stated here rather than a table of expected values.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class InterpolationModeTests
{
    private const double Tolerance = 1e-9;

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public InterpolationModeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Both cubic modes pass through the control points they were given, the
    /// monotone one stays inside the range of points that only rise, and
    /// neither answers anything at all before the first point.
    /// </summary>
    /// <remarks>
    /// A timestamp before the first control point has no pair of points around
    /// it: <c>_get_nearest_control_points</c>
    /// (gstinterpolationcontrolsource.c:56) finds no iterator and answers
    /// FALSE, which every mode passes straight on to its caller. A source with
    /// two points takes the same route through
    /// <c>interpolate_linear_get</c>, which both cubic modes fall back to
    /// while there are no more than two values
    /// (gstinterpolationcontrolsource.c:381 and :550).
    /// </remarks>
    [Fact]
    public void CubicModesPassThroughTheControlPointsAndStayMonotoneBetweenMonotonePoints()
    {
        (ClockTime Time, double Value)[] points =
        [
            (ClockTime.Zero, 0.0),
            (ClockTime.FromSeconds(1), 0.5),
            (ClockTime.FromSeconds(2), 1.0),
            (ClockTime.FromSeconds(3), 1.0),
        ];

        InterpolationControlSource source = InterpolationControlSource.New();

        foreach ((ClockTime time, double value) in points)
        {
            Assert.True(source.Set(time, value));
        }

        foreach (InterpolationMode mode in new[] { InterpolationMode.Cubic, InterpolationMode.CubicMonotonic })
        {
            source.Mode = mode;

            foreach ((ClockTime time, double value) in points)
            {
                Assert.True(source.TryGetValue(time, out double at));
                Assert.Equal(value, at, Tolerance);
            }
        }

        // The monotone mode over points that only rise: every sample stays in
        // the range of the points and never goes back down.
        source.Mode = InterpolationMode.CubicMonotonic;

        double previous = double.NegativeInfinity;

        for (int step = 0; step <= 30; step++)
        {
            ClockTime time = ClockTime.FromMilliseconds((ulong)(step * 100));

            Assert.True(source.TryGetValue(time, out double value));
            Assert.True(
                value is >= 0.0 and <= 1.0,
                $"the monotone cubic left the range of its control points at {step * 100} ms: {value}.");
            Assert.True(
                value >= previous,
                $"the monotone cubic went back down at {step * 100} ms: {value} after {previous}.");

            previous = value;
        }

        _output.WriteLine($"monotone cubic ended at {previous}");

        // Before the first point there is nothing to interpolate between, in
        // either mode and with either number of points.
        InterpolationControlSource later = InterpolationControlSource.New();
        Assert.True(later.Set(ClockTime.FromSeconds(1), 0.25));
        Assert.True(later.Set(ClockTime.FromSeconds(2), 0.75));

        foreach (InterpolationMode mode in new[] { InterpolationMode.Cubic, InterpolationMode.CubicMonotonic })
        {
            later.Mode = mode;
            Assert.False(later.TryGetValue(ClockTime.FromMilliseconds(500), out _));

            source.Mode = mode;
            Assert.True(source.TryGetValue(ClockTime.Zero, out double first));
            Assert.Equal(0.0, first, Tolerance);
        }
    }
}
