# Security policy

## Supported versions

| Version | Supported |
| --- | --- |
| `1.28.x` | Yes |
| Earlier | No |

Fixes ship in a new patch release of the current series; there are no backports to
an older one.

## Scope

This policy covers **GstSharp.Net itself**, which is managed code only:

* the native library loader — how `NativeLoader` probes for and selects a GStreamer
  installation, and the flavor and directory it pins;
* P/Invoke marshalling in the hand-written runtime, including the GObject/GLib layer
  and the ownership and lifetime rules around it;
* the generated bindings and the generator that emits them;
* the NuGet packages this repository publishes.

## Out of scope: GStreamer itself

A vulnerability in the GStreamer libraries, in a plugin or in one of their
dependencies is not a vulnerability in this binding. The packages bundle no native
code: they load whatever installation is present on the machine. Report those to the
GStreamer project through its own process:
<https://gstreamer.freedesktop.org/security/>.

If you cannot tell which side a problem is on — a crash inside a native call, say —
report it here and say that you are unsure.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting: the **Security** tab of this repository,
then **Report a vulnerability**. The report stays private until a fix is released.
Please do not open a public issue for a suspected vulnerability.

Useful to include: the package versions, the operating system and architecture, the
GStreamer version and flavor, a reproduction, and what an attacker gains.

## What to expect

Reports are acknowledged and assessed on a best-effort basis, and a confirmed issue is
fixed in a patch release and called out in the release notes, crediting the reporter if
they want it. There is no bounty program.
