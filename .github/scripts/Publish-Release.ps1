param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Tag,
    [string]$AssetDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\release')
)
$ErrorActionPreference = 'Stop'
# gh release view returns nonzero for a missing release; inspect it explicitly.
$PSNativeCommandUseErrorActionPreference = $false
if (-not $env:GITHUB_REPOSITORY) { throw 'GITHUB_REPOSITORY must identify the destination repository.' }
$version = $Tag.Substring(1)
$archive = Join-Path $AssetDirectory "DisplayVeil-$version-win.zip"
$checksum = "$archive.sha256"
foreach ($asset in @($archive, $checksum)) {
    if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) { throw "Missing release asset: $asset" }
}
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedLine = "$actualHash  $([IO.Path]::GetFileName($archive))"
if ((Get-Content -LiteralPath $checksum -Raw).Trim() -cne $expectedLine) {
    throw 'Release archive checksum does not match. Nothing was published.'
}

$existingJson = & gh release view $Tag --repo $env:GITHUB_REPOSITORY --json isDraft,url 2>$null
if ($LASTEXITCODE -eq 0) {
    $existing = $existingJson | ConvertFrom-Json
    if (-not $existing.isDraft) {
        Write-Output "Release is already published; its assets remain unchanged: $($existing.url)"
        return
    }
} else {
    & gh release create $Tag --repo $env:GITHUB_REPOSITORY --verify-tag --draft --title "Display Veil $version" --notes-file (Join-Path $PSScriptRoot '..\release-notes.md')
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the draft release.' }
}

# A failed upload leaves a draft. Rerunning replaces assets only in that draft.
& gh release upload $Tag $archive $checksum --repo $env:GITHUB_REPOSITORY --clobber
if ($LASTEXITCODE -ne 0) { throw 'Asset upload failed. The release remains a draft.' }
& gh release edit $Tag --repo $env:GITHUB_REPOSITORY --draft=false
if ($LASTEXITCODE -ne 0) { throw 'Assets were uploaded, but publishing the release failed.' }
Write-Output "Published Display Veil $version."
