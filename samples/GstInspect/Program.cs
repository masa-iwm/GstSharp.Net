// A port of gst-inspect-1.0 onto the binding: the registry census the C tool
// prints without arguments, and the whole page of an element.
//
// Usage: GstInspect [OPTION...] [ELEMENT-NAME]
//
// The ground truth is gstreamer/tools/gst-inspect.c of 1.28, and the goal is a
// byte-for-byte match: the CI legs run the real gst-inspect-1.0 and this sample
// against the same library and fail on any difference, so every quirk of the C
// tool is reproduced rather than fixed. Three of them are called out where they
// are ported -- the Indexable element flag is labelled "REQUIRE_INDEXABLE", the
// separator of the property flags list is written unconditionally for the
// GStreamer half of the bits, and the doc-uri metadata key is uppercased before
// it is compared and therefore never matches.
//
// What it prints:
//
//   * The census: one line per feature of every plugin in the registry, plugins
//     sorted by name and the features of each plugin sorted by name, then the
//     "Total count: N plugins, M features" line.
//
//   * An element page: the factory details, the plugin details, the GObject
//     hierarchy, the implemented interfaces, the element flags, the pad
//     templates with their caps, the clocking interaction, the URI handling
//     capabilities and their protocols, the pads of a fresh element, every
//     property with its blurb, flags, range, default and enumeration or flags
//     table, the signals and the action signals with their C signatures, the
//     children of a bin, and the presets.
//
// What is not reachable, and what it costs:
//
//   * A pad template whose pads have a GType of their own gets a "Type:" line
//     and a "Pad Properties" section from the C tool. GST_PAD_TEMPLATE_GTYPE
//     reads a field of GstPadTemplate that has no accessor to bind, so neither
//     is printed.
//
//   * GParamSpecValueArray::element_spec has no binding, so a property holding
//     a GValueArray is printed as "Array of GValues" without the type of its
//     members. GstValueArray, which is what GStreamer elements actually use, is
//     printed in full.
//
//   * A field of a caps structure that is itself a caps or a structure is
//     printed as its serialization rather than recursed into:
//     gst_value_get_caps and gst_value_get_structure are not bound.
//
//   * A blacklisted plugin is counted by the C tool and left out of the
//     listing. GST_PLUGIN_FLAG_BLACKLISTED is a bit of the GstObject flags word
//     of a GstPlugin, which the binding reaches for a GstElement only, so this
//     counts it like any other plugin -- it has no features, so it adds nothing
//     to the listing and one to the plugin count.
//
// Per-operating-system behavior in the C source, and what became of it:
//
//   * The pager. redirect_stdout() forks "less" (or $PAGER) and re-points
//     stdout at it when the output is a terminal, and it is #ifdef'd to nothing
//     on Windows, where the C tool never pages. There is no pager here on any
//     system: a sample that is read by a CI leg and by a diff should not have
//     one, and the C tool's own Windows build does not.
//
//   * Colors. The C tool colors its output when stdout is a terminal, and on
//     Windows it first has to turn on virtual terminal processing. Nothing is
//     colored here, which is what the C tool itself produces when its output is
//     piped -- and piped is how both are compared.
//
//   * g_win32_get_command_line() plus g_option_context_parse_strv() under
//     G_OS_WIN32, for the same reason every GStreamer tool has it: a managed
//     string[] is already the Unicode command line.
//
//   * gst_macos_main() under __APPLE__ && TARGET_OS_MAC && !TARGET_OS_IPHONE --
//     the Cocoa run loop wrapper. gst-inspect has no main loop to put on it.
//
//   * setlocale (LC_ALL, "") and the translated strings behind _(). The C tool
//     prints "readable, writable" through gettext and a translated GStreamer
//     would print those words in another language; this prints the English
//     catalogue always, which is also what an untranslated installation does.
//     The CI diff runs both under LC_ALL=C for that reason.
return Inspection.Run(args);
