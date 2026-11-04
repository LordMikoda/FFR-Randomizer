# Builds a release: the app, then FFRRandomizer-Setup.exe with the app, README
# and mod folder inside, then FFRRandomizer-<version>.zip holding Setup, the
# portable app (Portable\FFRRandomizer.exe) and the README. Output goes to release/<version>/.
# Run: powershell -ExecutionPolicy Bypass -File tools/make_release.ps1 -Version 0.1.1
# -Ue4ss is a folder laid out like the game's Win64 folder (dwmapi.dll, ue4ss\...)
# plus ue4ss-version.txt; Setup offers to install it when a player has no UE4SS.
# It lives outside the vault: copy it from a tested game install. Pass "" to
# leave UE4SS out.
param([Parameter(Mandatory)] [string]$Version, [string]$Ue4ss = "D:\AI Stuff\Exports\UE4SS-4.0.0-rc1")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "release\$Version"
$payload = Join-Path $out "payload"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force (Join-Path $payload "FFRRandomizer") | Out-Null

& cmd /c (Join-Path $root "gui\build.cmd")
if ($LASTEXITCODE -ne 0) { throw "GUI build failed" }
Copy-Item (Join-Path $root "gui\build\FFRRandomizer.exe") $payload
Copy-Item (Join-Path $root "release\README.txt") $payload

# The mod folder, without a player's Randomizer File or spoiler logs.
$mod = Join-Path $root "mod\FFRRandomizer"
foreach ($file in Get-ChildItem $mod -Recurse -File) {
    if ($file.Name -eq "Randomizer.txt" -or $file.Name -like "Spoiler_*") { continue }
    $target = Join-Path (Join-Path $payload "FFRRandomizer") $file.FullName.Substring($mod.Length + 1)
    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
    Copy-Item $file.FullName $target
}

if ($Ue4ss -ne "") {
    if (-not (Test-Path (Join-Path $Ue4ss "ue4ss-version.txt"))) { throw "No ue4ss-version.txt in $Ue4ss" }
    Copy-Item -Recurse $Ue4ss (Join-Path $payload "ue4ss")
}

$setup = Join-Path $out "FFRRandomizer-Setup.exe"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "installer\build.ps1") -Payload $payload -Version $Version -Out $setup
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }

# Entry names are written by hand so they use forward slashes.
$zipPath = Join-Path $out "FFRRandomizer-$Version.zip"
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, "Create")
try {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $setup, "FFRRandomizer-Setup.exe")
    # The portable app: the same exe Setup installs, for players who only
    # need the new app over a mod that's already new enough.
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $payload "FFRRandomizer.exe"), "Portable/FFRRandomizer.exe")
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root "release\README.txt"), "README.txt")
} finally {
    $zip.Dispose()
}
Remove-Item -Recurse -Force $payload
Write-Output "Wrote $zipPath ($([Math]::Round((Get-Item $zipPath).Length / 1KB)) KB)"
