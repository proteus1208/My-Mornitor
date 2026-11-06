param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$dotnet = 'dotnet'
$project = Join-Path $PSScriptRoot 'src\NjcAI.Monitor\NjcAI.Monitor.csproj'
$output = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $PSScriptRoot "dist\$Runtime"
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$config = Join-Path $PSScriptRoot 'NuGet.Config'

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

& $dotnet restore $project --configfile $config -r $Runtime
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& $dotnet publish $project -c Release -r $Runtime --no-restore --self-contained $selfContained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$executable = Join-Path $output 'NJC AI Monitor.exe'
$targetArchitecture = if ($Runtime -eq 'win-arm64') { 'Arm64' } else { 'X64' }
$processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
if ($processArchitecture -eq $targetArchitecture) {
    $testExtractionRoot = Join-Path (Join-Path $PSScriptRoot 'dist') `
        ("NJC-AI-Monitor-selftest-{0}" -f [Guid]::NewGuid().ToString('N'))
    $previousExtractionRoot = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
    try {
        New-Item -ItemType Directory -Path $testExtractionRoot -Force | Out-Null
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $testExtractionRoot
        $test = Start-Process -FilePath $executable -ArgumentList '--self-test' `
            -WindowStyle Hidden -Wait -PassThru
        try {
            $testExitCode = $test.ExitCode
        }
        finally {
            $test.Dispose()
        }
    }
    finally {
        if ([string]::IsNullOrWhiteSpace($previousExtractionRoot)) {
            Remove-Item Env:DOTNET_BUNDLE_EXTRACT_BASE_DIR -ErrorAction SilentlyContinue
        }
        else {
            $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousExtractionRoot
        }
        $resolvedTemp = [System.IO.Path]::GetFullPath(
            (Join-Path $PSScriptRoot 'dist')).TrimEnd('\') + '\'
        $resolvedExtraction = [System.IO.Path]::GetFullPath($testExtractionRoot)
        if ($resolvedExtraction.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedExtraction).StartsWith('NJC-AI-Monitor-selftest-',
                [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolvedExtraction)) {
            Remove-Item -LiteralPath $resolvedExtraction -Recurse -Force
        }
    }
    if ($testExitCode -ne 0) {
        throw "NJC AI Monitor self-test failed with exit code $testExitCode."
    }
} else {
    Write-Host "Self-test skipped for cross-architecture target $Runtime (host: $processArchitecture)."
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\LICENSE') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\NOTICE.md') -Destination $output -Force
Write-Host "Windows build ready: $output"
