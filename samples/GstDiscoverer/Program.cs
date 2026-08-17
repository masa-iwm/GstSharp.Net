// A port of gst-discoverer-1.0 onto the binding: it asks GstDiscoverer what is
// inside every URI it is given and prints the report the C tool prints -- the
// result, the duration, whether the file is seekable and live, and the tree of
// streams with the details of each one.
//
// Usage: GstDiscoverer [OPTION...] <uris>
//
// The ground truth is gst-plugins-base/tools/gst-discoverer.c of 1.28. It is
// 778 lines and this port follows the synchronous half of them: the URI or file
// name on the command line, the directory recursion, the "Analyzing <uri>" line,
// the blocking gst_discoverer_discover_uri, the result switch, the properties
// block, the topology walk with its container recursion, the per-stream blocks
// for audio, video and subtitles, the tags under --verbose and the table of
// contents under --toc.
//
// Why the asynchronous half is not here:
//
//   -a runs the same discoveries from a GMainLoop and collects them through the
//   "discovered" signal, which hands the handler a GstDiscovererInfo and a
//   GError. A GError argument is the one signal shape the binding's signal
//   planner does not marshal yet, so GstPbutils.Discoverer::discovered is in
//   girs/skip-report.md under UnsupportedSignature. DiscoverUriAsync, Start,
//   Stop and the Finished event are all bound, and all useless without the
//   signal that carries the result -- so -a is absent rather than half done.
//   The same planner feature also unblocks GES.Project::error-loading-asset and
//   GES.DiscovererManager::discovered, which is why it is a feature and not a
//   patch to this sample.
//
// What is deliberately different, and why:
//
//   * A URI whose discovery posted an error message loses its report.
//     gst_discoverer_discover_uri fills its GError *and* returns an information
//     object whenever the run saw an error on the bus, whatever result it
//     settled on; the binding raises the GError before it wraps the return
//     value, so the object is out of reach. This prints the three lines the C
//     tool prints for GST_DISCOVERER_ERROR -- "Done discovering <uri>", "An
//     error was encountered while discovering the file", and the message.
//
//     Two consequences, and both were measured rather than reasoned about. A
//     failed discovery that still produced a topology has its properties block
//     printed by the C tool and not by this one. And an unplayable URI scheme,
//     which the C tool reports as "Missing plugins" followed by the installer
//     detail of the missing element, is reported here as the error message that
//     came with it -- so the branch that would print those details is not
//     reachable in practice, which is just as well:
//     gst_discoverer_info_get_missing_elements_installer_details returns a
//     gchar** and is not bound either. A result that arrives without an error
//     message on the bus is returned normally and is reported line for line.
//
//   * Caps printed without --verbose keep their buffers. The C caps_to_string
//     strips every field holding a GstBuffer first, through
//     gst_structure_filter_and_map_in_place_id_str, which is not bound. The
//     branch is only reachable for caps that are not fixed -- fixed caps are
//     printed as a codec description instead, and --verbose turns the stripping
//     off in the C tool too -- so nothing in a normal run differs.
//
//   * The speaker layout of an audio stream is computed rather than read.
//     gst_audio_channel_positions_from_mask is not bound and the GEnumClass
//     nickname table is not reachable, but neither is needed: the channel mask
//     is 1 << GstAudioChannelPosition bit by bit, and a GObject nickname is the
//     member name in lower case with dashes. Both halves are verified against
//     the real tool on a stereo and on a 5.1 file. See Discovery.Printing.cs.
//
//   * --print-cache-dir is absent. It prints g_get_user_cache_dir() joined with
//     "gstreamer-1.0/discoverer", and that first part is a GLib rule --
//     XDG_CACHE_HOME or ~/.cache on Unix, the local application data folder on
//     Windows -- that the binding does not expose and .NET has no equivalent
//     of. It says nothing about discovery. --use-cache is here: it is one
//     property on the discoverer.
//
//   * The usage line prints "GstDiscoverer" where the C tool prints argv[0],
//     which is the full path of the executable.
//
//   * One block is not stable in either tool, so do not expect a diff of two
//     runs to be empty: the merged tag list that --verbose prints under
//     Properties comes out of gst_discoverer_info_get_tags, whose order depends
//     on which streaming thread reported its tags first. The real tool
//     reorders that block between two runs of its own on the same file; the
//     per-stream tag blocks underneath are stable in both.
//
//   * --fail-on-error is not a gst-discoverer option. The C tool exits 0 for
//     every URI it could not discover, which leaves an automated run nothing to
//     gate on; this option turns those into exit code 1 so that the CI leg can
//     assert that the media was understood rather than only that nothing
//     crashed. Exit code -1 for a command line with no URI at all is the C
//     tool's own exit(-1).
//
// Per-operating-system behavior in the C source, and what became of it. Like
// gst-typefind.c and gst-device-monitor.c, this tool installs no signal
// handler, has no timer and has no console code, so the list is short:
//
//   * g_win32_get_command_line() plus g_option_context_parse_strv() and the
//     g_strv_length()/g_strfreev() pair around them under G_OS_WIN32 -- the C
//     tool re-reads the Unicode command line because the argv the CRT built is
//     in the local code page. A managed string[] is already the Unicode one.
//
//   * gst_macos_main() under __APPLE__ && TARGET_OS_MAC && !TARGET_OS_IPHONE --
//     the Cocoa run loop wrapper. It exists so that the -a path has a run loop
//     to sit on; the synchronous path this port covers has no main loop at all.
//
//   * The path handling is per-OS without being written that way.
//     g_path_is_absolute, g_get_current_dir and g_build_filename decide what an
//     absolute path is per platform, and gst_filename_to_uri -- which is bound
//     -- does all three steps itself, so the drive letters and backslashes of
//     Windows and the slashes of Unix are the library's business rather than
//     this sample's. A Windows path is never mistaken for a URI along the way,
//     because gst_uri_is_valid wants a protocol of at least two characters and
//     a drive letter is one. G_DIR_SEPARATOR_S in the directory recursion
//     becomes the file enumeration of .NET.
//
//   * setlocale (LC_ALL, "") is the one line that would change the output per
//     machine rather than per operating system, and it does not: every number
//     this tool prints is formatted with the invariant culture, which is what
//     the C tool's "%u" and GST_TIME_FORMAT produce in any locale.
//
//   * #ifndef GST_DISABLE_DEPRECATED around the "Additional info" block is a
//     build option of the library rather than an operating system. The builds
//     this binding loads have it enabled, so the block is ported.
return Discovery.Run(args);
