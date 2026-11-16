$projectRoot = Split-Path -Parent $PSScriptRoot
$stateRoot = Join-Path $projectRoot 'src/PrintMerkez.Web/App_Data/background'
$statusFile = Join-Path $stateRoot 'status.json'
if (Test-Path -LiteralPath $statusFile) { Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json | Format-List }
else { Write-Output 'No background runtime status found.' }
try { Invoke-RestMethod 'http://127.0.0.1:5180/health' -TimeoutSec 5 | Format-List }
catch { Write-Output 'Panel is not responding.' }
Write-Output ('Logs: '+(Join-Path $stateRoot 'logs'))
