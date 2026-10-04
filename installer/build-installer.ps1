[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '0.2.0',
    [ValidateSet('ru-ru', 'en-us')]
    [string] $Culture = 'ru-ru',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$wix = Join-Path $repoRoot '.tools\wix5\wix.exe'
$outputDir = Join-Path $repoRoot 'artifacts\installer'
$publishDir = Join-Path $repoRoot 'artifacts\publish\win-x64'
if (-not (Test-Path -LiteralPath $wix)) {
    throw 'Restore the local WiX .NET tool first. See installer/README.md.'
}

if (-not $SkipPublish) {
    # Fresh folders prevent packaging obsolete files from previous releases.
    $publishDir = Join-Path $repoRoot ('artifacts\publish\win-x64-' + [Guid]::NewGuid().ToString('N'))
    & dotnet publish (Join-Path $repoRoot 'src\LaypersonLogViewer.App\LaypersonLogViewer.App.csproj') `
        -c Release -r win-x64 --self-contained true '-p:PublishTrimmed=false' '-p:DebugType=None' `
        '-p:DebugSymbols=false' "-p:Version=$Version" -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
}
foreach ($file in @('LaypersonLogViewer.App.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $file))) { throw "Missing runtime file: $file" }
}
$config = Get-Content -Raw -LiteralPath (Join-Path $publishDir 'LaypersonLogViewer.App.runtimeconfig.json') | ConvertFrom-Json
if ($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks -or -not $config.runtimeOptions.includedFrameworks) {
    throw 'The publication is not self-contained.'
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'samples') -Destination $publishDir -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $publishDir 'docs') | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\Спецификация.md') -Destination (Join-Path $publishDir 'docs') -Force

# Preserve notices from the runtime and dependencies where supplied in their packages.
$assets = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'src\LaypersonLogViewer.App\obj\project.assets.json') | ConvertFrom-Json
$packageRoots = $assets.packageFolders.PSObject.Properties.Name
$packagePaths = @($assets.libraries.PSObject.Properties.Value | Where-Object type -eq 'package' | ForEach-Object path)
foreach ($framework in $config.runtimeOptions.includedFrameworks) {
    $packagePaths += "microsoft.netcore.app.runtime.win-x64/$($framework.version)"
}
foreach ($packagePath in ($packagePaths | Sort-Object -Unique)) {
    foreach ($root in $packageRoots) {
        $packageDir = Join-Path $root $packagePath
        if (-not (Test-Path -LiteralPath $packageDir)) { continue }
        $notices = Get-ChildItem -LiteralPath $packageDir -File | Where-Object Name -Match '(?i)^(licen[sc]e|notice|third.party)'
        if ($notices) {
            $noticeDir = Join-Path $publishDir ('licenses\' + $packagePath.Replace('/', '-'))
            New-Item -ItemType Directory -Force -Path $noticeDir | Out-Null
            $notices | Copy-Item -Destination $noticeDir -Force
        }
        break
    }
}

function Escape-Xml([string] $value) { [Security.SecurityElement]::Escape($value) }
function Get-StableId([string] $path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($path.ToLowerInvariant())))).Replace('-', '').Substring(0, 32) }
    finally { $sha.Dispose() }
}
$xml = [Text.StringBuilder]::new()
$components = [Collections.Generic.List[string]]::new()
[void]$xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment><DirectoryRef Id="INSTALLFOLDER">')
function Add-PayloadDirectory([string] $path, [string] $relative) {
    $directoryId = Get-StableId "directory/$relative"
    $cleanupId = "Cleanup_$directoryId"
    $components.Add($cleanupId)
    [void]$xml.AppendLine("<Component Id=`"$cleanupId`" Guid=`"*`"><RemoveFolder Id=`"Remove_$directoryId`" On=`"uninstall`"/><RegistryValue Root=`"HKCU`" Key=`"Software\LaypersonLogViewer\Installer\Folders`" Name=`"$directoryId`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`"/></Component>")
    foreach ($file in (Get-ChildItem -LiteralPath $path -File | Sort-Object Name)) {
        $id = Get-StableId "$relative/$($file.Name)"
        $components.Add("File_$id")
        $source = Escape-Xml $file.FullName
        $guid = [Guid]::ParseExact($id, 'N').ToString()
        [void]$xml.AppendLine("<Component Id=`"File_$id`" Guid=`"$guid`"><File Id=`"Payload_$id`" Source=`"$source`"/><RegistryValue Root=`"HKCU`" Key=`"Software\LaypersonLogViewer\Installer\Files`" Name=`"$id`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`"/></Component>")
    }
    foreach ($dir in (Get-ChildItem -LiteralPath $path -Directory | Sort-Object Name)) {
        $childRelative = "$relative/$($dir.Name)"
        $childId = Get-StableId "directory/$childRelative"
        [void]$xml.AppendLine("<Directory Id=`"Dir_$childId`" Name=`"$(Escape-Xml $dir.Name)`">")
        Add-PayloadDirectory $dir.FullName $childRelative
        [void]$xml.AppendLine('</Directory>')
    }
}
Add-PayloadDirectory $publishDir ''
[void]$xml.AppendLine('</DirectoryRef><ComponentGroup Id="PublishedFiles">')
foreach ($id in $components) { [void]$xml.AppendLine("<ComponentRef Id=`"$id`"/>") }
[void]$xml.AppendLine('</ComponentGroup></Fragment></Wix>')
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$payloadSource = Join-Path $outputDir 'Payload.wxs'
[IO.File]::WriteAllText($payloadSource, $xml.ToString(), [Text.UTF8Encoding]::new($false))
$installer = Join-Path $outputDir "LaypersonLogViewer-$Version-win-x64-$Culture.msi"
$language = if ($Culture -eq 'ru-ru') { '1049' } else { '1033' }

Push-Location $repoRoot
try {
    & $wix build (Join-Path $PSScriptRoot 'LaypersonLogViewer.wxs') $payloadSource -arch x64 `
        -ext WixToolset.UI.wixext -culture $Culture -d "AppVersion=$Version" -d "Language=$language" -o $installer
    if ($LASTEXITCODE -ne 0) { throw "MSI compilation failed (exit $LASTEXITCODE)." }
}
finally { Pop-Location }
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -Encoding ascii -LiteralPath "$installer.sha256"
Write-Host "Installer: $installer"
Write-Host "SHA256: $hash"
