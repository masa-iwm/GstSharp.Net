#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Installs an official GStreamer Windows build and reports where it landed.

.DESCRIPTION
    Since 1.28 the Windows packages are Inno Setup executables, not MSIs, so
    there is no ADDLOCAL=ALL and no msiexec: the installer is started with the
    Inno silent switches and waited for.

    Two things have to be true afterwards, and neither is guaranteed by the
    installer:

    * The environment variables the installer sets are not visible to the
      workflow, because the runner process was started before the installation.
    * The install location depends on whether the installer ran per user or per
      machine.

    The script therefore probes the known roots for the anchor library and
    writes GSTREAMER_1_0_ROOT_<FLAVOR>_<ARCH> and the bin directory into
    GITHUB_ENV and GITHUB_PATH. That variable is the second candidate
    NativeInstallPlanner.EnumerateWindows looks at, so the rest of the job
    resolves the installation deterministically.

.PARAMETER Version
    The GStreamer version to install, for example 1.28.6.

.PARAMETER Flavor
    msvc or mingw.

.PARAMETER Architecture
    The architecture token of the installer file name.

.PARAMETER DownloadDirectory
    Where the installer is downloaded to. Cache this directory to skip the
    download on later runs.

.PARAMETER InstallerArguments
    The switches the installer is started with. /CURRENTUSER can be added when
    a machine wide installation is refused.

.PARAMETER SkipChecksum
    Skips the SHA-256 verification of the download.

.EXAMPLE
    ./eng/install-gstreamer-windows.ps1 -Version 1.28.6 -Flavor msvc
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [ValidateSet('msvc', 'mingw')]
    [string] $Flavor = 'msvc',

    [string] $Architecture = 'x86_64',

    [string] $DownloadDirectory = (Join-Path $env:TEMP 'gstreamer-download'),

    [string[]] $InstallerArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'),

    [switch] $SkipChecksum
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$file = "gstreamer-1.0-$Flavor-$Architecture-$Version.exe"
$url = "https://gstreamer.freedesktop.org/data/pkg/windows/$Version/$Flavor/$file"
$installer = Join-Path $DownloadDirectory $file

New-Item -ItemType Directory -Path $DownloadDirectory -Force | Out-Null

if (Test-Path -Path $installer -PathType Leaf) {
    Write-Host "install-gstreamer: $file is already downloaded."
}
else {
    Write-Host "install-gstreamer: downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $installer -UseBasicParsing
}

if (-not $SkipChecksum) {
    $sums = "$installer.sha256sum"

    try {
        Invoke-WebRequest -Uri "$url.sha256sum" -OutFile $sums -UseBasicParsing
    }
    catch {
        # A missing checksum file is reported, not fatal: the download itself
        # came over TLS from the project's own server.
        Write-Warning "install-gstreamer: no checksum published for $file ($($_.Exception.Message))."
        $sums = $null
    }

    if ($sums) {
        $expected = ((Get-Content -Path $sums -TotalCount 1) -split '\s+')[0]
        $actual = (Get-FileHash -Path $installer -Algorithm SHA256).Hash

        if ($expected -and $actual -ne $expected.ToUpperInvariant()) {
            Remove-Item -Path $installer -Force
            throw "install-gstreamer: $file has SHA-256 $actual but $expected was published."
        }

        Write-Host "install-gstreamer: checksum verified."
    }
}

Write-Host "install-gstreamer: running $file $($InstallerArguments -join ' ')"

# Inno Setup executables are GUI applications: without -Wait the shell returns
# before the installation has even started.
$process = Start-Process -FilePath $installer -ArgumentList $InstallerArguments -Wait -PassThru

if ($process.ExitCode -ne 0) {
    throw "install-gstreamer: the installer exited with $($process.ExitCode)."
}

$directoryName = "${Flavor}_$Architecture"

$roots = @(
    (Join-Path $env:LOCALAPPDATA "Programs\gstreamer\1.0\$directoryName"),
    "C:\gstreamer\1.0\$directoryName",
    (Join-Path ${env:ProgramFiles} "gstreamer\1.0\$directoryName")
)

if ($Flavor -eq 'msvc') {
    $anchor = 'gstreamer-1.0-0.dll'
}
else {
    $anchor = 'libgstreamer-1.0-0.dll'
}

$root = $roots | Where-Object { Test-Path -Path (Join-Path $_ "bin\$anchor") } | Select-Object -First 1

if (-not $root) {
    throw "install-gstreamer: no $anchor below any of: $($roots -join ', ')."
}

$bin = Join-Path $root 'bin'
$variable = "GSTREAMER_1_0_ROOT_$($Flavor.ToUpperInvariant())_$($Architecture.ToUpperInvariant())"

Write-Host "install-gstreamer: installed at $root"
& (Join-Path $bin 'gst-inspect-1.0.exe') --version

if ($env:GITHUB_ENV) {
    "$variable=$root" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append
    $bin | Out-File -FilePath $env:GITHUB_PATH -Encoding utf8 -Append
    Write-Host "install-gstreamer: exported $variable and put $bin on PATH."
}
else {
    Write-Host "install-gstreamer: outside a workflow; set $variable=$root to pin this installation."
}
