namespace Gst.Video;

/// <summary>
/// What a managed element implements to be a <c>GstColorBalance</c>, the
/// interface through which an application or <c>playsink</c> adjusts the
/// brightness, contrast, hue or saturation of the video an element processes.
/// </summary>
/// <remarks>
/// <para>
/// All four members answer for one element, and every one of them can be
/// called from any thread: the application thread, the streaming thread of
/// the pipeline, or the thread <c>playsink</c> sets its own channels up on.
/// They have to be safe to call concurrently.
/// </para>
/// <para>
/// Declaring the interface to GObject is a separate step:
/// <c>Gst.Video.ColorBalanceImplementation.For&lt;TSelf&gt;()</c> builds the
/// entry that goes into <c>SubclassOptions.Interfaces</c> when the subclass is
/// defined. Implementing this interface alone attaches nothing.
/// </para>
/// <para>
/// The consumer surface is the generated one: an element of the managed type
/// is presented as <see cref="IColorBalance"/> by <c>As&lt;IColorBalance&gt;()</c>,
/// and <see cref="ColorBalanceExtensions.ValueChanged"/> is how an
/// implementation announces a change.
/// </para>
/// </remarks>
public interface IColorBalanceImplementation
{
    /// <summary>
    /// Gets whether the element balances colors in dedicated hardware or in
    /// software.
    /// </summary>
    /// <remarks>
    /// <c>playsink</c> prefers an element that answers
    /// <see cref="ColorBalanceType.Hardware"/> when it finds more than one that
    /// offers the four channels it proxies.
    /// </remarks>
    ColorBalanceType BalanceType { get; }

    /// <summary>
    /// Returns the channels the element offers, such as <c>BRIGHTNESS</c> or
    /// <c>HUE</c>.
    /// </summary>
    /// <returns>The channels, in the order they are listed.</returns>
    /// <remarks>
    /// <para>
    /// Answer channels from <see cref="ColorBalanceChannel.New"/> that the
    /// element keeps, and the same instances on every call for as long as the
    /// set does not change. <c>gst_color_balance_list_channels</c> lends its
    /// caller a list the element owns, so the runtime builds that list, keeps
    /// a reference to every channel in it, and hands it out again for as long
    /// as the answer here lists the same channels in the same order. An answer
    /// that differs gets a new list; the previous ones stay valid until the
    /// element is finalized, because a caller on another thread may still be
    /// walking one of them. <strong>An implementation that makes new channels
    /// on every call therefore leaks</strong> a list, and a reference to every
    /// channel in it, per call until the element goes, and <c>playsink</c>
    /// asks on every value it sets.
    /// </para>
    /// <para>
    /// <strong>The labels are not decoration.</strong> <c>playsink</c> uses an
    /// element only when it offers channels whose labels contain
    /// <c>BRIGHTNESS</c>, <c>CONTRAST</c>, <c>HUE</c> and <c>SATURATION</c>, and
    /// it finds its channel again by that substring and asserts that it did
    /// (<c>g_assert (channel)</c>, <c>gstplaysink.c:1720</c> when it sets its
    /// video chain up and <c>:5548</c> whenever a value is set on one of its
    /// own channels). An element whose channels stop
    /// carrying the label <c>playsink</c> chose it for aborts the process
    /// there, which nothing in this binding can prevent.
    /// </para>
    /// <para>
    /// An empty list is how C spells no channels. An answer that is
    /// <see langword="null"/> or holds a <see langword="null"/> or disposed
    /// entry, or a member that throws, is
    /// reported to <see cref="Gst.Interop.ExceptionTrap"/>, and the caller is
    /// given the list answered before, so a channel it already found does not
    /// disappear from under it.
    /// </para>
    /// </remarks>
    IReadOnlyList<ColorBalanceChannel> ListChannels();

    /// <summary>
    /// Sets the value of one channel.
    /// </summary>
    /// <param name="channel">
    /// One of the channels <see cref="ListChannels"/> answered, as the caller
    /// passed it. The runtime hands over the wrapper the element holds, so it
    /// can be compared by reference as long as the element has not disposed
    /// it.
    /// </param>
    /// <param name="value">
    /// The new value, which the caller is asked to keep between
    /// <see cref="ColorBalanceChannel.MinValue"/> and
    /// <see cref="ColorBalanceChannel.MaxValue"/>. Nothing enforces that, so
    /// clamp or ignore a value outside the range.
    /// </param>
    /// <remarks>
    /// <para>
    /// GStreamer fires no notification on its own: an implementation that
    /// changed the value calls <see cref="ColorBalanceExtensions.ValueChanged"/>
    /// on itself with the value it now has, which emits the
    /// <c>value-changed</c> signal of the element and that of the channel.
    /// <c>playsink</c> listens to the former to keep its own channels in step.
    /// </para>
    /// <para>
    /// A member that throws is reported to
    /// <see cref="Gst.Interop.ExceptionTrap"/>; the caller learns nothing,
    /// since the C function returns nothing.
    /// </para>
    /// </remarks>
    void SetValue(ColorBalanceChannel channel, int value);

    /// <summary>
    /// Returns the value of one channel.
    /// </summary>
    /// <param name="channel">
    /// One of the channels <see cref="ListChannels"/> answered, as the caller
    /// passed it.
    /// </param>
    /// <returns>
    /// The current value, between <see cref="ColorBalanceChannel.MinValue"/>
    /// and <see cref="ColorBalanceChannel.MaxValue"/>.
    /// </returns>
    /// <remarks>
    /// A member that throws is reported to
    /// <see cref="Gst.Interop.ExceptionTrap"/>, and the caller is told the
    /// minimum value of the channel, which is what
    /// <c>gst_color_balance_get_value</c> answers for an element that
    /// implements no <c>get_value</c>.
    /// </remarks>
    int GetValue(ColorBalanceChannel channel);
}
