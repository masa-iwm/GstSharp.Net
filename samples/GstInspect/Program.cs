// A partial port of gst-inspect-1.0 onto the binding -- "inspect-lite": the
// registry census that the C tool prints without arguments, and the sections of
// an element page that the bound surface can reach.
//
// Usage: GstInspect [OPTION...] [ELEMENT-NAME]
//
// The ground truth is gstreamer/tools/gst-inspect.c of 1.28. It is 2542 lines
// and this is not all of them: it is the LAYOUT of the sections below, line for
// line, and an honest note at the end of every page naming the ones that are
// missing.
//
// What it prints:
//
//   * The census. One line per feature of every plugin in the registry, plugins
//     sorted by name and the features of each plugin sorted by name, then the
//     "Total count: N plugins, M features" line. This is print_element_list
//     with two of its three branches: an element factory prints its long name,
//     and anything else prints its GObject type name in parentheses. The third
//     branch, the file extensions of a typefind factory, needs
//     gst_type_find_factory_get_extensions -- a gchar** return that is not
//     bound -- so a typefind factory takes the "anything else" branch here.
//
//   * Factory Details. The rank with its name, and the metadata of the factory.
//     gst_element_factory_get_metadata_keys is another gchar** return, so the
//     keys are named rather than enumerated: long-name, klass, description,
//     author, doc-uri and icon-name, which is what gst_element_class_set_metadata
//     takes. The derived Documentation URL is the C tool's own rule, module list
//     and all.
//
//   * Plugin Details, complete: name, description, filename, version, license,
//     source module, the derived documentation URL, the reformatted release
//     date, binary package and origin.
//
//   * The type hierarchy, complete. It is a walk of GType.Parent and needs
//     nothing else.
//
//   * Pad Templates, with their availability and their caps, printed through the
//     C tool's print_caps and print_field. The caps of a template are read off
//     the element's class rather than off the factory's static templates, which
//     are a GList of structures the binding does not marshal; the two hold the
//     same names, directions, presences and caps.
//
//   * URI handling, as far as "Element can act as source." The protocol list
//     underneath needs gst_uri_handler_get_protocols, one more gchar** return.
//
//   * Element Properties: the name, the flags, the type and the value a freshly
//     built element has, each in its C column. That value is what the C tool
//     prints as the default for a readable property -- it reads the live object
//     too.
//
// What it does not print, and why. Every one of these is a missing piece of the
// binding rather than a decision, and the note at the end of each page says so
// to the person reading the output as well:
//
//   * Element Flags, Clocking Interaction. Both read GST_OBJECT_FLAG_IS_SET on
//     the element: LOCKED_STATE, SINK, SOURCE, PROVIDE_CLOCK, REQUIRE_CLOCK,
//     INDEXABLE. The flags word of GstObject is a structure field behind a
//     macro, so there is no call to bind.
//
//   * Implemented Interfaces. g_type_interfaces is used inside the runtime
//     (Core/GObject/SignalQuery.cs walks it to describe a type) but is not
//     public API of the binding.
//
//   * Pads. The pads of an instantiated element, through GstIterator; the
//     iterator is bound, the element's pad iterators are the part that is not.
//
//   * Element Signals and action signals. SignalQuery is bound and can describe
//     a signal by id, but g_signal_list_ids -- the call that hands out the ids
//     of a type -- is not public API, so there is nothing to enumerate from.
//     This is the largest single gap of the page and the one worth closing
//     first: the machinery behind it already exists.
//
//   * Children and Presets. GstChildProxy is not walked here, and
//     gst_preset_get_preset_names is a gchar** return.
//
//   * Inside the sections that are printed: the description of a property
//     (g_param_spec_get_blurb), the range of a numeric one (a field of the
//     GParamSpec subtype), the table of values under an enumeration or a flags
//     property (a walk of its GEnumClass or GFlagsClass), and the value of a
//     write-only property (g_param_value_set_default). Core/GObject/ParamSpec.cs
//     exposes Name, ValueType and Flags and nothing else; the rest of
//     GParamSpec introspection is one Core feature, and it is what would turn
//     this sample into a full gst-inspect.
//
//     Two smaller ones in the same section. The C tool prints the fields of a
//     property whose value is a GstStructure underneath it, which is reachable
//     here and simply not done: it is print_field again, at an indentation of
//     its own. And a numeric default is printed without the column padding the
//     C tool gives it -- that padding exists to line the default up behind the
//     range, and the range is not there.
//
// The property column layout is kept even where the column is empty: a property
// prints as "name" padded to twenty, a colon, and nothing, where the C tool
// prints the blurb after the colon. That is deliberate -- the missing column is
// visibly missing rather than filled with something invented.
//
// Two more differences worth naming:
//
//   * A blank line that belongs to a section this port does not print is not
//     printed either, so the vertical spacing of a page is the C tool's minus
//     those lines. The one exception is the blank line before the URI section,
//     which the clocking section owns in the C tool and which this prints
//     itself, because it separates two sections that are both here.
//
//   * A blacklisted plugin is counted by the C tool and left out of the
//     listing. GST_PLUGIN_FLAG_BLACKLISTED is a bit of the same unreachable
//     GstObject flags word, so this counts it like any other plugin -- it has
//     no features, so it adds nothing to the listing and one to the plugin
//     count.
//
// --no-coverage-note is not a gst-inspect option; it takes the closing note off
// so that a page can be diffed against the C tool's without a tail to strip.
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
return Inspection.Run(args);
