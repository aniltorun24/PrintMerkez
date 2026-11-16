$ErrorActionPreference = 'Stop'
$stateRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/PrintMerkez.Web/App_Data/background'
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $stateRoot 'paused.flag'), [DateTime]::UtcNow.ToString('o'))
$statusFile = Join-Path $stateRoot 'status.json'
for ($i=0; $i -lt 20; $i++) {
    if (!(Test-Path -LiteralPath $statusFile)) { break }
    $state = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
    if ($state.Phase -eq 'Paused') { break }
    if (!(Get-Process -Id $state.SupervisorId -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Seconds 1
}
Write-Output 'PrintMerkez background paused. Use start-background.ps1 to resume.'
