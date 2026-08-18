[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [string]$OutputPath = (Join-Path $PSScriptRoot 'publish'),

    [switch]$SkipFrontendBuild
)

$ErrorActionPreference = 'Stop'

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$publishDirectory = [System.IO.Path]::GetFullPath($OutputPath, $repositoryRoot)

if ($publishDirectory -eq $repositoryRoot) {
    throw 'The publish output directory must not be the repository root.'
}

if (-not $publishDirectory.StartsWith($repositoryRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The publish output directory must be inside the repository: $publishDirectory"
}

if (-not $SkipFrontendBuild) {
    Write-Host 'Building frontend...'
    Push-Location (Join-Path $repositoryRoot 'frontend')
    try {
        Invoke-ExternalCommand -FilePath 'npm' -Arguments @('run', 'build')
    }
    finally {
        Pop-Location
    }
}

if (Test-Path -LiteralPath $publishDirectory) {
    Write-Host "Removing previous publish output: $publishDirectory"
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Write-Host "Publishing WEC to: $publishDirectory"
Invoke-ExternalCommand -FilePath 'dotnet' -Arguments @(
    'publish',
    (Join-Path $repositoryRoot 'src/Wec.Host'),
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '--output', $publishDirectory
)

$hostExecutable = Join-Path $publishDirectory 'Wec.Host.exe'
if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
    throw "Publish completed but the expected executable is missing: $hostExecutable"
}

Write-Host "Publish completed: $hostExecutable"
