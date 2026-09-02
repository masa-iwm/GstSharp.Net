using System.Runtime.InteropServices;
using Gst.GLib;
using Gst.GObject;

namespace Gst.Play;

/// <content>
/// The four parses of the API bus the generator leaves out: the two that read a
/// <c>GError</c> through a pointer to a pointer, and the two whose C half reads
/// an uninitialised message kind when it is handed a message of another bus.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_play_message_parse_error</c> and
/// <c>gst_play_message_parse_warning</c> take a <c>GError **</c> beside a
/// <c>GstStructure **</c>, which is a shape the planner does not bind, so both
/// are hand written here in the shape the transcoder module uses for the same
/// pair: the error arrives as a <see cref="GException"/> value copied out of
/// the boxed <c>GError</c>, and the details as a structure of the caller's own
/// or as <see langword="null"/>. The details are absent on GStreamer 1.24 —
/// <c>on_error</c> copies whatever the element attached and attaches nothing
/// when there was nothing — and always present from 1.26 on, where they carry
/// the <c>uri</c> and, when it is known, the <c>stream-id</c>.
/// </para>
/// <para>
/// <c>gst_play_message_parse_error_missing_plugin</c> and its warning twin call
/// <c>gst_play_message_parse_type</c> first and compare its result against the
/// kind they expect. That parse answers a message which is not one of a play
/// API bus with <c>g_return_if_fail</c>, which leaves the local it was to fill
/// untouched, and the comparison then reads whatever was on the stack. The
/// members below make that check themselves, so a foreign message is an
/// <see cref="ArgumentException"/>.
/// </para>
/// </remarks>
public static unsafe partial class PlayMessageExtensions
{
    /// <summary>The name every message of the API bus carries.</summary>
    private const string MessageDataName = "gst-play-message-data";

    /// <summary>The field that says which kind of message it is.</summary>
    private const string MessageTypeField = "play-message-type";

    /// <summary>
    /// Reads the error and the details out of a
    /// <see cref="PlayMessage.Error"/> message.
    /// </summary>
    /// <param name="msg">The message, as it came off the API bus.</param>
    /// <param name="error">The error the play reported.</param>
    /// <param name="details">
    /// The details of the error, which the caller has to dispose, or
    /// <see langword="null"/> when the message carries none. From GStreamer
    /// 1.26 on the play always attaches them, with the <c>uri</c> the error
    /// refers to and, when it is known, the <c>stream-id</c>; on 1.24 an error
    /// whose element attached nothing carries none.
    /// </param>
    /// <remarks>
    /// This is <c>gst_play_message_parse_error</c>, written by hand because its
    /// two out parameters are pointers to pointers; see the remarks on this
    /// class.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="msg"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="msg"/> is not a message of a play API bus, is not a
    /// <see cref="PlayMessage.Error"/>, or carries no error.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="msg"/> was disposed.</exception>
    public static void ParseError(Gst.Message msg, out GException error, out Gst.Structure? details) =>
        ParseIssue(msg, PlayMessage.Error, "error", "error-details", out error, out details);

    /// <summary>
    /// Reads the warning and the details out of a
    /// <see cref="PlayMessage.Warning"/> message.
    /// </summary>
    /// <param name="msg">The message, as it came off the API bus.</param>
    /// <param name="error">The warning the play reported.</param>
    /// <param name="details">
    /// The details of the warning, which the caller has to dispose, or
    /// <see langword="null"/> when the message carries none. The version rule is
    /// the one of <see cref="ParseError"/>.
    /// </param>
    /// <remarks>
    /// This is <c>gst_play_message_parse_warning</c>, written by hand for the
    /// same reason as <see cref="ParseError"/>; see the remarks on this class.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="msg"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="msg"/> is not a message of a play API bus, is not a
    /// <see cref="PlayMessage.Warning"/>, or carries no warning.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="msg"/> was disposed.</exception>
    public static void ParseWarning(Gst.Message msg, out GException error, out Gst.Structure? details) =>
        ParseIssue(msg, PlayMessage.Warning, "warning", "warning-details", out error, out details);

