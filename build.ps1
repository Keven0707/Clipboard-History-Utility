param(
    [string]$OutputName = 'QuietClip.exe'
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$outputDirectory = Join-Path $projectRoot 'dist'
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or -not $OutputName.EndsWith('.exe')) {
    throw 'OutputName must be a simple .exe file name.'
}
$outputFile = Join-Path $outputDirectory $OutputName
$compilerCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)

$compilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compilerPath) {
    throw 'Windows C# compiler was not found. Enable .NET Framework 4.x and try again.'
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName

$compilerArguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/codepage:65001',
    '/platform:anycpu',
    "/out:$outputFile",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll',
    '/reference:System.Windows.Forms.dll'
) + $sourceFiles

& $compilerPath $compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host "Build complete: $outputFile"
