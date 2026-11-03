param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'
$staging = Join-Path $dist (".package-{0}-{1}" -f $Runtime, [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $PSScriptRoot "dist\NJC-AI-Monitor-Windows-$Runtime.zip"

try {
    & (Join-Path $PSScriptRoot 'build.ps1') -Runtime $Runtime -OutputDirectory $staging
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $source = Join-Path $staging '*'
    Compress-Archive -Path $source -DestinationPath $archive -CompressionLevel Optimal -Force
    Write-Host "Package ready: $archive"
}
finally {
    $resolvedDist = [System.IO.Path]::GetFullPath($dist).TrimEnd('\') + '\'
    $resolvedStaging = [System.IO.Path]::GetFullPath($staging)
    if ($resolvedStaging.StartsWith($resolvedDist, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedStaging).StartsWith('.package-', [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStaging)) {
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}
