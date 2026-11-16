param(
    [string]$Server = 'Moi-PrintSrv.trnavm.com',
    [string]$Queue = 'Moi İşletme Printer',
    [ValidateRange(1,1000)][int]$MaxEvents = 50
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
# Current Windows identity only. No stored passwords, printing, API writes, or server changes.
dotnet run --no-launch-profile --project (Join-Path $projectRoot 'src/PrintMerkez.Agent') -- `
    --Windows:ReadOnce=true "--Windows:Server=$Server" "--Windows:Queue=$Queue" "--Windows:MaxEvents=$MaxEvents"
if ($LASTEXITCODE -ne 0) { throw "Olay okuma kontrolü başarısız; çıkış kodu: $LASTEXITCODE" }
