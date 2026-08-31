using Gst.GLib;
using Gst.GObject;

namespace Gst.Transcoder;

/// <content>
/// The two parses of the API bus that read a field the transcoder does not
/// always post.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_transcoder_message_parse_error</c> and
/// <c>gst_transcoder_message_parse_warning</c> are the two members of the
/// family that read more than one field, and both read them through the
/// <c>PARSE_MESSAGE_FIELD</c> macro of <c>gsttranscoder.c</c>, whose miss
/// branch is <c>g_error()</c> — which aborts the process. The transcoder posts
/// an error with the <c>error</c> field alone on four paths, none of which
/// attaches the <c>issue-details</c> the second half of the parse then demands,
/// so the imported call would abort on exactly the messages the API exists to
/// report.
/// </para>
/// <para>
/// The members below read the structure of the message themselves and answer
/// what is there: the error as a <see cref="GException"/> copied out of the
/// boxed <c>GError</c>, and the details as a structure of the caller's own or
/// as <see langword="null"/> when the field is absent. A message that is not a
/// transcoder message, or one that is a transcoder message of another kind, is
/// an <see cref="ArgumentException"/> rather than an abort.
/// </para>
/// </remarks>
public static unsafe partial class TranscoderMessageExtensions
{
    /// <summary>The name every message of the API bus carries.</summary>
    private const string MessageDataName = "gst-transcoder-message-data";

    /// <summary>The field that says which kind of message it is.</summary>
    private const string MessageTypeField = "transcoder-message-type";

    /// <summary>The field that carries the details of an error or a warning.</summary>
    private const string IssueDetailsField = "issue-details";

    /// <summary>
    /// Reads which kind of message a message of the API bus is.
    /// </summary>
    /// <param name="msg">The message, as it came off the API bus.</param>
    /// <returns>The kind of message, which says which parse below applies.</returns>
    /// <remarks>
    /// <para>
    /// The kind is a field of the payload structure whose name lives in a
    /// private header of the library, so C has no accessor for it either: an
    /// application that reads the API bus itself rather than through a
    /// <see cref="TranscoderSignalAdapter"/> has to spell the field. This is
    /// that read, done once and in the one place that knows the name, and it
    /// is what makes the polled route of
    /// <see cref="Transcoder.GetMessageBus"/> usable —
    /// <see cref="Transcoder.IsTranscoderMessage"/> says that a message
    /// belongs to a transcoder, and this says which of the six it is.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="msg"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="msg"/> is not a message of a transcoder API bus.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="msg"/> was disposed.</exception>
    public static TranscoderMessage ParseType(Gst.Message msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        using Gst.Structure data = RequireMessageData(msg);
        return ReadType(data, msg);
    }

    /// <summary>
    /// Reads the error and the details out of a
    /// <see cref="TranscoderMessage.Error"/> message.
    /// </summary>
    /// <param name="msg">The message, as it came off the API bus.</param>
    /// <param name="error">The error the transcoder reported.</param>
    /// <param name="details">
    /// The details of the error, which the caller has to dispose, or
    /// <see langword="null"/> when the message carries none. The four errors
    /// the transcoder raises itself — a clock loss it could not recover
    /// from, a state the pipeline refused to change to, no encoding profile and a
    /// pipeline that would not start — carry none. An error it forwards from
    /// the bus of its own pipeline always carries them.
    /// </param>
    /// <remarks>
    /// This is <c>gst_transcoder_message_parse_error</c>, written by hand
    /// because the imported one aborts the process on a message with no
    /// details; see the remarks on this class.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="msg"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="msg"/> is not a message of a transcoder API bus, is not
    /// a <see cref="TranscoderMessage.Error"/>, or carries no error.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="msg"/> was disposed.</exception>
    public static void ParseError(Gst.Message msg, out GException error, out Gst.Structure? details) =>
        ParseIssue(msg, TranscoderMessage.Error, "error", out error, out details);