    /// <summary>
    /// Reads the missing plugin descriptions out of a
    /// <see cref="PlayMessage.Error"/> message.
    /// </summary>
    /// <param name="msg">The message, as it came off the API bus.</param>
    /// <param name="descriptions">
    /// One human readable description per missing plugin, or
    /// <see langword="null"/> when the error is not a missing plugin one.
    /// </param>
    /// <param name="installerDetails">
    /// The installer detail string of each of them, in the same order and of
    /// the same length, as <c>Gst.Pbutils.Global.InstallPluginsSync</c> takes
    /// them, or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the message carried a missing plugin error.
    /// </returns>
    /// <remarks>
    /// This is <c>gst_play_message_parse_error_missing_plugin</c>, guarded:
    /// see the remarks on this class. It arrived in GStreamer 1.26 and throws
    /// <see cref="EntryPointNotFoundException"/> on an older installation, as
    /// every member above the floor of the binding does.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="msg"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="msg"/> is not a message of a play API bus or is not a
    /// <see cref="PlayMessage.Error"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="msg"/> was disposed.</exception>
    public static bool ParseErrorMissingPlugin(
        Gst.Message msg,
        out string[]? descriptions,
        out string[]? installerDetails)
    {
        RequireKind(msg, PlayMessage.Error);

        // Both slots are zero before the call: the C helper writes them on its
        // way in, but nothing else does, and a slot that stays untouched must
        // not be read as a pointer.
        nint descriptionsNative = default;
        nint installerDetailsNative = default;
        int nativeResult = GstPlayMessageParseErrorMissingPluginNative(
            msg.Handle,
            &descriptionsNative,
            &installerDetailsNative);
        GC.KeepAlive(msg);
        descriptions = Gst.Interop.GMarshal.StrvToArray(descriptionsNative, free: true);
        installerDetails = Gst.Interop.GMarshal.StrvToArray(installerDetailsNative, free: true);
        return nativeResult != 0;
    }

    /// <summary>
    /// Reads the missing plugin descriptions out of a
    /// <see cref="PlayMessage.Warning"/> message.
    /// </summary>
    /// <param name="msg">The message, as it came off the API bus.</param>
    /// <param name="descriptions">
    /// One human readable description per missing plugin, or
    /// <see langword="null"/> when the warning is not a missing plugin one.
    /// </param>
    /// <param name="installerDetails">
    /// The installer detail string of each of them, in the same order and of
    /// the same length, or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the message carried a missing plugin
    /// warning.
    /// </returns>
    /// <remarks>
    /// This is <c>gst_play_message_parse_warning_missing_plugin</c>, guarded
    /// like <see cref="ParseErrorMissingPlugin"/>, and it arrived in the same
    /// release.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="msg"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="msg"/> is not a message of a play API bus or is not a
    /// <see cref="PlayMessage.Warning"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="msg"/> was disposed.</exception>
    public static bool ParseWarningMissingPlugin(
        Gst.Message msg,
        out string[]? descriptions,
        out string[]? installerDetails)
    {
        RequireKind(msg, PlayMessage.Warning);

        nint descriptionsNative = default;
        nint installerDetailsNative = default;
        int nativeResult = GstPlayMessageParseWarningMissingPluginNative(
            msg.Handle,
            &descriptionsNative,
            &installerDetailsNative);
        GC.KeepAlive(msg);
        descriptions = Gst.Interop.GMarshal.StrvToArray(descriptionsNative, free: true);
        installerDetails = Gst.Interop.GMarshal.StrvToArray(installerDetailsNative, free: true);
        return nativeResult != 0;
    }

