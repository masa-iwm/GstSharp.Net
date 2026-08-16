#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Publishes a sample with NativeAOT and runs it.

.DESCRIPTION
    The gate has two halves, and both of them matter:

    1. The publish must not produce a single trimming or AOT warning
       (IL2xxx / IL3xxx). TrimmerSingleWarn is turned off so that each warning
       names the member it came from instead of the assembly it lives in.
    2. The published executable must run and exit with 0. The failure mode this
       catches - a GType that never made it into the managed type registry, so
       that a cast returns null - produces no build warning at all
       (docs/ownership.md, "The GType registry").

.PARAMETER Project
    The project to publish, as a directory or a .csproj path, relative to the
    repository root or absolute.

.PARAMETER Rid
    The runtime identifier to publish for.

.PARAMETER Configuration
    The build configuration.

.PARAMETER Property
    Extra MSBuild properties, each as "Name=Value".

.PARAMETER RunArguments
    The command line of the published executable.

.EXAMPLE
    ./eng/aot-gate.ps1 -Project samples/AotSmoke -Rid win-x64

.EXAMPLE
    ./eng/aot-gate.ps1 -Project samples/AppSinkSpans -Rid win-x64 `
        -Property InvariantGlobalization=true -RunArguments '--mode','pull'

.NOTES
    On a development machine the publish can fail with MSB3073 when vswhere.exe
    is not on PATH; see eng/ci-notes.md. The CI runners do not need that
    workaround.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Project,

    [string] $Rid = 'win-x64',

    [string] $Configuration = 'Release',

    [string[]] $Property = @(),

    [string[]] $RunArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot

function Resolve-ProjectFile {
    param([string] $Path)

    $candidate = if ([System.IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $repository $Path }

    if (Test-Path -Path $candidate -PathType Leaf) {
        return (Resolve-Path -Path $candidate).Path
    }

    if (Test-Path -Path $candidate -PathType Container) {
        $files = @(Get-ChildItem -Path $candidate -Filter '*.csproj' -File)
        if ($files.Count -eq 1) {
            return $files[0].FullName
        }

        throw "'$candidate' holds $($files.Count) project files; name one of them explicitly."
    }

    throw "'$candidate' is neither a project file nor a directory."
}

$projectFile = Resolve-ProjectFile -Path $Project
$name = [System.IO.Path]::GetFileNameWithoutExtension($projectFile)

$publishDirectory = Join-Path $repository "artifacts/aot/$name/$Rid"
$logDirectory = Join-Path $repository 'artifacts/logs'
$log = Join-Path $logDirectory "aot-$name.log"

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

$arguments = @(
    'publish', $projectFile,
    '--configuration', $Configuration,
    '--runtime', $Rid,
    '--output', $publishDirectory,
    '-p:PublishAot=true',
    '-p:TrimMode=full',
    '-p:TrimmerSingleWarn=false'
)

foreach ($entry in $Property) {
    $arguments += "-p:$entry"
}

Write-Host "aot-gate: dotnet $($arguments -join ' ')"

# Merging the streams of a native command turns every line it writes to stderr
# into an error record, which would end the script here rather than at the
# check below. The exit code is the thing that decides, so the preference is
# lowered for the call itself.
$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'

try {
    & dotnet @arguments 2>&1 | Tee-Object -FilePath $log
    $published = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previous
}

if ($published -ne 0) {
    throw "aot-gate: the publish of $name failed with exit code $published; the output is in $log."
}

# The publish is expected to fail on these already, because
# TreatWarningsAsErrors is on for the whole repository. The scan is the
# explicit assertion of the gate, and it also catches a warning that a future
# property change would let through.
$warnings = @(Select-String -Path $log -Pattern 'warning\s+IL[0-9]{4}' -AllMatches)

if ($warnings.Count -gt 0) {
    foreach ($warning in $warnings) {
        Write-Host $warning.Line
    }

    throw "aot-gate: the publish of $name produced $($warnings.Count) trimming or AOT warning(s)."
}

# No ternary: the script also has to run under Windows PowerShell 5.1, which a
# development machine may still default to.
if ($Rid.StartsWith('win')) {
    $executable = Join-Path $publishDirectory "$name.exe"
}
else {
    $executable = Join-Path $publishDirectory $name
}

if (-not (Test-Path -Path $executable -PathType Leaf)) {
    throw "aot-gate: the publish of $name produced no executable at $executable."
}

$size = [Math]::Round((Get-Item $executable).Length / 1MB, 1)
Write-Host "aot-gate: publish is clean, $executable is $size MB."
Write-Host "aot-gate: running $name $($RunArguments -join ' ')"

$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'

try {
    & $executable @RunArguments
    $ran = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previous
}

if ($ran -ne 0) {
    throw "aot-gate: the published $name exited with $ran."
}

Write-Host "aot-gate: $name passed."
