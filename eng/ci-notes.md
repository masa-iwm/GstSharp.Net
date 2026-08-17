# CI notes

Why the workflows in `.github/workflows/` look the way they do, what is pinned,
what a runner does that a development machine does not, and how to run each
gate by hand.

## The gates

`.github/workflows/ci.yml` runs on every push to `main`, on every pull request
against `main`, on demand, and through `workflow_call` from the release
workflow. Jobs are split by what they need from the machine:

| Job | Runner | Needs GStreamer | What only this job covers |
| --- | --- | --- | --- |
| `verify` | `ubuntu-latest` | no | generator drift (the whole generated tree plus `girs/skip-report.md`), warning-free build, generator/analyzer tests, and the proof that `GstSharp.Core.Tests` needs no installation |
| `linux` | `ubuntu-24.04` | apt | the Linux SONAME path of `NativeLoader` |
| `macos` | `macos-latest` | Homebrew | the macOS dylib path and the Homebrew directory of the planner |
| `windows-mingw` | `windows-latest` | MSYS2 | the MinGW file names and the MSYS2 / search-path branch of `NativeInstallPlanner` |
| `windows-msvc-aot` | `windows-latest` | official installer | the MSVC file names, the environment-variable branch of the planner, and both NativeAOT gates |

Every job has `timeout-minutes`, the workflow cancels superseded runs of the
same ref, and each job uploads its `.trx` files (or the AOT publish logs) as an
artifact when it fails.

## GStreamer per operating system

### Windows, MSVC flavor — the official installer

Since 1.28 the official Windows packages are **Inno Setup executables, not
MSIs**, so there is no `msiexec` and no `ADDLOCAL=ALL`:

```
https://gstreamer.freedesktop.org/data/pkg/windows/1.28.6/msvc/gstreamer-1.0-msvc-x86_64-1.28.6.exe
```

`eng/install-gstreamer-windows.ps1` downloads it (into a directory that
`actions/cache` keeps between runs), verifies the published
`.sha256sum` next to it, and starts it with `/VERYSILENT /SUPPRESSMSGBOXES
/NORESTART`.

Two details are load-bearing:

* An Inno Setup installer is a GUI application, so it is started through
  `Start-Process -Wait`. Without that the step would return before the
  installation had begun and the build would race it.
* Whatever environment variables the installer sets are **invisible to the
  job**: the runner process was started before the installation. The script
  therefore probes the known roots (`%LOCALAPPDATA%\Programs\gstreamer\1.0\…`,
  `C:\gstreamer\1.0\…`, `%ProgramFiles%\gstreamer\1.0\…`) for the anchor library
  and writes `GSTREAMER_1_0_ROOT_MSVC_X86_64` into `GITHUB_ENV` plus the `bin`
  directory into `GITHUB_PATH`. That variable is the second candidate
  `NativeInstallPlanner.EnumerateWindows` looks at, so the rest of the job
  resolves deterministically.

No setup type or component list is passed. The Inno equivalent of
`ADDLOCAL=ALL` is `/TYPE=` plus `/COMPONENTS=`, whose names for this installer
are not documented anywhere the author could check, and guessing a name that
does not exist silently changes what gets installed. The default selection
contains core and base, which is everything the suites use (`fakesrc`,
`fakesink`, `identity`, `capsfilter`, `videotestsrc`, `videoconvert`, `volume`,
`appsrc`, `appsink`), and a missing plugin fails the job loudly on the
AppSinkSpans gate.

### Windows, MinGW flavor — MSYS2

`msys2/setup-msys2@v2` with `mingw-w64-x86_64-gstreamer`,
`-gst-plugins-base`, `-gst-plugins-good`, `-gst-plugins-bad-libs` and
`-gst-editing-services`. It was chosen over the official
MinGW installer because the two Windows jobs then cover different code:

* the MSVC job covers the environment-variable branch and the `gstreamer-1.0-0.dll`
  naming;
* the MSYS2 job covers the `lib`-prefixed naming and the MSYS2 branch of the
  planner, which is how the maintainer's own machine finds GStreamer.

