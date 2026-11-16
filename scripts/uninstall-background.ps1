$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'stop-background.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$installFile = Join-Path $projectRoot 'src/PrintMerkez.Web/App_Data/background/install.json'
$supervisor = Join-Path $PSScriptRoot 'background-supervisor.ps1'
if (Test-Path -LiteralPath $installFile) {
    $installation = Get-Content -LiteralPath $installFile -Raw | ConvertFrom-Json
    if ($installation.Mode -eq 'ScheduledTask') {
        $task = Get-ScheduledTask -TaskName $installation.TaskName -ErrorAction SilentlyContinue
        if ($task -and !($task.Actions | Where-Object {$_.Arguments -like ('*'+$supervisor+'*')})) { throw 'Task ownership check failed.' }
        if ($task) { Unregister-ScheduledTask -TaskName $installation.TaskName -Confirm:$false }
    }
    Remove-Item -LiteralPath $installFile
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'PrintMerkez.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    if ($shortcut.Arguments -like ('*'+$supervisor+'*')) { Remove-Item -LiteralPath $shortcutPath }
}
Write-Output 'Automatic startup removed. Database, application files and logs were preserved.'
