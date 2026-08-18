using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.Controller;

/// <summary>
/// A control source that answers a periodic waveform, the
/// <c>GstLFOControlSource</c> of <c>libgstcontroller</c>.
/// </summary>
/// <remarks>
/// <para>
/// There are no control points here: the value at a timestamp is
/// <see cref="Offset"/> plus <see cref="Amplitude"/> times the
/// <see cref="Waveform"/> at that point of the period, and the period is one
/// over <see cref="Frequency"/> seconds. <see cref="Timeshift"/> moves the
/// waveform to the right. That is why this class derives from
/// <see cref="Gst.ControlSource"/> and not from
/// <see cref="TimedValueControlSource"/>: the native
/// <c>GstLFOControlSource</c> derives straight from <c>GstControlSource</c> as
/// well.
/// </para>
/// <code>
/// LFOControlSource source = LFOControlSource.New();
/// source.Waveform = LFOWaveform.Sine;
/// source.Frequency = 2.0;      // two periods per second
/// source.Amplitude = 0.5;
/// source.Offset = 0.5;         // so the values run across [0, 1]
///
/// element.AddControlBinding(DirectControlBinding.NewAbsolute(element, "volume", source));
/// </code>
/// <para>
/// <b>Set all five properties.</b> A control source is expected to answer
/// values in <c>[0, 1]</c>, and a fresh source does not: it swings from
/// <c>-1</c> to <c>1</c>, because its amplitude starts at <c>1.0</c> and its
/// offset at <c>0</c> — the parameter specification of <c>offset</c> declares a
/// default of <c>1.0</c>, but the constructor of GStreamer 1.28 never applies
/// it. An amplitude and an offset that add up to at most one is what keeps a
/// source inside the expected range; what leaves it is clamped by the control
/// binding.
/// </para>
/// <para>
/// The four numeric properties are themselves marked controllable, and the
/// source synchronises its own properties before it evaluates a timestamp, so
/// an LFO can be driven by a second control source — an amplitude that grows
/// over the first second, for example — through an ordinary
/// <see cref="DirectControlBinding"/> on the source itself.
/// </para>
/// </remarks>
public sealed unsafe partial class LFOControlSource : Gst.ControlSource
{
    /// <summary>
    /// The shortest period the native source accepts is one nanosecond, so this
    /// many periods per second is the highest frequency it accepts.
    /// </summary>
    private const double HighestFrequency = 1_000_000_000.0;

    /// <summary>
    /// Wraps a native <c>GstLFOControlSource</c>.
    /// </summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal LFOControlSource(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets or sets the shape of the periodic waveform.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A fresh source reads back as <see cref="LFOWaveform.Square"/> while it
    /// answers a sine.</b> The native constructor installs the sine and then
    /// stores the <em>success</em> of doing so in the field the property reads,
    /// which is a one in GStreamer 1.28 and therefore a square. Writing the
    /// property once puts the two back in agreement, which is one more reason to
    /// set the shape rather than to inherit it.
    /// </para>
    /// <para>
    /// This is the <c>waveform</c> property, read and written through
    /// <see cref="Gst.GObject.Object.GetProperty{T}(string)"/> and
    /// <see cref="Gst.GObject.Object.SetProperty(string, object?)"/>, which
    /// convert against the type the property declares — an enumeration here, a
    /// <c>gdouble</c> or a <c>guint64</c> for the four below. That pair is
    /// public surface of <c>GstSharp.Net</c> and needs no <c>GValue</c> of its
    /// own; <see cref="InterpolationControlSource.Mode"/> keeps the longer round
    /// trip because it is the worked example of one.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public LFOWaveform Waveform
    {
        get => GetProperty<LFOWaveform>("waveform");
        set => SetProperty("waveform", value);
    }