The installation is announced **only through `PATH`**
(`<msys2-location>\mingw64\bin`, from the action's `msys2-location` output,
available since v2.24.1). Setting `MSYSTEM_PREFIX` or
`GSTREAMER_1_0_ROOT_MINGW_X86_64` would work as well, and would defeat the
purpose: the point of the job is that the search-path probe finds an MSYS2 root
on its own. `setup-msys2` installs into the runner temp directory
(`D:\a\_temp\msys64`), not into `C:\msys64`; the planner does not care, because
it recognizes the `msys64` segment inside any `PATH` entry and then probes
`ucrt64\bin`, `mingw64\bin` and `clang64\bin` below it. The MSYS2 that the
runner image already carries cannot win that race: a candidate only counts when
the directory holds `libgstreamer-1.0-0.dll` (`NativeInstallPlanner.HasGStreamer`),
and the preinstalled copy has no GStreamer packages.

### Linux — apt

Runtime packages only:

```
libgstreamer1.0-0 libgstreamer-plugins-base1.0-0 libgstreamer-plugins-bad1.0-0
libges-1.0-0
gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-tools
```

The `-dev` packages are deliberately not installed. The binding loads versioned
SONAMEs (`libgstreamer-1.0.so.0`, `libgstapp-1.0.so.0`, see `NativeNames`),
which the runtime packages provide; `-dev` would only add headers and the
unversioned `.so` symlinks, which nothing here uses.

