param([string]$KspRoot = '')
$ErrorActionPreference = 'Stop'
$references = @()
if ($KspRoot) { $references += ('-p:KspRoot=' + (Resolve-Path -LiteralPath $KspRoot).Path) }
& dotnet build (Join-Path $PSScriptRoot 'RailgunSandbox.csproj') @references -c Release --nologo -p:NuGetAudit=false --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Sandbox client compilation failed' }
$checkOutput = & dotnet run --project (Join-Path $PSScriptRoot 'tests\SandboxTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Reusable fleet checks failed' }
$checkOutput | Write-Output
if (($checkOutput -join "`n") -notmatch 'PASS (\d+) ') { throw 'Missing successful test count.' }
$checks = [int]$Matches[1]
$stage = Join-Path $PSScriptRoot 'staged\GameData\RailgunSandbox'
New-Item -ItemType Directory -Path (Join-Path $stage 'Plugins') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameData\RailgunSandbox\ReusableParts.cfg') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\net472\RailgunSandbox.dll') -Destination (Join-Path $stage 'Plugins') -Force
$inputs = @('FlightFleet.cs','RailLaunch.cs','DockingHardware.cs','DockingPortPolicy.cs','RailgunDeliveryPort.cs','AeroMenu.cs','ReentryEnvelope.cs','RecoveryGuidance.cs','StockPods.cs','DepotModel.cs','SandboxFlight.cs','DepotInspection.cs','RailgunSandbox.csproj','GameData\RailgunSandbox\ReusableParts.cfg') |
    ForEach-Object { [pscustomobject]@{ Path=$_; SHA256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash } }
[pscustomobject]@{ Inputs=$inputs; DllSHA256=(Get-FileHash -LiteralPath (Join-Path $stage 'Plugins\RailgunSandbox.dll') -Algorithm SHA256).Hash; Checks=$checks } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'build-stamp.json') -Encoding utf8
Write-Output "Offline-only sandbox build staged: $stage"
