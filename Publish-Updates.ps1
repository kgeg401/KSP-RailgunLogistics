param(
    [string]$BundleVersion='0.2.3',
    [Parameter(Mandatory=$true)][string]$ServerPath,
    [switch]$Publish
)
$ErrorActionPreference='Stop'
$repo='kgeg401/KSP-RailgunLogistics'
$build=Join-Path $PSScriptRoot "update-release\$BundleVersion"
[xml]$release=Get-Content -LiteralPath (Join-Path $build 'friends.xml') -Raw
$clientHash=($release.Release.File | Where-Object Path -eq 'Plugins/RailgunLogistics.dll').Sha256
[xml]$control=Get-Content -LiteralPath (Join-Path $ServerPath 'Config\LMPModControl.xml') -Raw
$serverHash=$control.SelectSingleNode("//DllFile[FilePath='RailgunLogistics/Plugins/RailgunLogistics.dll']").Sha.Replace('-','')
if($clientHash -ne $serverHash) { throw 'The server must require this exact gameplay DLL before promoting its client release.' }
$serverExe=Join-Path (Resolve-Path -LiteralPath $ServerPath).Path 'Server.exe'
$running=Get-CimInstance Win32_Process -Filter "Name='Server.exe'" | Where-Object ExecutablePath -eq $serverExe
if(!$running) { throw 'Start and verify the server with its matching plugin before promoting the Friends channel.' }
$latestChange=@(Get-Item -LiteralPath (Join-Path $ServerPath 'Plugins\RailgunLogistics.Server.dll'),(Join-Path $ServerPath 'Config\LMPModControl.xml')) | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if($running.CreationDate -lt $latestChange.LastWriteTime) { throw 'Server files changed after its current process started. Restart/verify it before release promotion.' }
$clientZip=Join-Path $build "RailgunLogistics-Update-$BundleVersion.zip"
if((Get-FileHash -LiteralPath $clientZip -Algorithm SHA256).Hash -ne $release.Release.Sha256) { throw 'Release ZIP no longer matches its descriptor.' }
Write-Output "Ready: $repo v$BundleVersion. Server accepts client SHA $clientHash."
if(!$Publish) { Write-Output 'Preview only. Pass -Publish after approving public release publication.'; return }
# GitHub connector has no release-asset upload endpoint; CLI is used for those operations.
$assets=@($clientZip,(Join-Path $build "RailgunLogistics-$BundleVersion.zip"))
$releaseList=& gh release list --repo $repo --limit 100 --json tagName
if($LASTEXITCODE -ne 0) { throw 'Could not inspect existing releases.' }
$releaseItems=$releaseList | ConvertFrom-Json
if(@($releaseItems | Where-Object { $_.tagName -eq "v$BundleVersion" }).Count -eq 0) {
    & gh release create "v$BundleVersion" @assets --repo $repo --title "Railgun Logistics $BundleVersion - startup updates" --notes-file (Join-Path $build 'release-notes.md') --draft
    if($LASTEXITCODE -ne 0) { throw 'Draft release creation failed; channel unchanged.' }
}
$remoteRelease=& gh release view "v$BundleVersion" --repo $repo --json assets,isDraft | ConvertFrom-Json
if($LASTEXITCODE -ne 0) { throw 'Could not inspect uploaded assets.' }
foreach($asset in $assets) {
    $remoteAsset=$remoteRelease.assets | Where-Object name -eq (Split-Path $asset -Leaf)
    $expected='sha256:'+(Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    if(!$remoteAsset -or $remoteAsset.digest -ne $expected) { throw 'Uploaded asset differs or is missing; existing release/channel are not overwritten.' }
}
if($remoteRelease.isDraft) {
    & gh release edit "v$BundleVersion" --repo $repo --draft=false
    if($LASTEXITCODE -ne 0) { throw 'Publishing failed; channel unchanged.' }
}
$channelPath='channel/friends.xml'
$treeJson=& gh api "repos/$repo/git/trees/main?recursive=1"
if($LASTEXITCODE -ne 0) { throw 'Could not inspect the existing channel.' }
$tree=$treeJson | ConvertFrom-Json
$existing=$tree.tree | Where-Object { $_.path -eq 'channel/friends.xml' } | Select-Object -ExpandProperty sha
$body=@{message="Promote Friends channel to $BundleVersion";content=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $build 'friends.xml')));branch='main'}
if($existing) { $body.sha=$existing.Trim() }
$request=Join-Path $build 'promote-request.json'
[IO.File]::WriteAllText($request,($body | ConvertTo-Json))
& gh api --method PUT "repos/$repo/contents/$channelPath" --input $request --jq '.content.html_url'
if($LASTEXITCODE -ne 0) { throw 'Release exists but channel promotion failed. Existing players keep the previous pinned version.' }
