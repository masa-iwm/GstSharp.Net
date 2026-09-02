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
| `verify` | `ubuntu-latest` | no | generator drift (the whole generated tree plus `girs/skip-report.md`), warning-free build, generator/analyzer tests, the proof that `GstSharp.Core.Tests` needs no installation, and the pack that validates the public surface against the published baseline |
| `linux` | `ubuntu-24.04` | apt | the Linux SONAME path of `NativeLoader`, the only plugin set that can run the WebRTC tests, and the `linux-x64` NativeAOT gate |
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
gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-plugins-bad
gstreamer1.0-nice gstreamer1.0-tools
```

The `-dev` packages are deliberately not installed. The binding loads versioned
SONAMEs (`libgstreamer-1.0.so.0`, `libgstapp-1.0.so.0`, see `NativeNames`),
which the runtime packages provide; `-dev` would only add headers and the
unversioned `.so` symlinks, which nothing here uses.

`libgstreamer-plugins-bad1.0-0` and `libges-1.0-0` are libraries rather than
plugin sets. Every binding assembly registers its types from a module
initialiser, and `TypeRegistry.Freeze` resolves them all, so a library a module
names has to be there even when no test builds an element out of it:
`libgstwebrtc-1.0.so.0` comes from the bad libraries package and
`libges-1.0.so.0` from `libges-1.0-0`. The latter earns its place twice — it
ships the `nle` and `ges` plugins beside the library, and `ges_init` fails
outright when the non linear engine is not in the registry ("The `nle` plugin is
missing", which is the library's own wording).

This is also the only leg that installs the bad *plugins*, and it does so for
one element: `webrtcbin`. Everywhere else the plugin half is deliberately left
out, which used to mean that every test behind
`[RequiresElementFact("webrtcbin", …)]` was skipped on every leg — a guard that
never runs guards nothing. `gstreamer1.0-nice` completes the element rather than
adding another one: `webrtcbin` is findable without it and cannot leave the NULL
state, because it builds its transports out of `nicesrc` and `nicesink`.

The other side of that decision is `RequiredElementsTests`. The job sets
`GSTSHARP_REQUIRED_ELEMENTS=webrtcbin,nicesrc,dtlssrtpenc` on its integration
test step, and the test fails when any of them is missing from the registry.
Without it, a package that stopped shipping one of the three would turn the
WebRTC tests back into skips and nothing would be red. The variable is unset
everywhere else, where the test asserts nothing: it is the promise of a leg that
installs a plugin set on purpose, not a switch.

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
1.24 throws `EntryPointNotFoundException`. `BasicTutorial08` still looks
`wavescope` up before it builds its visualization branch and runs without the
branch when the factory is absent, and `BasicTutorial07` does the same; on this
leg the factory is found, because `gstreamer1.0-plugins-bad` is installed for
`webrtcbin`, so both visualization branches are exercised here rather than
skipped — and the fallback the two of them share is exercised by no leg at all.

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

The script is RID agnostic — the only thing it decides from `-Rid` is whether
the file it runs ends in `.exe` — so the `linux` job runs the first gate as
well:

```
./eng/aot-gate.ps1 -Project samples/AotSmoke -Rid linux-x64
```

`pwsh` is on the ubuntu images and so is what ILC links with (`clang`, `zlib`),
and the run half of the gate finds the library the job installed from apt;
`AotSmoke` needs nothing beyond core, base, controller and `fakesink`. Only that
one sample is gated there. The AppSinkSpans gate measures the GType registry
under full trimming, which is a decision ILC makes from the same IL whatever the
RID; what is genuinely per-RID is the compile, the native link and the load of a
binary with no host beside it, and `AotSmoke` already covers those. A second ILC
publish would roughly double the minutes the leg spends to repeat the first
answer.

## The surface check

The last step of `verify` packs the whole solution:

```
dotnet pack GstSharp.Net.slnx --no-restore --configuration Release \
    --output artifacts/surface-check -p:Version=1.28.999-surface-check
