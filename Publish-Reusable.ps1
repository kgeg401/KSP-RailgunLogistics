param(
    [string]$BundleVersion='0.3.0',
    [string]$BootstrapVersion='0.2.5',
    [Parameter(Mandatory=$true)][string]$ServerPath,
    [switch]$Publish
)
$ErrorActionPreference='Stop'
$repo='kgeg401/KSP-RailgunLogistics'
$build=Join-Path $PSScriptRoot "update-release\$BundleVersion"
$bootstrap=Join-Path $PSScriptRoot "update-release\$BootstrapVersion"
[xml]$full=Get-Content -LiteralPath (Join-Path $build 'friends.xml') -Raw
[xml]$bridge=Get-Content -LiteralPath (Join-Path $bootstrap 'friends.xml') -Raw
if($full.Release.ReusableFleet -ne 'true' -or $bridge.Release.ReusableFleet -eq 'true') { throw 'Expected one full fleet release and one classic updater bridge.' }
foreach($path in @('Updater/ApplyUpdate.exe','Updater/RailgunLogistics.Updater.dll')) {
    if(($full.Release.File | Where-Object Path -eq $path).Sha256 -ne ($bridge.Release.File | Where-Object Path -eq $path).Sha256) { throw 'Both stages must contain the same compatible updater and helper.' }
}
$clientHash=($full.Release.File | Where-Object Path -eq 'Plugins/RailgunLogistics.dll').Sha256
[xml]$control=Get-Content -LiteralPath (Join-Path $ServerPath 'Config\LMPModControl.xml') -Raw
$entry=$control.SelectSingleNode("//MandatoryPlugins/DllFile[translate(FilePath,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='railgunlogistics/plugins/railgunlogistics.dll']")
if(!$entry -or $entry.Sha.Replace('-','') -ne $clientHash) { throw 'The host must require the exact integrated gameplay DLL.' }
foreach($part in @('krlReusableProbe','krlReusableFuelCargo','krlReusableMonoCargo')) {
    if(!$control.SelectSingleNode("//AllowedParts/string[text()='$part']")) { throw "Host is missing reusable part $part." }
}
$serverRoot=(Resolve-Path -LiteralPath $ServerPath).Path
$running=Get-CimInstance Win32_Process -Filter "Name='Server.exe'" | Where-Object ExecutablePath -eq (Join-Path $serverRoot 'Server.exe')
if(!$running) { throw 'Start and verify the matching host before promotion.' }
$plugin=Join-Path $serverRoot 'Plugins\RailgunLogistics.Server.dll'
if([Reflection.AssemblyName]::GetAssemblyName($plugin).Version.ToString(3) -ne $BundleVersion) { throw 'Expected the matching shared-fleet server plugin.' }
$changed=@(Get-Item -LiteralPath $plugin,(Join-Path $serverRoot 'Config\LMPModControl.xml')) | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if($running.CreationDate -lt $changed.LastWriteTime) { throw 'The host must restart after its plugin and mod-control update.' }
foreach($folder in @($build,$bootstrap)) {
    [xml]$descriptor=Get-Content -LiteralPath (Join-Path $folder 'friends.xml') -Raw
    $zip=Join-Path $folder "RailgunLogistics-Update-$($descriptor.Release.Version).zip"
    if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $descriptor.Release.Sha256) { throw 'A staged update ZIP no longer matches its manifest.' }
}
Write-Output "Prepared shared fleet $BundleVersion through updater bridge $BootstrapVersion. The server accepts client $clientHash."
if(!$Publish) { Write-Output 'Preview only; releases and both update channels are unchanged.'; return }
function GhJson([string[]]$Arguments) {
    $response=& gh @Arguments
    if($LASTEXITCODE -ne 0) { throw 'GitHub operation failed; inspect the existing release before retrying.' }
    return ($response | ConvertFrom-Json)
}
function PostJson([string]$Endpoint,$Body,[string]$Name,[string]$Method='POST') {
    $file=Join-Path $build $Name
    [IO.File]::WriteAllText($file,($Body | ConvertTo-Json -Depth 12))
    return (GhJson -Arguments @('api','--method',$Method,$Endpoint,'--input',$file))
}
# Old helpers cannot enforce new dependency requirements. Their channel receives
# only an unchanged classic gameplay binary plus the newer checking updater.
$previous=GhJson -Arguments @('api',"repos/$repo/contents/channel/friends.xml?ref=main")
[xml]$previousManifest=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($previous.content))
$priorGameplay=($previousManifest.Release.File | Where-Object Path -eq 'Plugins/RailgunLogistics.dll').Sha256
if($priorGameplay -ne ($bridge.Release.File | Where-Object Path -eq 'Plugins/RailgunLogistics.dll').Sha256) { throw 'The updater bridge would change gameplay for an old helper.' }
foreach($folder in @($bootstrap,$build)) {
    [xml]$descriptor=Get-Content -LiteralPath (Join-Path $folder 'friends.xml') -Raw
    $version=$descriptor.Release.Version; $tag="v$version"
    $listed=GhJson -Arguments @('release','list','--repo',$repo,'--limit','100','--json','tagName')
    if(!@($listed | Where-Object tagName -eq $tag).Count) {
        & gh release create $tag --repo $repo --target main --title "Railgun Logistics $version" --notes-file (Join-Path $folder 'release-notes.md') --draft --prerelease
        if($LASTEXITCODE -ne 0) { throw 'Draft creation failed; channels are unchanged.' }
    }
    $release=GhJson -Arguments @('release','view',$tag,'--repo',$repo,'--json','assets,isDraft')
    foreach($name in @("RailgunLogistics-$version.zip","RailgunLogistics-Update-$version.zip")) {
        $file=Join-Path $folder $name; $expected='sha256:'+(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $asset=$release.assets | Where-Object name -eq $name
        if(!$asset) {
            & gh release upload $tag $file --repo $repo
            if($LASTEXITCODE -ne 0) { throw 'Asset upload failed; channels are unchanged.' }
            $release=GhJson -Arguments @('release','view',$tag,'--repo',$repo,'--json','assets,isDraft')
            $asset=$release.assets | Where-Object name -eq $name
        }
        if(!$asset -or $asset.digest -ne $expected) { throw 'Uploaded asset differs; it was not overwritten.' }
    }
    if($release.isDraft) {
        & gh release edit $tag --repo $repo --draft=false
        if($LASTEXITCODE -ne 0) { throw 'Release publication failed; channels are unchanged.' }
    }
}
$head=GhJson -Arguments @('api',"repos/$repo/git/ref/heads/main")
$base=GhJson -Arguments @('api',"repos/$repo/git/commits/$($head.object.sha)")
$tree=PostJson "repos/$repo/git/trees" @{
    base_tree=$base.tree.sha
    tree=@(
        @{path='channel/friends.xml';mode='100644';type='blob';content=[IO.File]::ReadAllText((Join-Path $bootstrap 'friends.xml'))},
        @{path='channel/reusable.xml';mode='100644';type='blob';content=[IO.File]::ReadAllText((Join-Path $build 'friends.xml'))}
    )
} 'channel-tree.json'
$commit=PostJson "repos/$repo/git/commits" @{message="Promote shared fleet $BundleVersion with compatible updater bridge";tree=$tree.sha;parents=@($head.object.sha)} 'channel-commit.json'
# A non-fast-forward update is rejected if main changes during publication.
$null=PostJson "repos/$repo/git/refs/heads/main" @{sha=$commit.sha;force=$false} 'channel-ref.json' 'PATCH'
$verified=GhJson -Arguments @('api',"repos/$repo/git/ref/heads/main")
if($verified.object.sha -ne $commit.sha) { throw 'Publication did not retain the expected channel commit.' }
Write-Output "Published both update stages and atomically promoted the channels: https://github.com/$repo/commit/$($commit.sha)"
