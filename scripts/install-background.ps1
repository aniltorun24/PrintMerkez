$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$appProject = Join-Path $projectRoot 'src/PrintMerkez.Web/PrintMerkez.Web.csproj'
$publishPath = Join-Path $projectRoot 'runtime/local'
$stateRoot = Join-Path $projectRoot 'src/PrintMerkez.Web/App_Data/background'
& (Join-Path $PSScriptRoot 'stop-background.ps1')
dotnet publish $appProject -c Release -r win-x64 --self-contained false -o $publishPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed; the previous background process remains paused.' }
$shellPath = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$supervisor = Join-Path $PSScriptRoot 'background-supervisor.ps1'
$arguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File "'+$supervisor+'"'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$taskName = 'PrintMerkez-Local-'+$identity.User.Value.Split('-')[-1]
$mode = 'StartupShortcut'
try {
    $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existing -and !($existing.Actions | Where-Object {$_.Arguments -like ('*'+$supervisor+'*')})) {
        throw 'Existing task does not belong to this installation.'
    }
    $action = New-ScheduledTaskAction -Execute $shellPath -Argument $arguments -WorkingDirectory $projectRoot
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
    $recheck = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
    $principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -Hidden
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($trigger,$recheck) -Principal $principal -Settings $settings -Force | Out-Null
    $mode = 'ScheduledTask'
} catch {
    Write-Output ('Task registration unavailable ('+$_.Exception.GetType().Name+'); using the current user Startup folder.')
}
$startupPath = [Environment]::GetFolderPath('Startup')
New-Item -ItemType Directory -Path $startupPath -Force | Out-Null
$shortcutPath = Join-Path $startupPath 'PrintMerkez.lnk'
if ($mode -eq 'StartupShortcut') {
    $wsh = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $shellPath
    $shortcut.Arguments = $arguments
    $shortcut.WorkingDirectory = $projectRoot
    $shortcut.WindowStyle = 7
    $shortcut.Description = 'PrintMerkez local development background host'
    $shortcut.Save()
    Write-Output ('Startup shortcut installed: '+$shortcutPath)
}
[IO.File]::WriteAllText((Join-Path $stateRoot 'install.json'), (@{Mode=$mode;TaskName=$taskName;InstalledAtUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Compress))
& (Join-Path $PSScriptRoot 'start-background.ps1')
