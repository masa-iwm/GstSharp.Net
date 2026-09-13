// A port of gst-stats-1.0 onto the binding: it reads a GStreamer debug log that
// carries GST_TRACER records and prints the report the C tool prints.
//
// Usage: GstStats [OPTION...] FILE
//
// The ground truth is gstreamer/tools/gst-stats.c of 1.28. It is 1349 lines and
// this port follows all of them: the three log line regular expressions and the
// format probe on the first line, the twelve tracer structures it understands,
// the aggregation into pad, element, bin, thread, latency and plugin statistics,
// and every printf of the report -- including the quirks, which are kept rather
// than fixed, because the gate this sample carries is a byte for byte diff
// against the C tool on the same log.
//
// Why the log is a file and not captured in this process:
//
//   Tracing is turned on by GST_TRACERS and written where GST_DEBUG_FILE says,
//   and both are read exactly once, GST_TRACERS in _priv_gst_tracing_init and
//   GST_DEBUG_FILE in _priv_gst_debug_init, before gst_init returns. There is no
//   API that loads a tracer or redirects the debug log afterwards, so a process
//   cannot decide at run time to start tracing itself.
//
//   The binding does have the other half -- Gst.Global.DebugAddLogFunction with
//   Gst.DebugMessage.Get() hands a managed callback the very text a log line
//   carries -- so an in-process variant is possible for a process that was
//   started with GST_TRACERS already set. It would not be this tool though: the
//   analyzer would be inside the traced process, so its own pads, buffers and
//   queries would land in the numbers it reports, and the report could only ever
//   cover the process itself and never a log somebody else recorded. The C tool
//   reads a file, and reading a file is also what makes the CI gate possible.
//
// What is deliberately different:
//
//   * A file that cannot be read is an error here: the message goes to standard
//     error and the exit code is 1. The C tool ignores a failed fopen, prints an
//     empty report and exits 0, which gives an automated run nothing to gate on.
//
//   * --version prints the two version lines and not the third one: the origin
//     of the package is a compile time constant of the C tool.
//
//   * --fail-if-empty is not a gst-stats option. It turns a log that yielded no
//     tracer record at all into exit code 1, so that the CI leg asserts that the
//     log was really parsed rather than only that nothing crashed.
//
//   * The C tool reports a line that matched no regular expression, a structure
//     it does not know and a payload that does not parse through GST_WARNING,
//     which is silent unless the reader turns GStreamer debugging on. This
//     counts them and prints one summary line on standard error, which keeps
//     standard output diffable and still says that something was dropped.
//
//   * Threads are printed in the order of their id here. The C tool walks a
//     GHashTable, whose order is unspecified, so a log with more than one thread
//     has no defined section order to reproduce -- which is why the CI diff
//     against gst-stats-1.0 compares sorted lines. The same holds for the two
//     latency tables, which are read out of a hash table before they are sorted
//     by their first timestamp: entries that share a timestamp are ordered by
//     their key here.
//
//   * printf's %p, which prints the thread id, is not one format: glibc prints
//     0x and no padding, and the C runtime of Windows prints sixteen padded
//     hexadecimal digits with no prefix -- in lower case when the tool was built
//     with MinGW and in upper case with the MSVC runtime (msvcrt and UCRT both).
//     The port prints the glibc form off Windows and the MinGW form on it,
//     because MinGW is the only Windows build that ships gst-stats-1.0 and so
//     the only one the diff on the Windows CI leg ever runs against.
//
//   * The record templates a tracer logs once, whose structure name ends in
//     .class, are recognised and skipped without being counted. Neither tool
//     reads them -- the C tool has a TODO about it -- but it tests the suffix
//     on the whole payload, which ends in a semicolon and therefore never
//     matches, so it counts every template as an unknown structure instead.
//     Only the counter on standard error differs; the report is the same.
//
// What is faithfully kept, because the report counts on it:
//
//   * The C tool reads a log line with fgets into 5000 bytes, so a longer line
//     reaches the parser as several: the first carries a structure that was cut
//     in the middle and does not parse, the rest match no parser at all, and the
//     record is therefore missing from the report. A caps query of a video
//     pipeline is regularly that long, so a port that reads whole lines counts
//     more queries than the C tool does. This reads the same 4999 bytes at a
//     time.
//
//   * The quirks of the aggregation: the running mean that rounds down once per
//     buffer, the else that leaves the maximum size of a pad that saw a single
//     buffer at zero, the nanosecond that is added to the receiving element to
//     break a tie, and the 64 bit differences the comparison functions truncate
//     to 32 bits before they order two records by their first activity.
//
// Nothing else in gst-stats.c is guarded by an operating system: it has no main
// loop, no signal handler and no console code, so the port is one behavior on
// every system.
return StatsRunner.Run(args);
