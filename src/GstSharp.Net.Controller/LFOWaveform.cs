namespace Gst.Controller;

/// <summary>
/// The periodic waveform an <see cref="LFOControlSource"/> answers.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>GstLFOWaveform</c>. The values are the ones the C enumeration
/// declares, because the waveform travels through the <c>waveform</c> property
/// as a plain enumeration value.
/// </para>
/// <para>
/// Every waveform runs around <see cref="LFOControlSource.Offset"/> and reaches
/// <see cref="LFOControlSource.Offset"/> plus or minus
/// <see cref="LFOControlSource.Amplitude"/>, so the values only stay inside the
/// <c>[0, 1]</c> a control source is expected to answer while the two of them
/// add up to at most one.
/// </para>
/// </remarks>
public enum LFOWaveform
{
    /// <summary>
    /// A sine that starts at the offset, rises through the first quarter of the
    /// period and falls back through the third.
    /// </summary>
    Sine = 0,

    /// <summary>
    /// A square that holds the offset minus the amplitude for the first half of
    /// the period and the offset plus the amplitude for the second.
    /// </summary>
    Square = 1,

    /// <summary>
    /// A falling ramp: the offset plus the amplitude at the start of the period,
    /// straight down to the offset minus the amplitude at its end.
    /// </summary>
    Saw = 2,

    /// <summary>
    /// A rising ramp, which is <see cref="Saw"/> upside down: the offset minus
    /// the amplitude at the start of the period, straight up to the offset plus
    /// the amplitude at its end.
    /// </summary>
    ReverseSaw = 3,

    /// <summary>
    /// A triangle that starts at the offset, reaches the offset plus the
    /// amplitude after a quarter of the period, the offset minus the amplitude
    /// after three quarters, and is back at the offset at the end.
    /// </summary>
    Triangle = 4,
}
