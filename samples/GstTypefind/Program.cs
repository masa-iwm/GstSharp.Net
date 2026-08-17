// A port of gst-typefind-1.0 onto the binding: it detects the media type of
// every file it is given and prints the one line the C tool prints.
//
// Usage: GstTypefind [OPTION...] FILES
//
// The ground truth is gstreamer/tools/gst-typefind.c of 1.28. It is 222 lines
// and this port follows all of them: a fresh filesrc ! typefind ! fakesink
// pipeline per file, the "have-type" signal connected on the typefind element,
// PAUSED and a blocking get_state, the "<file> - <caps>" line on success, the
// "<file> - FAILED: <message>" line on standard error when the state change
// fails, and the recursion into a directory that is named on the command line.
//
// Why this sample exists: the "have-type" signal.
//
//   typefind is a plugin element. No .gir file describes it, so no generated
//   event covers its signals, and the only way to reach one is the dynamic
//   machinery of the binding -- Object.ConnectSignal(name, handler), which
//   looks the signal up in the introspection GObject itself keeps and marshals
//   the emission through a single closure. That path is what this sample
//   exercises, and it is the first sample that does.
//
//   "have-type" is declared as (guint probability, GstCaps caps), and both
//   arguments arrive typed: the probability as a uint in args[0], the caps as a
//   Gst.Caps in args[1].
//
//   The caps are the interesting half. A GstCaps is a mini object, which
//   GObject sees as a boxed type, so it travels in the emission as a GValue
//   holding a boxed pointer -- and a mini object wrapper does not derive from
//   Gst.GObject.Boxed, so Value.GetBoxed<T>() cannot build one. The dynamic
//   layer therefore looks the GType of the value up in the type registry, which
//   holds the factory of every generated wrapper, and hands the handler the
//   wrapper rather than the raw handle. Value.GetMiniObject<T>() is the same
//   route for code that holds the value itself, for example a property or a
//   structure field.
//
//   The argument is lent to the handler, exactly as the const GstCaps * of the
//   C signature is: the wrapper is released when the handler returns. Keeping
//   the caps therefore means copying them, which is what the C handler does
//   with gst_caps_copy and what HaveTypeWatch does with Caps.Copy(). See
//   docs/ownership.md.
//
//   Caps.ToString() is gst_caps_to_string, so the printed line is character for
//   character the line of the C tool -- which the alternative route,
//   serializing the "caps" property with gst_value_serialize, would not be:
//   that one wraps and escapes the string. Reading the caps back from the src
//   pad of typefind with GetCurrentCaps() after PAUSED is a second route that
//   prints the same line; the emission is the one the C tool takes, and it is
//   the one that says whether a type was found at all.
//
// What is deliberately different:
//
//   * The error after a failed state change is read with PopFiltered, which
//     returns whatever the bus already holds, where the C tool calls
//     gst_bus_poll with a zero timeout. gst_bus_poll runs a GMainLoop of its
//     own; the polled bus is what an application on this binding is meant to
//     look like, and with a zero timeout the two see the same message.
//
//   * The C tool asserts that filesrc, typefind and fakesink exist and aborts
//     the process when one does not. This reports the missing elements on the
//     file's own FAILED line and carries on with the next file.
//
//   * --version prints the two version lines and not the third one: the origin
//     of the package is a compile time constant of the C tool.
//
//   * --fail-on-unknown is not a gst-typefind option. The C tool exits 0 for
//     every file it could not identify, which leaves an automated run nothing
//     to gate on; this option turns those into exit code 1 so that the CI leg
//     can assert the type was found rather than only that nothing crashed.
//
// Nothing in gst-typefind.c is guarded by an operating system: it installs no
// signal handler, has no timer and no console code. The two preprocessor
// guards it does carry are the ones every GStreamer tool carries -- the
// g_win32_get_command_line() argument vector, which a managed string[] already
// is, and gst_macos_main, the Cocoa run loop wrapper, which a tool with no
// main loop does not need. The port is therefore one behavior on every system.
return TypeFinder.Run(args);
