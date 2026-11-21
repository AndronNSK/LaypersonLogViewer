[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ReleaseJson,
    [string] $OutputPath = 'updates.json'
)
$ErrorActionPreference = 'Stop'
$release = Get-Content -LiteralPath $ReleaseJson -Raw | ConvertFrom-Json
if ($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^\d+\.\d+\.\d+$') {
    throw 'The update manifest requires a published stable release with a numeric version.'
}
$version = $release.tag_name
$packages = foreach ($platform in @('win-x64', 'linux-x64')) {
    $name = if ($platform -eq 'win-x64') { "LaypersonLogViewer-$version-win-x64-ru-ru.msi" }
        else { "layperson-log-viewer_${version}_ubuntu22.04_amd64.deb" }
    $asset = @($release.assets | Where-Object name -eq $name)
    $url = "https://github.com/AndronNSK/LaypersonLogViewer/releases/download/$version/$name"
    if ($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded' -or $asset[0].size -le 0 -or
        $asset[0].digest -notmatch '^sha256:[a-fA-F0-9]{64}$' -or $asset[0].browser_download_url -ne $url) {
        throw "Missing or invalid installer metadata for $platform."
    }
    [ordered]@{
        Platform = $platform
        FileName = $name
        DownloadUrl = $url
        Size = [long]$asset[0].size
        Sha256 = $asset[0].digest.Substring(7)
    }
}
$manifest = [ordered]@{ FormatVersion = 1; Version = $version; Packages = @($packages) }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Output "Update manifest written to $OutputPath"