    /// <summary>
    /// Reads the warning and the details out of a
    /// <see cref="TranscoderMessage.Warning"/> message.
    /// </summary>
    /// <param name="msg">The message, as it came off the API bus.</param>
    /// <param name="error">The warning the transcoder reported.</param>
    /// <param name="details">
    /// The details of the warning, which the caller has to dispose, or
    /// <see langword="null"/> when the message carries none.
    /// </param>
    /// <remarks>
    /// This is <c>gst_transcoder_message_parse_warning</c>, written by hand for
    /// the same reason as <see cref="ParseError"/>; see the remarks on this
    /// class.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="msg"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="msg"/> is not a message of a transcoder API bus, is not
    /// a <see cref="TranscoderMessage.Warning"/>, or carries no warning.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="msg"/> was disposed.</exception>
    public static void ParseWarning(Gst.Message msg, out GException error, out Gst.Structure? details) =>
        ParseIssue(msg, TranscoderMessage.Warning, "warning", out error, out details);

    /// <summary>
    /// The body of both parses: the error field is the only difference between
    /// them.
    /// </summary>
    /// <param name="msg">The message to read.</param>
    /// <param name="expected">The kind of message the caller asked for.</param>
    /// <param name="errorField">The name of the field that carries the error.</param>
    /// <param name="error">The error the message carries.</param>
    /// <param name="details">The details of the message, or <see langword="null"/>.</param>
    private static void ParseIssue(
        Gst.Message msg,
        TranscoderMessage expected,
        string errorField,
        out GException error,
        out Gst.Structure? details)
    {
        ArgumentNullException.ThrowIfNull(msg);

        using Gst.Structure data = RequireMessageData(msg);
        if (ReadType(data, msg) != expected)
        {
            throw new ArgumentException(
                $"The message is not a {expected} message of a transcoder API bus.",
                nameof(msg));
        }

        using Value issue = data.GetValue(errorField);
        if (issue.IsEmpty || !string.Equals(issue.Type.Name, "GError", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {expected} message of the transcoder API bus carries no '{errorField}' field.",
                nameof(msg));
        }

        // The value holds a copy of the GError of the message, and the
        // exception copies the three fields out of it eagerly, so what is
        // handed back shares nothing with either.
        error = GException.FromBorrowed(issue.GetBoxed())
            ?? throw new ArgumentException(
                $"The '{errorField}' field of the transcoder API bus message holds no error.",
                nameof(msg));

        // The details are optional on every path: none of the four errors the
        // transcoder raises itself attaches them.
        using Value detailsValue = data.GetValue(IssueDetailsField);
        details = detailsValue.IsEmpty ? null : detailsValue.GetBoxed<Gst.Structure>();
    }

    /// <summary>
    /// Answers the payload structure of a message of the API bus, and refuses
    /// every other message.
    /// </summary>
    /// <param name="msg">The message to read.</param>
    /// <returns>The payload, which the caller has to dispose.</returns>
    private static Gst.Structure RequireMessageData(Gst.Message msg)
    {
        // gst_message_get_structure hands out what the message owns; the
        // wrapper of a boxed type is a copy of it, which is what the reads
        // below are taken from. An EOS message and everything else that
        // carries no structure at all lands here as null - asking the C
        // function instead would answer the same but log a GLib critical on
        // the way.
        Gst.Structure? data = msg.GetStructure();
        if (data is null || !string.Equals(data.GetName(), MessageDataName, StringComparison.Ordinal))
        {
            data?.Dispose();
            throw new ArgumentException(
                "The message is not a message of a transcoder API bus. Only a message that "
                + "Transcoder.IsTranscoderMessage accepts can be parsed here.",
                nameof(msg));
        }

        return data;
    }

    /// <summary>
    /// Reads the kind field out of the payload of a message of the API bus.
    /// </summary>
    /// <param name="data">The payload of the message.</param>
    /// <param name="msg">The message it came from, for the exception.</param>
    /// <returns>The kind of message.</returns>
    private static TranscoderMessage ReadType(Gst.Structure data, Gst.Message msg)
    {
        using Value kind = data.GetValue(MessageTypeField);
        if (kind.IsEmpty || kind.Type.Fundamental.Value != GType.Enum.Value)
        {
            throw new ArgumentException(
                "The message carries the payload of a transcoder API bus but no message type.",
                nameof(msg));
        }

        return (TranscoderMessage)kind.GetEnum();
    }
}
