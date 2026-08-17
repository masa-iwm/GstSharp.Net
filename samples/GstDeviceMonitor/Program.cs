// A port of gst-device-monitor-1.0 onto the binding: it lists the capture and
// playback devices the installed device providers know about, and with
// --follow it keeps listening for devices that come and go.
//
// Usage: GstDeviceMonitor [OPTION...] [DEVICE_CLASSES[:FILTER_CAPS]] ...
//
// The ground truth is gst-plugins-base/tools/gst-device-monitor.c of 1.28.
// Every line this prints is the line the C tool prints: the "Device found:"
// block with its name, class, caps and properties, the gst-launch snippet
// underneath, and the two-line "Device removed:" report.
//
// The arguments are the C tool's device filters. "Video/Source" restricts the
// listing to a device class; "Video/Source:video/x-raw" adds a caps filter
// after the colon; the option may be repeated, and each one becomes one
// DeviceMonitor.AddFilter call.
//
// What is deliberately different:
//
//   * The bus is polled with a 100 ms timed pop instead of being watched from a
//     GMainLoop, and GstSharp.DrainPendingReleases() runs once per poll. This
//     changes nothing that is visible: the C tool's whole run is already a bus
//     conversation. gst_device_monitor_start() posts one DEVICE_ADDED message
//     per device that is already there and a DEVICE_MONITOR_STARTED message
//     behind them, and both tools print the first kind and stop on the second.
//     The initial listing is therefore not a gst_device_monitor_get_devices()
//     call in either tool -- that function is bound and deliberately unused
//     here, because using it would be a different program.
//
//   * THE LAUNCH LINE IS THE FACTORY NAME ALONE. get_launch_line() in the C
//     tool creates the element for the device, creates a second bare element
//     from the same factory, walks the properties of the class and appends
//     every one whose value differs from the bare element's. Two pieces of
//     that are missing from the binding:
//
//       - g_object_class_list_properties has no binding at all. Core's
//         ParamSpec exposes Name and ValueType and nothing enumerates the
//         parameter specifications of a class; g_object_class_find_property is
//         declared in Core/Interop/GObjectNative.cs and is internal, and it
//         answers by name, which is the question that cannot be asked here.
//       - gst_value_compare has no binding either, so "differs from the
//         default" could not be decided even with the list in hand.
//
//     So a device whose element needs "device=..." to be reachable prints a
//     snippet that would open the default device instead. This is stated here
//     rather than papered over, and closing it is a Core property-introspection
//     feature, not sample code.
//
//   * --follow-for is not a gst-device-monitor option. It bounds the follow
//     loop so that the hotplug path can be exercised, and gated, without a
//     console signal. --follow on its own waits forever, as the C tool does.
//
//   * --version prints the two version lines and not the third one: the origin
//     of the package is a compile time constant of the C tool.
//
// Per-operating-system behavior in the C source, and what became of it. Unlike
// gst-launch.c, this tool has no signal handling, no timer and no console code,
// so there is far less of it than one might expect:
//
//   * SHELL QUOTING (the interesting one, and it is not per-OS but per-SHELL,
//     decided at run time). value_to_string() shell-quotes any property value
//     that is not purely alphanumeric, and get_shell_type() picks the dialect
//     from the environment: PROMPT set means cmd.exe, PSModulePath set means
//     PowerShell, otherwise POSIX. The three quoters -- g_shell_quote,
//     cmd_quote and powershell_quote -- differ in real ways, down to
//     cmd_quote's caret-escaping of % and powershell_quote's doubling of the
//     typographic quotes. NONE OF IT IS PORTED, because all three exist only to
//     quote the property values of the launch line, and the paragraph above
//     explains why there are none. Porting the quoters would be porting dead
//     code; they come back with the property enumeration or not at all.
//
//   * g_win32_get_command_line() plus g_option_context_parse_strv() under
//     G_OS_WIN32 -- the C tool re-reads the Unicode command line because the
//     argv the CRT built is in the local code page. A managed string[] is
//     already the Unicode one, so this is nothing to port.
//
//   * gst_macos_main() under __APPLE__ && TARGET_OS_MAC && !TARGET_OS_IPHONE --
//     the Cocoa run loop wrapper the C main() wraps real_main() in. It exists
//     to give a GMainLoop something to sit on; a polled bus does not need it.
//
//   * No signal handling of any kind. gst-device-monitor.c installs no SIGINT,
//     SIGHUP or SIGSEGV handler, so Ctrl+C during --follow ends the process
//     where it stands and the monitor is never stopped. That is mirrored here
//     rather than improved on: --follow-for is the way to end a follow run
//     tidily.
return DeviceMonitoring.Run(args);
