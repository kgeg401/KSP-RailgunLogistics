param(
    [string]$BundleVersion='0.2.1',
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
$clientZip=Join-Path $build "RailgunLogistics-Client-$BundleVersion.zip"
if((Get-FileHash -LiteralPath $clientZip -Algorithm SHA256).Hash -ne $release.Release.Sha256) { throw 'Release ZIP no longer matches its descriptor.' }
Write-Output "Ready: $repo v$BundleVersion. Server accepts client SHA $clientHash."
if(!$Publish) { Write-Output 'Preview only. Pass -Publish after approving public release publication.'; return }
# GitHub connector has no release-asset upload endpoint; CLI is used for those operations.
& gh release create "v$BundleVersion" $clientZip (Join-Path $build "RailgunLogistics-$BundleVersion-Setup.zip") (Join-Path $PSScriptRoot 'RailgunLogistics-0.2.0.zip') --repo $repo --title "Railgun Logistics $BundleVersion - startup updates" --notes-file (Join-Path $build 'release-notes.md') --draft
if($LASTEXITCODE -ne 0) { throw 'Draft release creation failed; channel unchanged.' }
& gh release edit "v$BundleVersion" --repo $repo --draft=false
if($LASTEXITCODE -ne 0) { throw 'Publishing failed; channel unchanged.' }
$channelPath='channel/friends.xml'
$existing=& gh api "repos/$repo/contents/$channelPath" --jq '.sha' 2>$null
$body=@{message="Promote Friends channel to $BundleVersion";content=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $build 'friends.xml')));branch='main'}
if($LASTEXITCODE -eq 0) { $body.sha=$existing.Trim() }
$request=Join-Path $build 'promote-request.json'
$body | ConvertTo-Json | Set-Content -LiteralPath $request -Encoding utf8
& gh api --method PUT "repos/$repo/contents/$channelPath" --input $request --jq '.content.html_url'
if($LASTEXITCODE -ne 0) { throw 'Release exists but channel promotion failed. Existing players keep the previous pinned version.' }
