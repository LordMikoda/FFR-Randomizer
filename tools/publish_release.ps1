# Publishes a release in one go: builds it (make_release.ps1), mirrors the
# source to the public repo (sync_github.ps1), tags it and creates the GitHub
# release with the zip and release/notes-<version>.md. Writes a log to
# release/<version>/publish.log with the zip's size and SHA-256.
# Run: powershell -ExecutionPolicy Bypass -File tools/publish_release.ps1 -Version 1.0.2
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$Repo = "D:\AI Stuff\Repos\FFR-Randomizer"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$here = $PSScriptRoot
$notes = Join-Path $root "release\notes-$Version.md"
if (-not (Test-Path $notes)) { throw "Missing $notes" }

# gh may not be on PATH in an older shell; winget puts it here.
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) {
    $gh = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GitHub.cli_*\bin\gh.exe" -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $gh) { throw "GitHub CLI (gh) not found" }

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "make_release.ps1") -Version $Version
if ($LASTEXITCODE -ne 0) { throw "make_release.ps1 failed" }
$zip = Join-Path $root "release\$Version\FFRRandomizer-$Version.zip"
if (-not (Test-Path $zip)) { throw "No zip at $zip" }
$log = Join-Path $root "release\$Version\publish.log"
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"zip $((Get-Item $zip).Length) bytes, SHA-256 $hash" | Out-File $log -Encoding utf8

git -C $Repo pull -q origin main
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "sync_github.ps1") -Repo $Repo -Message "FFR Randomizer $Version"
if ($LASTEXITCODE -ne 0) { throw "sync_github.ps1 failed" }
$commit = (git -C $Repo rev-parse HEAD).Trim()
"commit $commit" | Out-File $log -Append -Encoding utf8

git -C $Repo tag "v$Version"
git -C $Repo push -q origin "v$Version"
if ($LASTEXITCODE -ne 0) { throw "Pushing tag v$Version failed" }

& $gh release create "v$Version" $zip --repo LordMikoda/FFR-Randomizer --title "FFR Randomizer $Version" --notes-file $notes --verify-tag --latest
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
"released https://github.com/LordMikoda/FFR-Randomizer/releases/tag/v$Version" | Out-File $log -Append -Encoding utf8
Get-Content $log
