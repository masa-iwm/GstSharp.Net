# Platform notes

Behaviour of the libraries this binding wraps that is a property of one
platform rather than of the binding. Nothing here is a defect of GstSharp.Net
and nothing here is worked around by it: the notes exist so that an application
knows what to expect before it designs around a feature that is not there.

## Windows device providers

A `Gst.DeviceMonitor` reports devices as they appear and disappear, but only
for the device providers that can watch for the change. A provider watches by
implementing the `start` and `stop` of `GstDeviceProviderClass`; one that
implements only `probe` answers `CanMonitor()` with `false`. Starting a monitor
still succeeds either way, and the difference is what happens afterwards: a
provider that cannot monitor is probed exactly once, when the monitor starts,
its devices arrive as `device-added` messages then, and nothing about it changes
for as long as the monitor runs.

Observed with GStreamer 1.28 on Windows, and matching what the sources of the
providers implement:

| Provider | `CanMonitor()` |
| --- | --- |
| `mfdeviceprovider` (Media Foundation cameras and microphones) | `true` |
| `d3d11screencapturedeviceprovider` | `false` |
| `d3d12screencapturedeviceprovider` | `true` |

So screen capture is the one to design around: an application that has to
notice a display being plugged in, unplugged or rearranged gets that from the
D3D12 provider and does not get it from the D3D11 one.

**Re-enumerating does not help while the monitor runs.**
`DeviceMonitor.GetDevices()` asks every provider it watches, and a provider that
has been started answers from the list it holds rather than probing again — for
a provider that cannot monitor, that list is the one snapshot taken when the
monitor started. Picking up a change from such a provider means stopping the
monitor and starting it again, or building a new one; a refresh when the user
opens a source picker is the usual place to do it.

Branch on the `CanMonitor()` answer rather than on the provider name. It is
what the provider itself says about the build that is installed, so a later
release that gains or loses the ability for one of these classes is picked up
without a change. The other Windows providers are a mix — `ksdeviceprovider`
and `wasapi2deviceprovider` answer `true`, as does `asiodeviceprovider` where
the plugin is shipped, which needs the ASIO SDK at build time and is therefore
missing from some binary distributions; `decklinkdeviceprovider` answers
`false` — and the same rule covers all of them.
