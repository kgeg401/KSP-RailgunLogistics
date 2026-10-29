$ErrorActionPreference = 'Stop'
& dotnet build (Join-Path $PSScriptRoot 'RailgunSandbox.csproj') -c Release --nologo -p:NuGetAudit=false --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Sandbox client compilation failed' }
$checkOutput = & dotnet run --project (Join-Path $PSScriptRoot 'tests\SandboxTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Reusable fleet checks failed' }
$checkOutput | Write-Output
if (($checkOutput -join "`n") -notmatch 'PASS (\d+) ') { throw 'Missing successful test count.' }
$checks = [int]$Matches[1]
$sharedOutput = & dotnet run --project (Join-Path $PSScriptRoot 'tests\SharedFleetTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Shared fleet authority checks failed' }
$sharedOutput | Write-Output
if (($sharedOutput -join "`n") -notmatch 'PASS (\d+) shared-fleet') { throw 'Missing shared fleet check result.' }
$sharedChecks = [int]$Matches[1]
$stage = Join-Path $PSScriptRoot 'staged\GameData\RailgunSandbox'
New-Item -ItemType Directory -Path (Join-Path $stage 'Plugins') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameData\RailgunSandbox\ReusableParts.cfg') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\net472\RailgunSandbox.dll') -Destination (Join-Path $stage 'Plugins') -Force
$inputs = @('FlightFleet.cs','SharedFleet.cs','NetworkFleet.cs','MultiplayerValidation.cs','DeliveryEta.cs','NativeDeliveryEta.cs','TerminalPadGuidance.cs','LoadingSequence.cs','LoadingMechanism.cs','GantrySafety.cs','ShellLoadingRoute.cs','ShellCarrier.cs','LoaderAssets.cs','InspectionTargetFollower.cs','RailMesh.cs','RailAssets.cs','RailInspection.cs','LaunchShellPolicy.cs','LaunchShell.cs','RailLaunch.cs','DockingHardware.cs','DockingPortPolicy.cs','RailgunDeliveryPort.cs','AeroMenu.cs','ReentryEnvelope.cs','RecoveryGuidance.cs','StockPods.cs','DepotModel.cs','SandboxFlight.cs','DepotInspection.cs','RailgunSandbox.csproj','GameData\RailgunSandbox\ReusableParts.cfg') |
    ForEach-Object { [pscustomobject]@{ Path=$_; SHA256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash } }
[pscustomobject]@{ Inputs=$inputs; DllSHA256=(Get-FileHash -LiteralPath (Join-Path $stage 'Plugins\RailgunSandbox.dll') -Algorithm SHA256).Hash; Checks=$checks; SharedChecks=$sharedChecks } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'build-stamp.json') -Encoding utf8
Write-Output "Standalone reusable-fleet test build staged: $stage. The friends release integrates this code into RailgunLogistics.dll."