    /// <summary>
    /// The body of both issue parses: the two field names are the only
    /// difference between them.
    /// </summary>
    /// <param name="msg">The message to read.</param>
    /// <param name="expected">The kind of message the caller asked for.</param>
    /// <param name="errorField">The name of the field that carries the error.</param>
    /// <param name="detailsField">The name of the field that carries the details.</param>
    /// <param name="error">The error the message carries.</param>
    /// <param name="details">The details of the message, or <see langword="null"/>.</param>
    private static void ParseIssue(
        Gst.Message msg,
        PlayMessage expected,
        string errorField,
        string detailsField,
        out GException error,
        out Gst.Structure? details)
    {
        ArgumentNullException.ThrowIfNull(msg);

        using Gst.Structure data = RequireMessageData(msg);
        if (ReadType(data, msg) != expected)
        {
            throw new ArgumentException(
                $"The message is not a {expected} message of a play API bus.",
                nameof(msg));
        }

        using Value issue = data.GetValue(errorField);
        if (issue.IsEmpty || !string.Equals(issue.Type.Name, "GError", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {expected} message of the play API bus carries no '{errorField}' field.",
                nameof(msg));
        }

        // The value holds the GError of the message, and the exception copies
        // the three fields out of it eagerly, so what is handed back shares
        // nothing with the message.
        error = GException.FromBorrowed(issue.GetBoxed())
            ?? throw new ArgumentException(
                $"The '{errorField}' field of the play API bus message holds no error.",
                nameof(msg));

        // GStreamer 1.24 omits the field for an error or a warning that came
        // with no details of its own; 1.26 and later always attach one.
        using Value detailsValue = data.GetValue(detailsField);
        details = detailsValue.IsEmpty ? null : detailsValue.GetBoxed<Gst.Structure>();
    }

    /// <summary>
    /// Refuses every message that is not of the expected kind, so that the C
    /// call below is never handed one whose kind it would read uninitialised.
    /// </summary>
    /// <param name="msg">The message to check.</param>
    /// <param name="expected">The kind of message the caller asked for.</param>
    private static void RequireKind(Gst.Message msg, PlayMessage expected)
    {
        ArgumentNullException.ThrowIfNull(msg);

        using Gst.Structure data = RequireMessageData(msg);
        if (ReadType(data, msg) != expected)
        {
            throw new ArgumentException(
                $"The message is not a {expected} message of a play API bus.",
                nameof(msg));
        }
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
        // below are taken from. A message that carries no structure at all
        // lands here as null - asking gst_play_is_play_message instead would
        // answer the same but log a GLib critical on the way.
        Gst.Structure? data = msg.GetStructure();
        if (data is null || !string.Equals(data.GetName(), MessageDataName, StringComparison.Ordinal))
        {
            data?.Dispose();
            throw new ArgumentException(
                "The message is not a message of a play API bus. Only a message that "
                + "Play.IsPlayMessage accepts can be parsed here.",
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
    private static PlayMessage ReadType(Gst.Structure data, Gst.Message msg)
    {
        using Value kind = data.GetValue(MessageTypeField);
        if (kind.IsEmpty || kind.Type.Fundamental.Value != GType.Enum.Value)
        {
            throw new ArgumentException(
                "The message carries the payload of a play API bus but no message type.",
                nameof(msg));
        }

        return (PlayMessage)kind.GetEnum();
    }

    /// <summary>The <c>gst_play_message_parse_error_missing_plugin</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_message_parse_error_missing_plugin")]
    private static partial int GstPlayMessageParseErrorMissingPluginNative(
        nint msg,
        nint* descriptions,
        nint* installerDetails);

    /// <summary>The <c>gst_play_message_parse_warning_missing_plugin</c> entry point.</summary>
    [LibraryImport("GstPlay", EntryPoint = "gst_play_message_parse_warning_missing_plugin")]
    private static partial int GstPlayMessageParseWarningMissingPluginNative(
        nint msg,
        nint* descriptions,
        nint* installerDetails);
}
