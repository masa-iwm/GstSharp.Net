namespace Gst.GObject;

/// <summary>
/// One member of an enumeration type, that is a <c>GEnumValue</c>.
/// </summary>
/// <param name="Value">The number the member stands for.</param>
/// <param name="Name">
/// The name of the member as C declares it, for example
/// <c>GST_STATE_PLAYING</c>.
/// </param>
/// <param name="Nick">
/// The short name the member is written as in a pipeline description, for
/// example <c>playing</c>, or <see langword="null"/> when the type registered
/// none. GLib does not promise one, and its own printers test for it.
/// </param>
/// <remarks>
/// The two strings are copies. GLib hands them out as pointers into the class
/// of the type, which the binding releases as soon as it has read the table, so
/// nothing here points at anything that could go away.
/// </remarks>
public readonly record struct EnumValue(int Value, string Name, string? Nick);

/// <summary>
/// One member of a set of flags, that is a <c>GFlagsValue</c>.
/// </summary>
/// <param name="Value">
/// The bit or the combination of bits the member stands for. A member of a
/// flags type is not always one bit: GStreamer declares combinations such as
/// <c>GST_SEEK_FLAG_ACCURATE | GST_SEEK_FLAG_FLUSH</c> as members of their own.
/// </param>
/// <param name="Name">
/// The name of the member as C declares it, for example
/// <c>GST_SEEK_FLAG_FLUSH</c>.
/// </param>
/// <param name="Nick">
/// The short name the member is written as in a pipeline description, for
/// example <c>flush</c>, or <see langword="null"/> when the type registered
/// none. GLib does not promise one, and its own printers test for it.
/// </param>
/// <remarks>
/// The two strings are copies. GLib hands them out as pointers into the class
/// of the type, which the binding releases as soon as it has read the table, so
/// nothing here points at anything that could go away.
/// </remarks>
public readonly record struct FlagsValue(uint Value, string Name, string? Nick);
