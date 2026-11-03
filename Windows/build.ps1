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
    $test = Start-Process -FilePath $executable -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) {
        throw "NJC AI Monitor self-test failed with exit code $($test.ExitCode)."
    }
} else {
    Write-Host "Self-test skipped for cross-architecture target $Runtime (host: $processArchitecture)."
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\LICENSE') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\NOTICE.md') -Destination $output -Force
Write-Host "Windows build ready: $output"
