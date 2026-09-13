# GstStats

A port of `gst-stats-1.0`: it reads a GStreamer debug log that carries
`GST_TRACER` records and prints the report the C tool prints — the overall
counts, a section per thread with the statistics of the pads that thread drove,
the element and bin sections, the latency tables and the plugins and factories
that were used. `Program.cs` documents what the port keeps, and what it
deliberately does differently.

```sh
dotnet run --project samples/GstStats -- trace.log
dotnet run --project samples/GstStats -- --fail-if-empty trace.log
dotnet run --project samples/GstStats -- --tracer-regexp '^.*TRACE.*: (.*)$' trace.log
```

## Making a log

The report is read out of a log that another process wrote, and that process has
to be started with tracing already on:

```sh
GST_TRACERS="stats;rusage;latency(flags=pipeline+element+reported);factories" \
GST_DEBUG=GST_TRACER:7 \
GST_DEBUG_FILE=trace.log \
  gst-launch-1.0 -q videotestsrc num-buffers=50 ! videoconvert ! fakesink

dotnet run --project samples/GstStats -- trace.log
```

All three variables are read exactly once, while `gst_init` runs — `GST_TRACERS`
in `_priv_gst_tracing_init`, `GST_DEBUG_FILE` and `GST_DEBUG` in
`_priv_gst_debug_init` — so setting them after a GStreamer call has been made
does nothing, and there is no API that loads a tracer or redirects the log
later. That is also why this tool reads a file instead of tracing itself.

Two more consequences worth knowing:

* `GST_DEBUG_FILE` **truncates** the file it opens, `g_fopen(name, "w")` on POSIX
  and `CREATE_ALWAYS` on Windows. A run of this sample that still has the
  variable set would therefore erase the log it was about to read, so unset
  `GST_TRACERS` and `GST_DEBUG_FILE` before reading a log back.
* The `latency` tracer traces the pipeline alone by default. The `flags`
  parameter above is what makes it log `element-latency` and
  `element-reported-latency` as well, and therefore what fills the *Element
  Latency Statistics* and *Element Reported Latency* sections of the report.

The `--tracer-regexp` option replaces the expression that reads a log line; its
group 1 has to capture the text of the structure. It is .NET regular expression
syntax here, where the C tool takes a Perl one through GRegex.

`--fail-if-empty` is not an option of `gst-stats-1.0`. The C tool exits 0 for a
log it found nothing in — an empty file, a log written without `GST_TRACERS`, or
one whose lines no parser matched — which leaves an automated run nothing to
gate on. With the option, a run in which no structure reached one of the twelve
handlers exits 1 instead, and that is what the CI legs assert: that the log the
step before produced really was a tracer log and really was read.

## Windows: no CPU load

`gstrusage.c`, the tracer that logs `thread-rusage` and `proc-rusage`, needs the
`getrusage` of POSIX and is not built into the MinGW distribution of GStreamer:
`gst-inspect-1.0 --plugin coretracers` lists `dots`, `factories`, `latency`,
`leaks`, `log` and `stats` there and no `rusage`, and naming it in `GST_TRACERS`
only produces a warning. A log made on Windows therefore carries no resource
usage record, and the report has no `Avg CPU load:` line, neither in the overall
section nor per thread. Everything else is the same on all three platforms.

## Ordering

The C tool prints its thread sections, and reads its two latency tables, in
`GHashTable` order, which is unspecified: a log with more than one thread has no
defined section order to reproduce. This sample orders threads by their id and
latency rows that share a first timestamp by their key. That is why the CI leg
that diffs the two reports against each other compares sorted lines.

The thread id itself is printed with `%p` by the C tool, and that format is not
the same everywhere — the C library of Windows prints sixteen padded hexadecimal
digits, glibc prints `0x` and no padding — so the port prints what the platform
it runs on prints.
