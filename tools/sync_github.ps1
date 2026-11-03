# Mirrors the public part of this folder into a clone of
# github.com/LordMikoda/FFR-Randomizer, then commits and pushes. The vault is
# the source; the planning note and build output never leave it.
# Run: powershell -ExecutionPolicy Bypass -File tools/sync_github.ps1 -Repo <clone> -Message "<commit message>"
param(
    [Parameter(Mandatory)] [string]$Repo,
    [Parameter(Mandatory)] [string]$Message
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$folders = "mod", "gui", "installer", "tools", "art"
$files = "release\README.txt"
$skip = '\\gui\\build\\|\\Randomizer\.txt$|\\Spoiler_[^\\]*$'

# Clear what the mirror owns, so deleted files go too.
foreach ($folder in $folders) {
    $target = Join-Path $Repo $folder
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
}
foreach ($folder in $folders) {
    $source = Join-Path $root $folder
    foreach ($file in Get-ChildItem $source -Recurse -File) {
        if ($file.FullName -match $skip) { continue }
        $target = Join-Path $Repo $file.FullName.Substring($root.Length + 1)
        New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
        Copy-Item $file.FullName $target
    }
}
foreach ($file in $files) {
    $target = Join-Path $Repo $file
    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
    Copy-Item (Join-Path $root $file) $target
}
# Repo-only files (README.md, LICENSE, .gitignore) live in github/.
Copy-Item (Join-Path $root "github\*") $Repo -Force
Copy-Item (Join-Path $root "github\.gitignore") $Repo -Force

git -C $Repo add -A
git -C $Repo diff --cached --quiet
if ($LASTEXITCODE -eq 0) {
    Write-Output "Nothing changed."
    return
}
git -C $Repo commit -q -m $Message
git -C $Repo push -q origin main
Write-Output "Pushed: $Message"
