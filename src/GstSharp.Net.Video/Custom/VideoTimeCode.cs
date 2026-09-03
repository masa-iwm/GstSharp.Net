namespace Gst.Video;

public sealed unsafe partial class VideoTimeCode
{
    /// <summary>Gets the numerator of the frame rate the time code counts in.</summary>
    /// <value>The <c>fps_n</c> of the configuration the time code embeds.</value>
    /// <remarks>
    /// The value is copied out of the configuration at the moment of the call
    /// and is the one <see cref="New"/>, <see cref="NewFromDateTime"/> or
    /// <see cref="Init"/> was given; the configuration itself belongs to the
    /// time code and is not handed out.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public uint FpsN
    {
        get
        {
            uint value = ((VideoTimeCodeRaw*)Handle)->Config.FpsN;
            System.GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the denominator of the frame rate the time code counts in.</summary>
    /// <value>The <c>fps_d</c> of the configuration the time code embeds.</value>
    /// <remarks>
    /// The value is copied out of the configuration at the moment of the call
    /// and is the one <see cref="New"/>, <see cref="NewFromDateTime"/> or
    /// <see cref="Init"/> was given.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public uint FpsD
    {
        get
        {
            uint value = ((VideoTimeCodeRaw*)Handle)->Config.FpsD;
            System.GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the flags the time code was configured with.</summary>
    /// <value>The <c>flags</c> of the configuration the time code embeds.</value>
    /// <remarks>
    /// The value is copied out of the configuration at the moment of the call
    /// and is the one <see cref="New"/>, <see cref="NewFromDateTime"/> or
    /// <see cref="Init"/> was given. <see cref="VideoTimeCodeFlags.DropFrame"/>
    /// is the one that changes how the frame count wraps.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public Gst.Video.VideoTimeCodeFlags Flags
    {
        get
        {
            Gst.Video.VideoTimeCodeFlags value = ((VideoTimeCodeRaw*)Handle)->Config.Flags;
            System.GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the daily jam the time code counts from.</summary>
    /// <returns>
    /// The instant the time code counts from, or <see langword="null"/> when it
    /// carries no daily jam.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the <c>latest_daily_jam</c> of the configuration the time code
    /// embeds. What comes back <b>owns a reference of its own</b>, which is why
    /// this is a method the caller disposes rather than a property: the instant
    /// stays valid after the time code is gone, and disposing it leaves the time
    /// code holding the reference it took when it was built.
    /// </para>
    /// <para>
    /// No daily jam is a normal answer: <see cref="New"/> accepts
    /// <see langword="null"/> for one, and a time code without one has no
    /// instant to convert into, which is what <see cref="ToDateTime"/> reports
    /// as <see langword="null"/> as well.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public Gst.GLib.DateTime? GetLatestDailyJam()
    {
        Gst.GLib.DateTime? value = Gst.GLib.DateTime.FromNative(
            ((VideoTimeCodeRaw*)Handle)->Config.LatestDailyJam,
            Gst.Interop.Transfer.None);
        System.GC.KeepAlive(this);
        return value;
    }
}
