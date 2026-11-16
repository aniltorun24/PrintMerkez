$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$stateRoot = Join-Path $projectRoot 'src/PrintMerkez.Web/App_Data/background'
$stopFile = Join-Path $stateRoot 'paused.flag'
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
$shellPath = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$supervisor = Join-Path $PSScriptRoot 'background-supervisor.ps1'
$installFile = Join-Path $stateRoot 'install.json'
if (Test-Path -LiteralPath $installFile) {
    $installation = Get-Content -LiteralPath $installFile -Raw | ConvertFrom-Json
    if ($installation.Mode -eq 'ScheduledTask') {
        $task = Get-ScheduledTask -TaskName $installation.TaskName
        if ($task.State -eq 'Running' -and !(Test-Path -LiteralPath $stopFile)) {
            Write-Output 'PrintMerkez background task is already running.'
            exit 0
        }
        for ($i=0; $task.State -eq 'Running' -and $i -lt 10; $i++) {
            Start-Sleep -Seconds 1
            $task = Get-ScheduledTask -TaskName $installation.TaskName
        }
        if ($task.State -eq 'Running') { throw 'Previous background task is still stopping.' }
        if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }
        Start-ScheduledTask -TaskName $installation.TaskName
        Write-Output 'PrintMerkez background task started. Panel: http://127.0.0.1:5180'
        exit 0
    }
}
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }
Start-Process -FilePath $shellPath -ArgumentList @('-NoLogo','-NoProfile','-NonInteractive','-WindowStyle','Hidden','-ExecutionPolicy','RemoteSigned','-File',('"'+$supervisor+'"')) -WindowStyle Hidden | Out-Null
Write-Output 'PrintMerkez background start requested. Panel: http://127.0.0.1:5180'
