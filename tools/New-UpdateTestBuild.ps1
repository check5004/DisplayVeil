param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'artifacts\update-check-test'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
# Change only assembly metadata in a separate file. The production sources stay untouched.
$metadata = [IO.File]::ReadAllText((Join-Path $root 'src\AssemblyInfo.cs'))
$metadata = $metadata -replace '(Assembly(?:File)?Version\(")[^"]+("\))', ('${1}' + $Version + '.0${2}')
$testMetadata = Join-Path $output 'AssemblyInfo.cs'
[IO.File]::WriteAllText($testMetadata, $metadata, [Text.UTF8Encoding]::new($false))
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | Where-Object Name -ne 'AssemblyInfo.cs' | ForEach-Object FullName)
$sources += $testMetadata
$arguments = @('/nologo', '/optimize+', '/warn:4', '/warnaserror+', '/platform:anycpu', '/utf8output', '/codepage:65001',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Runtime.Serialization.dll', '/reference:System.Net.Http.dll', "/win32manifest:$root\app.manifest",
    "/win32icon:$root\assets\DisplayVeil.ico", "/resource:$root\assets\DisplayVeil.ico,DisplayVeil.App.ico", '/target:winexe', "/out:$output\DisplayVeil.exe")
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Update test build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination (Join-Path $output 'DisplayVeil.exe.config') -Force
if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $output 'DisplayVeil.exe')).Version.ToString(3) -ne $Version) { throw 'Test version mismatch.' }
Write-Output "Test-only build (do not distribute): $output\DisplayVeil.exe"
Write-Output "Launch with: --settings-dir `"$output\settings`""
