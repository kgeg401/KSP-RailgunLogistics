param([string]$BundleVersion='0.2.1')
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$build=Join-Path $project "update-release\$BundleVersion"
$lab=Join-Path $project ('evidence\updater-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$game=Join-Path $lab 'fake-game'
$mod=Join-Path $game 'GameData\RailgunLogistics'
$lmp=Join-Path $game 'GameData\LunaMultiplayer\Plugins'
New-Item -ItemType Directory -Path $mod,$lmp -Force | Out-Null
Copy-Item -LiteralPath $env:ComSpec -Destination (Join-Path $game 'KSP_x64.exe')
Copy-Item -LiteralPath 'C:\SteamLibrary\steamapps\common\Kerbal Space Program\GameData\LunaMultiplayer\Plugins\LmpClient.dll' -Destination $lmp
Set-Content -LiteralPath (Join-Path $game 'buildID64.txt') -Value 'build id = 03190'
Set-Content -LiteralPath (Join-Path $mod 'Settings.xml') -Value 'user settings preserved exactly' -Encoding Unicode
Set-Content -LiteralPath (Join-Path $mod 'local-note.txt') -Value 'keep extra files'
Set-Content -LiteralPath (Join-Path $game 'persistent-sentinel.sfs') -Value 'save progress'
$settingsHash=(Get-FileHash -LiteralPath (Join-Path $mod 'Settings.xml')).Hash
$helper=Join-Path $build 'package\GameData\RailgunLogistics\Updater\ApplyUpdate.exe'
$job=Join-Path $lab 'job-good'; New-Item -ItemType Directory -Path $job | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'friends.xml') -Destination (Join-Path $job 'release.xml')
Copy-Item -LiteralPath (Join-Path $build "RailgunLogistics-Client-$BundleVersion.zip") -Destination (Join-Path $job 'client.zip')
$fake=Start-Process -FilePath (Join-Path $game 'KSP_x64.exe') -ArgumentList '/c ping.exe -n 5 127.0.0.1 >nul' -WindowStyle Hidden -PassThru
$apply=Start-Process -FilePath $helper -ArgumentList @(('"'+$game+'"'),('"'+$job+'"')) -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 1
if(Test-Path -LiteralPath (Join-Path $mod 'InstalledRelease.xml')) { throw 'Updated while matching game process was running.' }
if((Get-Content -LiteralPath (Join-Path $job 'result.txt') -Raw) -notmatch 'Waiting') { throw 'Helper did not wait for the game.' }
$apply.WaitForExit(15000) | Out-Null
if(!$apply.HasExited -or $apply.ExitCode -ne 0) { throw 'Helper failed to apply after game exit.' }
if((Get-FileHash -LiteralPath (Join-Path $mod 'Settings.xml')).Hash -ne $settingsHash) { throw 'Local settings changed.' }
if((Get-Content -LiteralPath (Join-Path $mod 'local-note.txt') -Raw).Trim() -ne 'keep extra files') { throw 'Extra files lost.' }
if((Get-Content -LiteralPath (Join-Path $game 'persistent-sentinel.sfs') -Raw).Trim() -ne 'save progress') { throw 'Unrelated save changed.' }
$backup=Get-ChildItem -LiteralPath (Join-Path $game 'RailgunLogistics-Backups') -Directory | Select-Object -First 1
if((Get-FileHash -LiteralPath (Join-Path $backup.FullName 'Settings.xml')).Hash -ne $settingsHash) { throw 'Backup missing original settings.' }
$before=(Get-FileHash -LiteralPath (Join-Path $mod 'Plugins\RailgunLogistics.dll')).Hash
$jobBad=Join-Path $lab 'job-corrupt'; New-Item -ItemType Directory -Path $jobBad | Out-Null
Copy-Item -LiteralPath (Join-Path $job 'release.xml') -Destination $jobBad
Set-Content -LiteralPath (Join-Path $jobBad 'client.zip') -Value 'broken'
$bad=Start-Process -FilePath $helper -ArgumentList @(('"'+$game+'"'),('"'+$jobBad+'"')) -WindowStyle Hidden -PassThru -Wait
if($bad.ExitCode -ne 1) { throw 'Corrupt package was not rejected.' }
if((Get-FileHash -LiteralPath (Join-Path $mod 'Plugins\RailgunLogistics.dll')).Hash -ne $before) { throw 'Corrupt update changed installed DLL.' }
# Exercise the friend-facing first-install script against the fixture as well.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $build 'package\Install-Client.ps1') -KspPath $game
# The minimal fixture has no ModuleManager: refusal is expected and must leave the current mod intact.
if($LASTEXITCODE -eq 0) { throw 'Installer accepted missing dependency.' }
Set-Content -LiteralPath (Join-Path $game 'GameData\ModuleManager.fixture.dll') -Value 'fixture dependency marker'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $build 'package\Install-Client.ps1') -KspPath $game
if($LASTEXITCODE -ne 0) { throw 'First-install script failed.' }
if((Get-FileHash -LiteralPath (Join-Path $mod 'Settings.xml')).Hash -ne $settingsHash) { throw 'Installer changed local settings bytes.' }
Write-Output "PASS: waits for exact game, applies on exit, verifies bytes, preserves settings/extras/save sentinel, backs up, rejects corrupt download and missing dependency. Evidence: $lab"
