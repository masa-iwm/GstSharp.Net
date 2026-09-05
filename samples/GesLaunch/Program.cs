// A port of ges-launch-1.0 onto the binding: it builds a timeline from a
// command line description or loads one from a project file, and then plays it
// back or renders it.
//
// Usage: GesLaunch [OPTION...] [+clip <uri> [<property>=<value>...] ...]
//
// The ground truth is gst-editing-services/tools/ges-launcher.c, utils.c and
// ges-launcher-kb.c of 1.28. The timeline grammar itself is not tool code: the
// description tokens are escaped here and prefixed with "ges:", and everything
// after that prefix is parsed by the library's own GESCommandLineFormatter.
// The keywords it accepts - +clip, +test-clip, +effect, +title, +track,
// +keyframes and set-<property> - and their arguments are documented in the
// ges-launch-1.0 manual page; there is no +transition, because the formatter
// turns auto-transition on for the timeline it builds.
//
// Examples:
//
//   GesLaunch +test-clip snow d=1 +test-clip smpte d=1
//   GesLaunch --save-only project.xges +test-clip snow d=1
//   GesLaunch -l project.xges --videosink fakesink --audiosink fakesink
//   GesLaunch -o file:///tmp/out.ogg +test-clip snow d=1
//
// How the load is driven, which is the part of this sample worth reading:
//
//   The "loaded" signal of a GESProject is never emitted inside
//   Asset.Extract<Timeline>(). The library defers it through an idle source on
//   the main context that was thread-default - here, the process-wide default
//   one - when Extract was called, so nothing happens until somebody iterates
//   that context. This sample therefore runs a load-wait loop over
//   MainContext.Default.Iteration(mayBlock: false) on the very thread that
//   called Extract, and it never pushes a context of its own: the editing
//   services run a main loop over the *default* context themselves while they
//   discover a "+clip file://..." asset, and a pushed context would send that
//   discovery to a context nobody iterates.
//
//   For a "ges:" description the clips are already in the timeline when Extract
//   returns and one iteration emits "loaded". For a project file they are not:
//   every asset is requested asynchronously and the clips are created inside
//   the iteration, on the pumping thread. And "loaded" alone is not success - a
//   project with a broken asset emits "error-loading" and then "loaded" anyway
//   - so the run fails on "error-loading"/"error-loading-asset" exactly like
//   the C tool's seenerrors flag does.
//
//   Playback needs none of this. Once the timeline is loaded the bus is polled
//   with a timed pop, the way every other sample in this repository does it.
//
// Exit codes: 0 on end of stream, on --list-transitions and on --save-only,
// 1 on any error or on a command line that cannot be read, 2 when the run did
// not finish within --timeout.
//
// What is deliberately different from the C tool:
//
//   * --timeout <seconds> is not a ges-launch option. It bounds the load and
//     the playback so that an unattended run cannot hang, the way every other
//     sample in this repository is bounded. The default is 30 seconds and
//     --timeout 0 turns the bound off.
//
//   * The C tool runs inside a GApplication and a GMainLoop and watches the
//     bus from it. Here the bus is polled and the main context is iterated
//     only while the project is loading, which is what an application on this
//     binding is meant to look like.
//
//   * -s/--save saves once, after the project is loaded, through
//     ges_project_save. The C tool saves twice - once from
//     ges_timeline_save_to_uri before playback, in case GES crashes during it,
//     and once from its "loaded" handler - and the second save is the one this
//     keeps.
//
//   * The keyboard is on only when the run is interactive: --no-interactive
//     turns it off, and so does a redirected stdin, because Console.KeyAvailable
//     throws when stdin is a pipe. The C tool asks its terminal instead. Like
//     the C tool it is also off while rendering.
//
//   * -m/--mute is what the C tool does at ges-launcher.c:1263-1268 - a
//     fakeaudiosink and a fakevideosink as the preview sinks, with no fallback
//     to a plain fakesink. Where the C hands a NULL element to GES when a
//     factory is missing, this reports the missing factory and stops.
//
//   * -e/--encoding-profile is what the C tool does, not what its own help text
//     says. There is no preset file lookup anywhere in ges-launcher.c: the name
//     selects one of the encoding profiles the loaded project already carries
//     (ges-launcher.c:611-620), and it has no effect when --format is given or
//     when the project carries no profile of its own.
//
//   * The encoding details block is one line per fact rather than the recursive
//     dump of describe_encoding_profile, which is a printer of the C tool with
//     no counterpart here.
//
//   * --list-transitions prints the nicks of GESVideoStandardTransitionType,
//     read out of the type system the way the C tool's print_enum does. The
//     type is registered on demand, so a transition clip is created first when
//     the name is not known yet.
//
// What is not ported:
//
//   * --profile-from and --container-profile. The first rebuilds the track
//     topology out of the discoverer information of a named clip and the second
//     re-parents the profile tree (ges-launcher.c:773-811, 660-710); both are
//     fully bound and both are a feature of their own rather than of this port.
//
//   * --embed-nesteds, which pulls nested timelines into the saved project.
//
//   * --set-scenario, --set-test-file, --enable-validate and
//     --inspect-action-type: they are GstValidate, which has no module in this
//     binding and is compiled out of the C tool as well unless it was built
//     with it.
//
//   * The per-keyword --help synopsis. It comes from
//     ges_command_line_formatter_get_help, which the generator cannot bind
//     (girs/skip-report.md:75); --help prints the static synopsis below.
//
//   * The GST_DEBUG dot file dumps of the bus handler, and the x264enc tuning
//     of the "deep-element-added" handler that --smart-rendering installs.
//
// Everything runs on this thread. The editing services assert the thread a
// timeline and its tracks were created on, so a Task.Run around any of this
// would abort the process rather than fail.
return Launcher.Run(args);