    /// <summary>
    /// Gets or sets how many periods of the waveform fit into a second.
    /// </summary>
    /// <remarks>
    /// The period is one over this, so <c>2.0</c> is a period of half a second.
    /// The native source keeps the period as whole nanoseconds and refuses a
    /// frequency whose period would be shorter than one, which is the upper
    /// bound below.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The frequency is not positive, or its period would be shorter than a
    /// nanosecond.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public double Frequency
    {
        get => GetProperty<double>("frequency");

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, HighestFrequency);
            SetProperty("frequency", value);
        }
    }

    /// <summary>
    /// Gets or sets how far the waveform is moved to the right.
    /// </summary>
    /// <remarks>
    /// A timeshift of a quarter of the period turns a sine into a cosine. It is
    /// taken modulo the period, so any value is meaningful, and a shift to the
    /// left is the period minus the shift to the right.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.ClockTime Timeshift
    {
        get => new(GetProperty<ulong>("timeshift"));
        set => SetProperty("timeshift", value.Nanoseconds);
    }

    /// <summary>
    /// Gets or sets how far the waveform swings either side of
    /// <see cref="Offset"/>.
    /// </summary>
    /// <remarks>
    /// The range is the <c>[0, 1]</c> the property declares. Writing outside it
    /// is refused here rather than left to GObject, which answers such a write
    /// with a warning on the console and a silent clamp.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The amplitude is outside <c>[0, 1]</c>.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public double Amplitude
    {
        get => GetProperty<double>("amplitude");

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1.0);
            SetProperty("amplitude", value);
        }
    }

    /// <summary>
    /// Gets or sets the value the waveform swings around.
    /// </summary>
    /// <remarks>The range is the <c>[0, 1]</c> the property declares, as above.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The offset is outside <c>[0, 1]</c>.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public double Offset
    {
        get => GetProperty<double>("offset");

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1.0);
            SetProperty("offset", value);
        }
    }

    /// <summary>
    /// Asks the control source for its value at one timestamp.
    /// </summary>
    /// <param name="timestamp">The time to evaluate at.</param>
    /// <param name="value">The value of the source at that time.</param>
    /// <returns>
    /// <see langword="true"/>, which is what a waveform always answers: it is
    /// defined at every timestamp.
    /// </returns>
    /// <remarks>
    /// This is <see cref="TimedValueControlSource.TryGetValue"/> under the name
    /// the module gives it on every control source, and it is the same inherited
    /// <see cref="Gst.ControlSource.ControlSourceGetValue"/> underneath. The
    /// value is the raw one, before the control binding maps it onto the range
    /// of a property.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public bool TryGetValue(Gst.ClockTime timestamp, out double value) =>
        ControlSourceGetValue(timestamp, out value);

    /// <summary>
    /// Creates a new LFO control source.
    /// </summary>
    /// <returns>
    /// A source that answers a sine of one period per second with an amplitude
    /// of <c>1.0</c> and no offset, so it runs from <c>-1</c> to <c>1</c> until
    /// its properties are set. See <see cref="Waveform"/> for what it reports
    /// about itself in the meantime.
    /// </returns>
    /// <remarks>
    /// The wrapper is produced by
    /// <see cref="Gst.GObject.Object.FromNative{T}(nint, Transfer)"/> rather
    /// than by calling the constructor, which is the supported way to turn a
    /// handle into a wrapper and the reason this type appears in the type
    /// registry of the module.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The module was not registered, so the type registry could not build the
    /// wrapper. Calling this method is a call into this assembly and runs its
    /// module initialiser, so this should not happen.
    /// </exception>
    public static LFOControlSource New()
    {
        nint handle = GstLFOControlSourceNew();

        return Object.FromNative<LFOControlSource>(handle, Transfer.Full)
            ?? throw new InvalidOperationException(
                "gst_lfo_control_source_new did not produce a wrapper. The type registry has no entry for " +
                "GstLFOControlSource, which means the module of Gst.Controller did not register.");
    }

    /// <summary>
    /// Returns the <c>GType</c> that GObject registered
    /// <c>GstLFOControlSource</c> under.
    /// </summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("GstController", EntryPoint = "gst_lfo_control_source_get_type")]
    internal static partial nuint GetGType();

    /// <summary>
    /// Creates the wrapper of a native instance, for the type registry.
    /// </summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) =>
        new LFOControlSource(handle, transfer);

    [LibraryImport("GstController", EntryPoint = "gst_lfo_control_source_new")]
    private static partial nint GstLFOControlSourceNew();
}
