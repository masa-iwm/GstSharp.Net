// A port of gst-launch-1.0 onto the binding, faithful to what the real tool
// prints and to how it behaves, on the architecture this binding teaches.
//
// Usage: GstLaunch [OPTION...] PIPELINE-DESCRIPTION
//
// The ground truth is gstreamer/tools/gst-launch.c of 1.28. Every line this
// prints is the line the C tool prints, and the run state machine —
// target state, preroll, buffering, progress, live pipelines, EOS on shutdown —
// is ported statement by statement, because that machine is the part of the
// tool with behavior worth reproducing.
//
// It is a sample, not a product: no packaging and no translations. Where it
// cannot match the C tool, it says so here rather than differing quietly.
//
// One binary, per-operating-system behavior. The C tool compiles its signal
// handling and its Windows timer in and out with the preprocessor; this is a
// single cross-platform net10.0 executable that lights each one up where it
// belongs — Ctrl+C everywhere, SIGHUP and SIGQUIT on POSIX, the multimedia
// timer on Windows. Launcher.Platform.cs holds them and lists what the C
// source really has.
//
// What is deliberately different:
//
//   * The bus is polled with a 100 ms timed pop instead of being watched from
//     a GMainLoop, and GstSharp.DrainPendingReleases() runs once per poll. The
//     main loop is an implementation detail of the C tool; the polled loop is
//     what an application on this binding is meant to look like. One
//     consequence is visible: the C tool prints an error from its sync handler,
//     that is before the message reaches its bus watch, so with --messages the
//     error text there comes before the "Got message #N" line and here it comes
//     after. The same holds for the state-change dot dumps.
//
//     The position line is refreshed from that same poll rather than from a
//     100 ms timer of its own, so it ticks a little slower than the C tool's.
//
//   * The fault handler (-f/--no-fault) is half ported. The C tool catches
//     SIGSEGV and SIGQUIT and spins so that a debugger can attach; here only
//     SIGQUIT is caught, and it reports rather than spins. A SIGSEGV handler
//     running managed code is not memory safe and .NET owns that path already
//     (createdump), and a managed signal handler runs on a thread of its own,
//     so stopping it would not stop the pipeline. See InstallFaultHandler.
//
//   * The generic tag value of a GST_MESSAGE_TAG is printed with
//     gst_value_serialize where the C tool uses g_strdup_value_contents, which
//     is a GLib debug printer with no binding and a slightly different idea of
//     quoting. The tags of a TOC entry are not affected: the C tool reads those
//     with gst_tag_list_copy_value and so does this.
//
//   * --version prints the two version lines and not the third one: the origin
//     of the package is a compile time constant of the C tool.
//
//   * A second Ctrl+C is handled here and reaches the branch the C bus handler
//     has written down for it; the C tool takes its handler off after the
//     first, so there the second one ends the process. Only a third one does
//     that here.
//
//   * Not ported: --prog-name, because g_set_prgname also renames the process
//     in the messages GLib itself prints and nothing managed reaches that; the
//     GST_XINITTHREADS environment hints, because a managed
//     SetEnvironmentVariable does not reach the native getenv on every
//     platform; gst_macos_main, the Cocoa run loop wrapper the C main() uses on
//     macOS; and the index statistics, which are #if 0 in the C tool too.
//
//   * --interrupt-after is not a gst-launch option. It exists so that the
//     interrupt path can be exercised without a console signal, and it can be
//     given twice to reach the second interrupt.
return Launcher.Run(args);
