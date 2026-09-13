# GstDeviceMonitor

A port of `gst-device-monitor-1.0`: a `DeviceMonitor` with one `AddFilter` per
`DEVICE_CLASSES[:FILTER_CAPS]` argument, the device listing with its caps,
properties and launch line, and `--follow` for the hotplug report — all of it
read off the monitor's bus. `Program.cs` documents what the port keeps and what
it deliberately leaves behind.

```sh
dotnet run --project samples/GstDeviceMonitor
dotnet run --project samples/GstDeviceMonitor -- Video/Source
dotnet run --project samples/GstDeviceMonitor -- --follow --follow-for 5 Video/Source
```

## Headless runs and the pulse provider

Some providers wait for a daemon in `start` without a deadline: the PulseAudio
provider of gst-plugins-good sits there forever where no sound server runs, and
`gst-device-monitor-1.0` hangs there just the same, being the same monitor and
the same providers. A class filter is the way out, and it works because of *when*
it applies - `gst_device_monitor_add_filter` matches the requested classes
against each factory's `klass` segment by segment and instantiates only the
factories that match, so a provider that does not match is never constructed, let
alone started. `Video/Source` selects the video4linux2 provider
(`Source/Sink/Video`) and excludes pulse, alsa and oss (all `Sink/Source/Audio`),
which is the command the Linux CI leg runs:

```sh
dotnet run --project samples/GstDeviceMonitor -- Video/Source
```

Zero devices is a successful outcome there; a filter that matches nothing at all
is not, and exits `-1`. An `Audio/...` filter brings pulse back. One version
note: `DEVICE_MONITOR_STARTED`, the message a run without `--follow` normally
stops on, only exists from 1.28 - before it the monitor starts synchronously and
the listing is already on the bus when `Start` returns, so such a run ends on the
first poll that finds the bus empty instead.