The last two are libraries rather than plugin sets. Every binding assembly
registers its types from a module initialiser, and `TypeRegistry.Freeze`
resolves them all, so a library a module names has to be there even when no
test builds an element out of it: `libgstwebrtc-1.0.so.0` comes from the bad
plugins package and `libges-1.0.so.0` from `libges-1.0-0`. The latter earns its
place twice — it ships the `nle` and `ges` plugins beside the library, and
`ges_init` fails outright when the non linear engine is not in the registry
("The `nle` plugin is missing", which is the library's own wording).

The runner is pinned to `ubuntu-24.04` rather than `ubuntu-latest`. Its
GStreamer is 1.24, which is the floor `AbiProbeTests.NativeVersionIsSupported`
asserts and the oldest release the struct layouts are validated against. If the
`ubuntu-latest` label moves to a newer image, this coverage would disappear
without anything turning red.

That floor is also why the ported tutorials run **here** and only here — this
leg has the richest plugin set and no GUI — and why `BasicTutorial08` wires its
appsrc and appsink with the `need-data` / `new-sample` **signals** rather than
with `SetSimpleCallbacks`: the callbacks stand for
`gst_app_src_set_simple_callbacks`, which arrived in 1.28, and calling it on
1.24 throws `EntryPointNotFoundException`. Two of the elements the C tutorials
use are not on this leg either. `wavescope` is in `gstreamer1.0-plugins-bad`,
of which only the *library* package is installed, so `BasicTutorial08` looks the
factory up and leaves its visualization branch out when it is not there; the
same applies to `basic-tutorial-7` whenever it is ported.

### macOS — Homebrew

`brew install gstreamer`. The formula bundles core, base, good, bad, ugly and
libav, and installs into `/opt/homebrew/lib` on the arm64 runners, which is one
of the directories `NativeInstallPlanner.EnumerateUnixDirectories` tries after
the plain SONAME.

## Which suite runs where

* `GstSharp.Generator.Tests` and `GstSharp.Analyzers.Tests` — `verify` only.
  They are pure and there is nothing to gain from repeating them per platform.
* `GstSharp.Core.Tests` — on `verify` **and** on every native job. It is native
  free by construction (the loader rules run against `FakePlatformProbe`; the
  marshalling tests stop at the argument checks, before `GLibNative.Malloc0`;
  the signal tests only compare struct layouts), and running it on a machine
  that has no GStreamer is what keeps that true.
* `GstSharp.IntegrationTests` — every native job. `GstFixture` calls
  `GstSharp.Initialize()` with default options and throws when nothing is
  found, so a broken installation is a red suite rather than a silent skip.
  There is no way to point the fixture at a directory, which is why every job
  makes the installation discoverable through the documented probes instead of
  through a test-only switch.

Each suite is its own step rather than one solution-wide `dotnet test`. A
solution-level run is what a development machine does; here the per-project
steps keep a failure attributable in the job log, and they let a native job skip
a suite that does not belong on it.

## The NativeAOT gates

`eng/aot-gate.ps1` publishes, asserts a clean publish, and runs the result:

```
./eng/aot-gate.ps1 -Project samples/AotSmoke -Rid win-x64
./eng/aot-gate.ps1 -Project samples/AppSinkSpans -Rid win-x64 \
    -Property InvariantGlobalization=true -RunArguments '--mode','pull'
```

* `-p:PublishAot=true -p:TrimMode=full` is the shape a consuming application
  ships with, and the one the binding is expected to survive.
* `-p:TrimmerSingleWarn=false` expands the per-assembly summary, so a warning
  names the member it came from.
* The log is scanned for `warning IL####`. `TreatWarningsAsErrors` from
  `Directory.Build.props` should already fail the publish before the scan sees
  anything, which is why the scan exists: it does not depend on a property that
  a future change could relax.
* `InvariantGlobalization=true` is passed **on the command line**, not added to
  `samples/AppSinkSpans/AppSinkSpans.csproj`. The sample formats everything
  with `CultureInfo.InvariantCulture`, so it loses nothing, and the sample
  sources are out of scope for the CI change.
* The second gate is the interesting one: `Global.ParseLaunch` plus
  `GetByName(...) as AppSink` under full trimming. A missing GType
  registration shows up as a null cast at run time, never as a build warning
  (see `docs/ownership.md`), so the assertion is the exit code of the
  published executable.

## Release

`.github/workflows/release.yml` triggers on tags matching `v*`, calls `ci.yml`
through `workflow_call` so that a tag runs the same matrix as a branch, and
then packs and pushes.

* The version comes from the tag and only from the tag:
  `v1.28.0-preview.1` -> `-p:Version=1.28.0-preview.1`. A tag that is not a
  version fails the job before anything is built.
* `dotnet pack GstSharp.Net.slnx` packs every packable project, so the workflow
  does not have to be edited when the package set changes. Samples and test
  projects set `IsPackable=false`.
* **`RepositoryUrl` is required.** GitHub Packages refuses a NuGet package it
  cannot link to a repository, with a 403 that reads like an authentication
  failure. The workflow passes `-p:RepositoryUrl=https://github.com/<owner>/<repo>`
  and `-p:RepositoryType=git` so that the push works no matter what the package
  metadata in the project files ends up being.
* `--no-symbols`: GitHub Packages has no symbol server. `dotnet nuget push`
  uploads a `.snupkg` next to each `.nupkg` unless told not to, and that upload
  fails. Any symbol package produced is uploaded as a build artifact instead.
  Embedding symbols (`DebugType=embedded`) would make the question go away and
  belongs to the packaging change, not here.
* `secrets.GITHUB_TOKEN` with `permissions: packages: write` is enough to push
  to `https://nuget.pkg.github.com/masa-iwm/index.json`. Consumers still need a
  token with `read:packages`, which is a property of the feed and not something
  CI can remove.

## Runner caveats

* **vswhere**: `vswhere.exe` is already on `PATH` on the GitHub-hosted Windows
  runners, so the workflows do nothing about it. On a **development machine**
  the AOT publish can fail with `MSB3073` when the shell does not have
  `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on `PATH`;
  prepend it in that shell only.
* **MSYS2 location**: `setup-msys2` installs into the runner temp directory.
  Anything that hard-codes `C:\msys64` will not find it. Use the action's
  `msys2-location` output.
* **Inno privileges**: if a future runner image refuses the machine-wide
  installation, add `/CURRENTUSER` to `-InstallerArguments`; the script already
  probes the per-user root.
* **`-warnaserror`**: the `verify` job promotes MSBuild and NuGet warnings to
  errors. A newly published advisory for a test-only package can therefore turn
  `main` red without a code change (NU1901-NU1904). The fix is to update the
  package; suppressing the audit would hide it everywhere.
* **`macos-latest` is arm64.** Nothing in the binding is x64-specific, but the
  ABI probes run against an arm64 GStreamer there, which is the point.
* **Windows AOT needs the MSVC toolchain.** The `windows-latest` image ships
  it; a self-hosted runner would need the C++ workload.

## Running the gates locally

From the repository root. On Windows use `pwsh` (or Windows PowerShell; the
scripts avoid PowerShell 7-only syntax).

```sh
# verify
dotnet restore GstSharp.Net.slnx
dotnet run --project generator/GstSharp.Generator --no-restore -- verify --gir-dir girs --out-dir src
dotnet build GstSharp.Net.slnx --no-restore -warnaserror
dotnet test tests/GstSharp.Generator.Tests --no-restore
dotnet test tests/GstSharp.Analyzers.Tests --no-restore
dotnet test tests/GstSharp.Core.Tests --no-restore

# the native jobs (needs a GStreamer installation the loader can find)
dotnet test tests/GstSharp.IntegrationTests --no-restore
dotnet run --project samples/AppSinkSpans --no-restore -- --mode pull

# the ported tutorials the Linux job runs, and the media they run against
dotnet run --project samples/GstLaunch --no-restore -- -q videotestsrc num-buffers=300 ! video/x-raw,width=320,height=240,framerate=30/1 ! videoconvert ! theoraenc ! oggmux name=mux ! filesink location=tutorial-media.ogg audiotestsrc num-buffers=430 ! audioconvert ! vorbisenc ! mux.
dotnet run --project samples/tutorials/BasicTutorial02 --no-restore -- --headless
dotnet run --project samples/tutorials/BasicTutorial03 --no-restore -- --headless tutorial-media.ogg
dotnet run --project samples/tutorials/BasicTutorial04 --no-restore -- --headless --seek-at 1 --seek-to 7 tutorial-media.ogg
dotnet run --project samples/tutorials/BasicTutorial08 --no-restore -- --headless --chunks 200
dotnet run --project samples/tutorials/BasicTutorial13 --no-restore -- --headless --keys SsPNNPDq tutorial-media.ogg

# the AOT gates (Windows)
./eng/aot-gate.ps1 -Project samples/AotSmoke -Rid win-x64
./eng/aot-gate.ps1 -Project samples/AppSinkSpans -Rid win-x64 -Property InvariantGlobalization=true -RunArguments '--mode','pull'

# what the release job packs (no push)
dotnet pack GstSharp.Net.slnx --configuration Release --output artifacts/dist -p:Version=1.28.0-preview.1
```

The installer script can also be used to reproduce the MSVC job's environment
on a Windows machine:

```
./eng/install-gstreamer-windows.ps1 -Version 1.28.6 -Flavor msvc
```

Outside a workflow it prints the variable to set instead of writing it to
`GITHUB_ENV`, and it never removes an installation that is already there.

Everything the scripts write goes below `artifacts/`, which is ignored by git.

## Pinned versions

| Thing | Pin | Why |
| --- | --- | --- |
| `actions/checkout` `v7`, `actions/upload-artifact` `v7`, `actions/setup-dotnet` `v6`, `actions/cache` `v6` | major version only | current major versions; a major bump is a deliberate edit |
| `msys2/setup-msys2` | `v2` | the `msys2-location` output the MinGW job reads |
| .NET SDK | `global.json` (`10.0.300`, `rollForward: latestFeature`) | one place for the SDK version |
| GStreamer, Windows MSVC | `1.28.6` (`GSTREAMER_VERSION` in the job) | the version the binding is generated from |
| GStreamer, Windows MinGW | whatever MSYS2 ships | the MSYS2 packages are not versioned per release; the ABI probes only require >= 1.24 |
| GStreamer, Linux | `ubuntu-24.04` archive (1.24) | the supported floor |
| GStreamer, macOS | Homebrew `gstreamer` | rolling, currently 1.28 |

## What has been verified, and what has not

Run on the author's Windows machine against a clean export of `7d914e4`
(GStreamer 1.28.6, MinGW, found through the registry probe):

* `dotnet restore`, generator `verify` (271 files up to date),
  `dotnet build -warnaserror` (0 warnings, 0 errors);
* `GstSharp.Generator.Tests` 301/301, `GstSharp.Analyzers.Tests` 31/31,
  `GstSharp.Core.Tests` 32/32, `GstSharp.IntegrationTests` 43/43;
* `AppSinkSpans --mode pull` (120 frames);
* both AOT gates, including the published `AppSinkSpans.exe` producing the same
  checksum as the JIT run;
* `dotnet pack` of the whole solution;
* `actionlint` and a YAML parse of both workflows.

Not reproducible outside a runner, and therefore expected to need one round of
iteration: the apt, Homebrew, MSYS2 and Inno installations, the AOT publish on
a runner image, and the push to GitHub Packages.
