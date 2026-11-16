param([int]$PrinterId = 1, [ValidateSet('Unknown','Monochrome','Color')][string]$Color = 'Unknown')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$keyPath = Join-Path $projectRoot '.dev-agent-key'
if (!(Test-Path -LiteralPath $keyPath)) { throw 'Önce start-web.ps1 ile paneli başlatın.' }
$previousKey = $env:Agent__ApiKey
try {
    $env:Agent__ApiKey = [IO.File]::ReadAllText($keyPath).Trim()
    dotnet run --no-launch-profile --project (Join-Path $projectRoot 'src/PrintMerkez.Agent') -- --Agent:Simulate=true --Agent:ApiUrl=http://127.0.0.1:5180 --Agent:PrinterId=$PrinterId --Agent:Color=$Color
} finally { $env:Agent__ApiKey = $previousKey }