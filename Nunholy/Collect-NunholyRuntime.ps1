param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,

    [switch]$IncludeGamePath,

    [string]$OutputZip = (Join-Path $PSScriptRoot "Nunholy_IGT_runtime_probe.zip")
)

$ErrorActionPreference = "Stop"
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$DataDir = Join-Path $GameDir "Nunholy_Data"

$requiredFiles = [ordered]@{
    "UnityPlayer.dll" = Join-Path $GameDir "UnityPlayer.dll"
    "Assembly-CSharp.dll" = Join-Path $DataDir "Managed\Assembly-CSharp.dll"
    "UnityEngine.CoreModule.dll" = Join-Path $DataDir "Managed\UnityEngine.CoreModule.dll"
}

$missing = @($requiredFiles.GetEnumerator() | Where-Object { -not (Test-Path -LiteralPath $_.Value -PathType Leaf) })
if ($missing.Count -gt 0) {
    $names = ($missing | ForEach-Object { "  - $($_.Value)" }) -join [Environment]::NewLine
    throw "Required Nunholy runtime files were not found:`n$names"
}

# These improve Unity-version/PDB/thunk identification, but are not present as
# standalone files in every Unity build. Their absence must not abort collection.
$optionalCandidates = [ordered]@{
    "globalgamemanagers" = Join-Path $DataDir "globalgamemanagers"
    "boot.config" = Join-Path $DataDir "boot.config"
    "ScriptingAssemblies.json" = Join-Path $DataDir "ScriptingAssemblies.json"
    "mono-2.0-bdwgc.dll" = Join-Path $GameDir "MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll"
}

$filesToCollect = [ordered]@{}
foreach ($entry in $requiredFiles.GetEnumerator()) {
    $filesToCollect[$entry.Key] = $entry.Value
}

$missingOptional = New-Object System.Collections.Generic.List[string]
foreach ($entry in $optionalCandidates.GetEnumerator()) {
    if (Test-Path -LiteralPath $entry.Value -PathType Leaf) {
        $filesToCollect[$entry.Key] = $entry.Value
    }
    else {
        $missingOptional.Add($entry.Key)
    }
}

# Some Unity packages rename or bundle globalgamemanagers. Pick up a nearby
# variant when one exists, without making it a requirement.
if (-not $filesToCollect.Contains("globalgamemanagers") -and (Test-Path -LiteralPath $DataDir -PathType Container)) {
    $globalManagersVariant = Get-ChildItem -LiteralPath $DataDir -File -Recurse -Filter "globalgamemanagers*" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($globalManagersVariant) {
        $filesToCollect[$globalManagersVariant.Name] = $globalManagersVariant.FullName
        $missingOptional.Remove("globalgamemanagers") | Out-Null
    }
}

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("Nunholy_IGT_probe_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir | Out-Null

try {
    $report = New-Object System.Collections.Generic.List[string]
    $report.Add("Nunholy IGT pure-ASL runtime probe")
    $report.Add("PRIVATE EVIDENCE — NOT PUBLISHABLE")
    $report.Add("This archive contains hashes and metadata for private game runtime files.")
    $report.Add("Collector version: 3")
    $report.Add("Collected UTC: $([DateTime]::UtcNow.ToString('O'))")
    if ($IncludeGamePath) {
        $report.Add("Game directory: $GameDir")
    }
    else {
        $report.Add("Game directory: [REDACTED; rerun with -IncludeGamePath only when necessary]")
    }
    $report.Add("")

    foreach ($entry in $filesToCollect.GetEnumerator()) {
        $source = $entry.Value
        $destination = Join-Path $tempDir $entry.Key
        Copy-Item -LiteralPath $source -Destination $destination

        $item = Get-Item -LiteralPath $source
        $hash = Get-FileHash -LiteralPath $source -Algorithm SHA256
        $version = $item.VersionInfo.FileVersion

        $report.Add("[$($entry.Key)]")
        $report.Add("OriginalName: $($item.Name)")
        $report.Add("Size: $($item.Length)")
        $report.Add("SHA256: $($hash.Hash)")
        if ($version) {
            $report.Add("FileVersion: $version")
        }
        $report.Add("")
    }

    if ($missingOptional.Count -gt 0) {
        $report.Add("Optional files not present (this is OK):")
        foreach ($name in $missingOptional) {
            $report.Add("  - $name")
        }
        $report.Add("")
    }

    $report | Set-Content -LiteralPath (Join-Path $tempDir "report.txt") -Encoding UTF8

    if (Test-Path -LiteralPath $OutputZip) {
        Remove-Item -LiteralPath $OutputZip -Force
    }

    Compress-Archive -Path (Join-Path $tempDir "*") -DestinationPath $OutputZip -CompressionLevel Optimal

    Write-Host "Created private evidence archive: $OutputZip"
    if ($missingOptional.Count -gt 0) {
        Write-Warning ("Optional files were not found (collection is still valid): " + ($missingOptional -join ", "))
    }
    Write-Warning "PRIVATE EVIDENCE ONLY: do not publish this archive or its original game paths."
}
finally {
    if (Test-Path -LiteralPath $tempDir) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force
    }
}
