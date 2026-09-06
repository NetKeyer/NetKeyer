param(
    [string]$Version = "",
    [int]$ProtocolVersion = 1,
    [string]$OutputDir = "",
    [switch]$KeepStaging,
    [string]$Tag = "",
    [string]$Commit = "",
    [string]$BuildDate = "",
    [string]$PythonPath = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$ServicesDir = Join-Path $RepoRoot "rendezvous_services"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot "Releases"
}

$PyProjectPath = Join-Path $ServicesDir "pyproject.toml"
$ResolvedVersion = $Version
if ([string]::IsNullOrWhiteSpace($ResolvedVersion) -and (Test-Path $PyProjectPath)) {
    $line = Select-String -Path $PyProjectPath -Pattern '^version\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($line -and $line.Matches.Count -gt 0) {
        $ResolvedVersion = $line.Matches[0].Groups[1].Value
    }
}

if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    $VenvPython = Join-Path $ServicesDir ".venv\Scripts\python.exe"
    if (Test-Path $VenvPython) {
        $PythonPath = $VenvPython
    } else {
        $PythonPath = "python"
    }
}

if ([string]::IsNullOrWhiteSpace($Commit)) {
    try {
        $Commit = (git -C $RepoRoot rev-parse --short HEAD 2>$null).Trim()
    } catch {
        $Commit = ""
    }
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
    try {
        $Tag = (git -C $RepoRoot describe --tags --exact-match 2>$null).Trim()
    } catch {
        if (-not [string]::IsNullOrWhiteSpace($ResolvedVersion)) {
            $Tag = "rs-v$ResolvedVersion"
        }
    }
}

if ([string]::IsNullOrWhiteSpace($BuildDate)) {
    $BuildDate = [DateTime]::UtcNow.ToString("o")
}

$Args = @(
    "release_helper.py",
    "--output-dir", $OutputDir,
    "--protocol-version", $ProtocolVersion,
    "--tag", $Tag,
    "--commit", $Commit,
    "--build-date", $BuildDate,
    "--include-path", "scripts/manage-jwt-keyring.py"
)

if (-not [string]::IsNullOrWhiteSpace($ResolvedVersion)) {
    $Args += @("--version", $ResolvedVersion)
}

if ($KeepStaging) {
    $Args += "--keep-staging"
}

Write-Host "Running release helper with stamp values:" -ForegroundColor Cyan
Write-Host "  Version:  $ResolvedVersion"
Write-Host "  Protocol: $ProtocolVersion"
Write-Host "  Tag:      $Tag"
Write-Host "  Commit:   $Commit"
Write-Host "  Build UTC:$BuildDate"
Write-Host "  Output:   $OutputDir"

Push-Location $ServicesDir
try {
    & $PythonPath @Args
} finally {
    Pop-Location
}
