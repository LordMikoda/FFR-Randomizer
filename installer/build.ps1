# Builds the installer with the C# compiler that ships with .NET Framework 4.8.
# -Payload is a folder laid out like the release: FFRRandomizer.exe, README.txt
# and the FFRRandomizer mod folder. Every file in it is embedded.
# Run: powershell -ExecutionPolicy Bypass -File installer/build.ps1 -Payload <folder> -Version 0.1.1 -Out <Setup.exe>
param(
    [Parameter(Mandatory)] [string]$Payload,
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$Out
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$icon = Join-Path $here "..\art\icon.ico"
$Payload = (Resolve-Path $Payload).Path

$work = Join-Path ([IO.Path]::GetTempPath()) ("ffr-setup-" + [guid]::NewGuid())
New-Item -ItemType Directory $work | Out-Null
try {
    $versionFile = Join-Path $work "version.txt"
    [IO.File]::WriteAllText($versionFile, $Version)
    $assemblyInfo = Join-Path $work "Version.cs"
    $numeric = ($Version -replace "[^0-9.]", "") + ".0"
    [IO.File]::WriteAllText($assemblyInfo, "[assembly: System.Reflection.AssemblyVersion(""$numeric"")]")

    $resources = @("/resource:$versionFile,version.txt", "/resource:$icon,icon.ico")
    foreach ($file in Get-ChildItem $Payload -Recurse -File) {
        $relative = $file.FullName.Substring($Payload.Length + 1).Replace("\", "/")
        $resources += "/resource:$($file.FullName),payload/$relative"
    }
    New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
    & $csc /nologo /target:winexe /optimize+ "/out:$Out" "/win32icon:$icon" "/win32manifest:$here\setup.manifest" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll @resources "$here\FFRRandomizerSetup.cs" $assemblyInfo
    if ($LASTEXITCODE -ne 0) { throw "csc failed" }
    Write-Output "Built $Out with $($resources.Count - 2) payload files"
} finally {
    Remove-Item -Recurse -Force $work
}
