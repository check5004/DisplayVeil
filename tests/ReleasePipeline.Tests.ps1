$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publisher = Join-Path $root '.github\scripts\Publish-Release.ps1'
$binary = Join-Path $root 'artifacts\DisplayVeil\DisplayVeil.exe'
$version = ([Reflection.AssemblyName]::GetAssemblyName($binary).Version).ToString(3)
$releaseDirectory = Join-Path $root 'artifacts\release'
$archive = Join-Path $releaseDirectory "DisplayVeil-$version-win.zip"
$checksum = "$archive.sha256"
$script:checks = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
    Write-Output "PASS $Message"
}

# Confirm a stale output file is not included, even on repeated local builds.
$sentinel = Join-Path (Split-Path -Parent $binary) ('not-for-release-' + [Guid]::NewGuid().ToString('N') + '.txt')
try {
    [IO.File]::WriteAllText($sentinel, 'This file must never be distributed.')
    & (Join-Path $root 'build.ps1') -Package -ExpectedVersion $version
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entries = @($zip.Entries | ForEach-Object FullName)
        Assert-True ($entries -contains 'DisplayVeil/DisplayVeil.exe') 'ZIP contains the executable'
        Assert-True ($entries -contains 'DisplayVeil/DisplayVeil.exe.config') 'ZIP contains the required runtime configuration'
        Assert-True ($entries -contains 'DisplayVeil/README.md') 'ZIP contains usage instructions'
        Assert-True ($entries -contains 'DisplayVeil/docs/RELEASING.md') 'ZIP contains release documentation'
        Assert-True (-not ($entries | Where-Object { $_ -match 'not-for-release-|settings\.json|Tests\.exe' })) 'ZIP excludes local settings, tests and stale output files'
    } finally { $zip.Dispose() }
} finally {
    if (Test-Path -LiteralPath $sentinel) { Remove-Item -LiteralPath $sentinel }
}
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-True ((Get-Content -LiteralPath $checksum -Raw).Trim() -ceq "$hash  $([IO.Path]::GetFileName($archive))") 'SHA-256 matches the packaged ZIP'
$rejected = $false
try { & (Join-Path $root 'build.ps1') -ExpectedVersion '99999.0.0' | Out-Null }
catch { $rejected = $_.Exception.Message -like '*does not match the executable version*' }
Assert-True $rejected 'A tag with the wrong version is rejected'

# Mock the CLI at the command boundary: these tests never contact GitHub.
$mockState = @{
    Calls = New-Object 'Collections.Generic.List[object]'
    ReleaseExists = $false
    Draft = $true
    UploadFails = $false
}
$mockGh = {
    $mockState.Calls.Add(@($args))
    $global:LASTEXITCODE = 0
    switch ($args[1]) {
        'view' {
            if (-not $mockState.ReleaseExists) { $global:LASTEXITCODE = 1; return }
            @{ isDraft = $mockState.Draft; url = 'https://example.invalid/release' } | ConvertTo-Json -Compress
        }
        'upload' { if ($mockState.UploadFails) { $global:LASTEXITCODE = 1 } }
    }
}.GetNewClosure()
Set-Item -Path Function:gh -Value $mockGh
function Get-GhOperations($State) { return @($State.Calls | ForEach-Object { $_[1] }) -join ',' }

$savedRepository = $env:GITHUB_REPOSITORY
$savedExitCode = $global:LASTEXITCODE
try {
    $env:GITHUB_REPOSITORY = 'tests/display-veil'
    & $publisher -Tag "v$version" -AssetDirectory $releaseDirectory
    Assert-True ((Get-GhOperations $mockState) -eq 'view,create,upload,edit') 'New release publishes only after uploading assets to a draft'
    Assert-True ($mockState.Calls[1] -contains '--verify-tag') 'Release creation requires an existing remote tag'
    Assert-True ($mockState.Calls[1] -contains '--draft') 'New release starts as a draft'

    $mockState.ReleaseExists = $true
    $mockState.Calls.Clear()
    & $publisher -Tag "v$version" -AssetDirectory $releaseDirectory
    Assert-True ((Get-GhOperations $mockState) -eq 'view,upload,edit') 'A rerun resumes an existing draft'

    $mockState.Draft = $false
    $mockState.Calls.Clear()
    & $publisher -Tag "v$version" -AssetDirectory $releaseDirectory
    Assert-True ((Get-GhOperations $mockState) -eq 'view') 'A published release is never overwritten'

    $mockState.Draft = $true
    $mockState.UploadFails = $true
    $mockState.Calls.Clear()
    $rejected = $false
    try { & $publisher -Tag "v$version" -AssetDirectory $releaseDirectory }
    catch { $rejected = $_.Exception.Message -like '*upload failed*' }
    Assert-True ($rejected -and (Get-GhOperations $mockState) -eq 'view,upload') 'Upload failure leaves the release unpublished'

    $checksumContent = [IO.File]::ReadAllText($checksum)
    try {
        [IO.File]::WriteAllText($checksum, 'incorrect checksum')
        $mockState.Calls.Clear()
        $rejected = $false
        try { & $publisher -Tag "v$version" -AssetDirectory $releaseDirectory }
        catch { $rejected = $_.Exception.Message -like '*checksum does not match*' }
        Assert-True ($rejected -and $mockState.Calls.Count -eq 0) 'Checksum failure prevents all release API calls'
    } finally { [IO.File]::WriteAllText($checksum, $checksumContent, [Text.Encoding]::ASCII) }
} finally {
    $env:GITHUB_REPOSITORY = $savedRepository
    # GitHub's pwsh wrapper exits with LASTEXITCODE. Do not leak an expected mock failure.
    $global:LASTEXITCODE = $savedExitCode
}
Write-Output "RESULT: $script:checks release pipeline checks passed"
