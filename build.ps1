param([switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 is required.' }
$output = Join-Path $root 'artifacts\DisplayVeil'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
$common = @('/nologo', '/optimize+', '/warn:4', '/warnaserror+', '/platform:anycpu', '/utf8output', '/codepage:65001', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Runtime.Serialization.dll', "/win32manifest:$root\app.manifest")
& $compiler @common '/target:winexe' "/out:$output\DisplayVeil.exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination (Join-Path $output 'DisplayVeil.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $output -Force
$docsOutput = Join-Path $output 'docs'
New-Item -ItemType Directory -Path $docsOutput -Force | Out-Null
Copy-Item -Path (Join-Path $root 'docs\*.md') -Destination $docsOutput -Force
Write-Output "Built: $output\DisplayVeil.exe"
if ($Test) {
    $testOutput = Join-Path $root 'artifacts\tests'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $tests = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs' | ForEach-Object FullName)
    & $compiler @common '/target:exe' '/main:DisplayVeil.Tests.TestRunner' "/out:$testOutput\DisplayVeil.Tests.exe" @sources @tests
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination (Join-Path $testOutput 'DisplayVeil.Tests.exe.config') -Force
    & "$testOutput\DisplayVeil.Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Package) {
    $archive = Join-Path $root 'artifacts\DisplayVeil-1.0.0-win.zip'
    Compress-Archive -LiteralPath $output -DestinationPath $archive -Force
    Write-Output "Packaged: $archive"
}