```

Nothing is pushed and the packages are thrown away with the runner. What the
step is there for is what happens after the pack:
`EnablePackageValidation=true` and `PackageValidationBaselineVersion` in
`src/Directory.Build.props` make the SDK download each package at the baseline
version from nuget.org and compare the assembly about to be packed against the
published one. A public member that disappeared, or that changed its shape, fails the
pack with `CP0002` (or a sibling code) naming the member; an added member says
nothing. That is the README's promise for `1.28.x` turned into a gate.

It runs on every push rather than at release time only, because package
validation is a **pack**-time check and no other job packs. Without this step a
removed member would survive review, sit on `main`, and first turn red when a
tag was already being published.

`1.28.999-surface-check` is a marker version. It sorts above anything the
series will realistically tag, so a package that escapes the runner cannot be
mistaken for a release, and the baseline it is compared against is unaffected
by it — the baseline is named by the property, not by the version being packed.

Two consequences worth knowing:

* The comparison covers exactly the packages that have a shipped baseline.
  Both the baseline download and the check itself are conditioned on `IsPackable` by the
  SDK, and `GstSharp.Net.Analyzers` sets it to `false`: it ships inside
  `GstSharp.Net` and has no package of its own to compare against. A module
  added after the current baseline is the one case that stays outside: it turns
  `EnablePackageValidation` off in its own project until its first release is on
  nuget.org, because there is no baseline to restore and the pack would fail on
  the missing package rather than on an API change.
* The baseline packages join the restore graph as `PackageDownload` items, so
  every job fetches them on `dotnet restore`, not only the one that packs.
  `**/Directory.Build.props` had to join the NuGet cache key for that to be
  paid once: the properties live there, the key hashed only
  `Directory.Packages.props`, the `.csproj` files and `global.json`, and
  `actions/cache` does not write a new cache when the key it was given already
  exists. The baselines — one per package that has one — would have been
  downloaded on every run of every job and cached on none of them.

The baseline moves with the GStreamer series, not with the patch level: `1.30`
is the release allowed to break compilation, and its first package becomes the
new `PackageValidationBaselineVersion`.

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

`.github/workflows/publish-nuget.yml` is the nuget.org half of the same story:
it packs the solution the way `release.yml` does, checks the package count
against the same frozen `expected`, logs in with `NuGet/login` and pushes with
`--skip-duplicate`.

### Before tagging

Two standing rules that outlive a single release and are easy to miss:

* **Confirm the nuget.org Trusted Publishing policy lists every package ID that
  is about to be pushed.** A policy that misses one lets the push start and then
  stops it part-way, which leaves a release half published.
* **A package added to the repository carries
  `<EnablePackageValidation>false</EnablePackageValidation>`, with a comment
  saying why, until its first release has shipped it** — there is no baseline on
  nuget.org to compare it against, and the pack would fail on the missing
  package rather than on an API change. The patch after that first release
  removes the property and the comment, and moves
  `PackageValidationBaselineVersion` forward to the version that shipped it.
  Until that happens, such a package is one the surface check does not cover.

## Docs

`.github/workflows/docs.yml` builds the documentation site with docfx and
deploys it to GitHub Pages. It runs on every push to `main` and on demand; it
is deliberately not part of `ci.yml`, because a pull request has nothing to
deploy to.

* **Pages has to be configured with the build type "GitHub Actions"**
  (Settings -> Pages -> Build and deployment -> Source). With the legacy branch
  source the `deploy` job fails: the `github-pages` environment it writes to
  does not exist.
* The two jobs are the shape the Pages actions expect: `build` uploads the
  rendered site with `upload-pages-artifact`, and `deploy` — the only job with
  `pages: write` and `id-token: write` — publishes it with `deploy-pages`.
  `configure-pages` is not needed when the artifact is uploaded that way.
* `concurrency: pages` with `cancel-in-progress: false`, so that two pushes in
  a row queue rather than interrupt a half-finished deployment.
* **No restore step.** `docfx metadata` compiles the seventeen packable projects
  through MSBuild and restores them itself, so the workflow goes straight from
  `dotnet tool restore` (docfx is pinned in `.config/dotnet-tools.json`) to
  `dotnet docfx docfx/docfx.json`.
* The run ends with warnings and that is expected — the job is green as long as
  the exit code is zero. Most of them are links in the *generated* XML
  documentation to native C symbols such as `GST_PAD_SRC`, which have no page
  in a managed reference; those are tracked as backlog work for the generator
  and are never fixed in `Generated/`. The rest are pre-existing and outside
  the site's control: two `Duplicate source file` warnings for
  `AnalyzerReleases.{Shipped,Unshipped}.md`, raised while loading the
  transitively referenced analyzer project and not suppressible (docfx's
  `rules` map keys on a log code, which that warning does not carry), and two
  duplicated-member warnings for `Gst.Interop.ModuleTypeEntry`.

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
dotnet pack GstSharp.Net.slnx --no-restore --configuration Release --output artifacts/surface-check -p:Version=1.28.999-surface-check

# the native jobs (needs a GStreamer installation the loader can find)
dotnet test tests/GstSharp.IntegrationTests --no-restore
dotnet run --project samples/AppSinkSpans --no-restore -- --mode pull
dotnet run --project samples/AppSrcPush --no-restore -- --buffers 200 --output appsrc-push.raw

# the ported tutorials the Linux job runs, and the media they run against
dotnet run --project samples/GstLaunch --no-restore -- -q videotestsrc num-buffers=300 ! video/x-raw,width=320,height=240,framerate=30/1 ! videoconvert ! theoraenc ! oggmux name=mux ! filesink location=tutorial-media.ogg audiotestsrc num-buffers=430 ! audioconvert ! vorbisenc ! mux.
dotnet run --project samples/tutorials/BasicTutorial02 --no-restore -- --headless
dotnet run --project samples/tutorials/BasicTutorial03 --no-restore -- --headless tutorial-media.ogg
dotnet run --project samples/tutorials/BasicTutorial04 --no-restore -- --headless --seek-at 1 --seek-to 7 tutorial-media.ogg
dotnet run --project samples/tutorials/BasicTutorial07 --no-restore -- --headless --buffers 200
dotnet run --project samples/tutorials/BasicTutorial08 --no-restore -- --headless --chunks 200
dotnet run --project samples/tutorials/BasicTutorial09 --no-restore -- tutorial-media.ogg
dotnet run --project samples/tutorials/BasicTutorial13 --no-restore -- --headless --keys SsPNNPDq tutorial-media.ogg

# the gst-play port, against the same generated ogg
dotnet run --project samples/GstPlay --no-restore -- --duration 2 --videosink fakesink --audiosink fakesink tutorial-media.ogg
dotnet run --project samples/GstPlay --no-restore -- --list-visualizations

# the plain application sample, run the way the native jobs run it: no URI, so
# the pipeline is the built in test pattern into a fakesink
dotnet run --project samples/PlaybinPlayer --no-restore -- --timeout 30

# the AOT gates (Windows)
./eng/aot-gate.ps1 -Project samples/AotSmoke -Rid win-x64
./eng/aot-gate.ps1 -Project samples/AppSinkSpans -Rid win-x64 -Property InvariantGlobalization=true -RunArguments '--mode','pull'

# the AOT gate the linux job runs (from pwsh on a Linux machine)
./eng/aot-gate.ps1 -Project samples/AotSmoke -Rid linux-x64

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
| .NET SDK | `global.json` (`10.0.100`, `rollForward: latestFeature`) | one place for the SDK version. The floor is the whole .NET 10 line rather than a feature band: every gate — build, the four test suites, generator determinism, package validation and the NativeAOT publish — was verified on 10.0.111, so a narrower floor would turn a working SDK away. `latestFeature` always climbs to the newest band present, so this floor decides only what is *refused*: CI runs on whatever the runners ship (10.0.400 when this was written) and a contributor runs on whatever they have |
| docfx | `2.78.5` (`.config/dotnet-tools.json`) | the documentation site is built from a pinned tool, so a local preview and the `Docs` workflow render the same thing. `rollForward: false`, so the tool refuses to run on a runtime other than the one it targets rather than rolling forward silently |
| Package validation baseline | `1.28.5` (`PackageValidationBaselineVersion` in `src/Directory.Build.props`) | the newest published 1.28.x, moved forward once nuget.org serves each release. Following the newest release is what puts each release's additions under the guard; against an older one they could vanish unnoticed. Never 1.28.0, which predates the promise. The anchor starts over at the next GStreamer series |
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
