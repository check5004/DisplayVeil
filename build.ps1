param(
    [switch]$Test,
    [switch]$Headless,
    [switch]$Package,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
if ($Headless -and -not $Test) { throw '-Headless requires -Test.' }
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
$binary = Join-Path $output 'DisplayVeil.exe'
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($binary).Version
$fileVersion = [version][Diagnostics.FileVersionInfo]::GetVersionInfo($binary).FileVersion
if ($assemblyVersion -ne $fileVersion -or $fileVersion.Revision -ne 0) {
    throw 'AssemblyVersion and AssemblyFileVersion must match and end in .0.'
}
$version = $fileVersion.ToString(3)
if ($ExpectedVersion -and $ExpectedVersion -cne $version) {
    throw "Release tag version $ExpectedVersion does not match the executable version $version. Update src/AssemblyInfo.cs first."
}
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
    $testArguments = @()
    if ($Headless) { $testArguments += '--headless' }
    & "$testOutput\DisplayVeil.Tests.exe" @testArguments | Tee-Object -FilePath (Join-Path $testOutput 'test-results.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Package) {
    $releaseOutput = Join-Path $root 'artifacts\release'
    New-Item -ItemType Directory -Path $releaseOutput -Force | Out-Null
    $archive = Join-Path $releaseOutput "DisplayVeil-$version-win.zip"
    # Package an explicit file list, never settings or stale files in the output folder.
    $files = @(
        @{ Source = $binary; Entry = 'DisplayVeil/DisplayVeil.exe' },
        @{ Source = "$binary.config"; Entry = 'DisplayVeil/DisplayVeil.exe.config' },
        @{ Source = (Join-Path $root 'README.md'); Entry = 'DisplayVeil/README.md' }
    )
    foreach ($document in Get-ChildItem -LiteralPath (Join-Path $root 'docs') -Filter '*.md') {
        $files += @{ Source = $document.FullName; Entry = 'DisplayVeil/docs/' + $document.Name }
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [IO.File]::Open($archive, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in $files) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.Source, $file.Entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$archive.sha256", "$hash  $([IO.Path]::GetFileName($archive))`n", [Text.Encoding]::ASCII)
    Write-Output "Packaged: $archive"
    Write-Output "Checksum: $archive.sha256"
}
