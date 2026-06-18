# Publishes a self-contained, single-file AimDecider.exe that runs standalone
# on 64-bit Windows with no .NET install required on the target machine.
$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot "src\AimDecider\AimDecider.csproj"

& dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishDir = Join-Path $repoRoot "src\AimDecider\bin\Release\net8.0-windows\win-x64\publish"
$exePath = Join-Path $publishDir "AimDecider.exe"

Write-Host ""
Write-Host "Published: $exePath"
if (Test-Path $exePath) {
    $sizeMb = [Math]::Round((Get-Item $exePath).Length / 1MB, 1)
    Write-Host "Size: $sizeMb MB"
}
