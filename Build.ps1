$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
foreach ($projectFile in @('src\Client\RailgunLogistics.csproj','src\Server\RailgunLogistics.Server.csproj')) {
    & dotnet build (Join-Path $projectRoot $projectFile) -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $projectFile" }
}
& dotnet run --project (Join-Path $projectRoot 'tests\CoreTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Logistics verification failed' }
$releaseRoot = Join-Path $projectRoot 'release'
$clientFolder = Join-Path $releaseRoot 'GameData\RailgunLogistics'
$serverFolder = Join-Path $releaseRoot 'Server\Plugins'
New-Item -ItemType Directory -Path $clientFolder,$serverFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'GameData\RailgunLogistics\Receiver.cfg') -Destination $clientFolder -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'GameData\RailgunLogistics\Settings.xml') -Destination $clientFolder -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'GameData\RailgunLogistics\RailgunLogistics.restockwhitelist') -Destination $clientFolder -Force
New-Item -ItemType Directory -Path (Join-Path $clientFolder 'Parts'),(Join-Path $clientFolder 'Plugins') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'GameData\RailgunLogistics\Parts\KerbinRailgun.cfg') -Destination (Join-Path $clientFolder 'Parts') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'GameData\RailgunLogistics\Parts\CargoPod.cfg') -Destination (Join-Path $clientFolder 'Parts') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'sandbox\GameData\RailgunSandbox\ReusableParts.cfg') -Destination (Join-Path $clientFolder 'Parts') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Client\bin\Release\net472\RailgunLogistics.dll') -Destination (Join-Path $clientFolder 'Plugins') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Server\bin\Release\net6.0\RailgunLogistics.Server.dll') -Destination $serverFolder -Force
foreach ($document in @('README.md','Install.ps1','LICENSE','CHANGELOG.md')) {
    if (Test-Path -LiteralPath (Join-Path $projectRoot $document)) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $releaseRoot -Force
    }
}
Get-ChildItem -LiteralPath $releaseRoot -Recurse -File |
    ForEach-Object { [PSCustomObject]@{ Path=$_.FullName.Substring($releaseRoot.Length+1); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'release-manifest.json') -Encoding utf8
Write-Output "Package staged in $releaseRoot"
