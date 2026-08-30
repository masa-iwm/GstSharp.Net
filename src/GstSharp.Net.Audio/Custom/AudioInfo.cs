namespace Gst.Audio;

public sealed unsafe partial class AudioInfo
{
    /// <summary>Gets the audio format the information describes.</summary>
    /// <value>
    /// The format, or <see cref="AudioFormat.Unknown"/> for an instance that
    /// carries no format description.
    /// </value>
    /// <remarks>
    /// <para>
    /// This is <c>GST_AUDIO_INFO_FORMAT</c>, which reads the <c>format</c> of
    /// the format description the instance points at. The description itself is
    /// <see cref="FormatInfo"/>.
    /// </para>
    /// <para>
    /// Every way of obtaining an instance runs <c>gst_audio_info_init</c>
    /// first, which assigns the description of
    /// <see cref="AudioFormat.Unknown"/> (audio-info.c, unchanged since 1.24),
    /// so the pointer is never null in practice. The unknown answer is what a
    /// block of zeroed memory would produce, and it is spelled out here rather
    /// than dereferenced.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public Gst.Audio.AudioFormat Format
    {
        get
        {
            nint finfo = ((AudioInfoRaw*)Handle)->Finfo;
            Gst.Audio.AudioFormat value = finfo == 0
                ? Gst.Audio.AudioFormat.Unknown
                : ((AudioFormatInfoRaw*)finfo)->Format;
            System.GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the description of the format the information describes.</summary>
    /// <value>The format description, which is never <see langword="null"/>.</value>
    /// <remarks>
    /// <para>
    /// This is the <c>finfo</c> field. The wrapper is <b>borrowed</b>: the
    /// description belongs to GStreamer, which keeps one per format for the
    /// life of the process, and the wrapper does not take part in its
    /// ownership. It is safe to keep, and reading through it says nothing about
    /// this <see cref="AudioInfo"/>, which may have moved on to another format
    /// by then.
    /// </para>
    /// <para>
    /// A missing description is not a normal answer: every way of obtaining
    /// an instance runs <c>gst_audio_info_init</c>, which assigns one, so a
    /// block of zeroed memory is the only thing that carries none. That is
    /// reported as an exception rather than handed out as a null every call
    /// site would have to carry forever, which is what
    /// <see cref="AudioFormatExtensions.GetInfo(AudioFormat)"/> does for the
    /// same native object.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The instance carries no format description.
    /// </exception>
    public Gst.Audio.AudioFormatInfo FormatInfo
    {
        get
        {
            nint finfo = ((AudioInfoRaw*)Handle)->Finfo;
            Gst.Audio.AudioFormatInfo value = Gst.Audio.AudioFormatInfo.FromNative(finfo)
                ?? throw new InvalidOperationException("GstAudioInfo carries no format description.");
            System.GC.KeepAlive(this);
            return value;
        }
    }
}
