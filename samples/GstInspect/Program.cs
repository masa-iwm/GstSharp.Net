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
//     and a "Pad Properties" section from the C tool, printed by the same
//     function as the element properties with a class and no instance. Two
//     things are missing for it: GST_PAD_TEMPLATE_GTYPE, which reads a field
//     of GstPadTemplate, and g_object_class_list_properties on a class that
//     has no instance -- the binding lists the properties of an object. So
//     neither line is printed, and the page of an element whose pads are its
//     own class is short by that block: multiqueue, input-selector,
//     compositor, matroskamux, rtpbin and webrtcbin are the ones measured. The
//     four elements the CI diff covers have plain GstPads.
//
//   * A metadata value that is not ASCII comes out differently under Windows:
//     the C tool hands its page to a stdout that transliterates -- "Dröge"
//     becomes "Dr?ge" -- and this writes the UTF-8 the library gave it, which
//     is what the C tool itself writes on Linux and macOS. Measured on
//     audioresample, spectrum, compositor and decodebin; the four elements the
//     CI diff covers have ASCII metadata.
//
//   * A field of a caps structure that is itself a caps, a structure or a
//     GstUniqueList of either is printed as its serialization rather than
//     recursed into: gst_value_get_caps and gst_value_get_structure, which
//     every one of those branches ends in, are not bound. ssdtensordec, whose
//     "tensors" field is a structure of lists of caps, is the element that
//     shows it.
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
