$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$webRoot = Join-Path $projectRoot 'src/PrintMerkez.Web'
$stateRoot = Join-Path $webRoot 'App_Data/background'
$logRoot = Join-Path $stateRoot 'logs'
$stopFile = Join-Path $stateRoot 'paused.flag'
$statusFile = Join-Path $stateRoot 'status.json'
$appHost = Join-Path $projectRoot 'runtime/local/PrintMerkez.Web.exe'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
try { $supervisorLock = [IO.File]::Open((Join-Path $stateRoot 'supervisor.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
catch [IO.IOException] { exit 0 }
$appProcess = $null
$restarts = 0
function Save-State([string]$phase) {
    $state = [ordered]@{ Phase=$phase; SupervisorId=$PID; ApplicationId=$(if($appProcess -and !$appProcess.HasExited){$appProcess.Id}else{$null});
        UpdatedAtUtc=[DateTime]::UtcNow.ToString('o'); Restarts=$restarts; Url='http://127.0.0.1:5180'; AppHost=$appHost }
    [IO.File]::WriteAllText(($statusFile+'.tmp'), ($state | ConvertTo-Json -Compress))
    Move-Item -LiteralPath ($statusFile+'.tmp') -Destination $statusFile -Force
}
function Log-Supervisor([string]$message) {
    [IO.File]::AppendAllText((Join-Path $logRoot 'supervisor.log'), ([DateTime]::UtcNow.ToString('o')+' '+$message+[Environment]::NewLine))
}
try {
    if (!(Test-Path -LiteralPath $appHost)) { throw 'Published app not found. Run install-background.ps1.' }
    if (!(Test-Path -LiteralPath $stopFile) -and (Test-Path -LiteralPath $statusFile)) {
        $previous = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
        if ($previous.ApplicationId -and $previous.SupervisorId -and !(Get-Process -Id $previous.SupervisorId -ErrorAction SilentlyContinue)) {
            $orphan = Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$previous.ApplicationId) -ErrorAction SilentlyContinue
            if ($orphan -and $orphan.ExecutablePath -eq $appHost -and $orphan.ParentProcessId -eq $previous.SupervisorId) {
                Log-Supervisor ('Recovering owned orphan app PID '+$orphan.ProcessId)
                Stop-Process -Id $orphan.ProcessId -Force
            }
        }
    }
    $keyFile = Join-Path $projectRoot '.dev-agent-key'
    if (!(Test-Path -LiteralPath $keyFile)) {
        $keyBytes = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($keyBytes) } finally { $rng.Dispose() }
        [IO.File]::WriteAllText($keyFile, [Convert]::ToBase64String($keyBytes))
    }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:Agent__ApiKey = [IO.File]::ReadAllText($keyFile).Trim()
    $env:Background__StopFile = $stopFile
    while (!(Test-Path -LiteralPath $stopFile)) {
        try {
            $runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
            $databasePath = Join-Path $webRoot 'App_Data/printmerkez.db'
            $arguments = @('--contentRoot', ('"'+$webRoot+'"'), '--urls', 'http://127.0.0.1:5180', '--Mail:Mode=Preview',
                '--ConnectionStrings:PrintDb', ('"Data Source='+$databasePath+'"'))
            $appProcess = Start-Process -FilePath $appHost -ArgumentList $arguments -WorkingDirectory $webRoot -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $logRoot ('app-'+$runId+'.log')) `
                -RedirectStandardError (Join-Path $logRoot ('error-'+$runId+'.log')) -PassThru
            Log-Supervisor ('Started app PID '+$appProcess.Id)
            Save-State 'Running'
            $healthTicks = 0
            $healthFailures = 0
            while (!$appProcess.HasExited) {
                if (Test-Path -LiteralPath $stopFile) {
                    if (!$appProcess.WaitForExit(15000)) { $appProcess.Kill(); $appProcess.WaitForExit() }
                    break
                }
                Start-Sleep -Seconds 1
                $appProcess.Refresh()
                $healthTicks++
                if ($healthTicks -ge 15 -and !$appProcess.HasExited) {
                    $healthTicks = 0
                    try { Invoke-RestMethod 'http://127.0.0.1:5180/health' -TimeoutSec 3 | Out-Null; $healthFailures = 0 }
                    catch { $healthFailures++ }
                    if ($healthFailures -ge 3) { Log-Supervisor 'App health did not respond three times; restarting.'; $appProcess.Kill();$appProcess.WaitForExit() }
                }
            }
            Log-Supervisor ('App exited with code '+$appProcess.ExitCode)
            $appProcess.Dispose()
            $appProcess = $null
        } catch {
            Log-Supervisor ('Supervisor error type '+$_.Exception.GetType().Name)
            if ($appProcess -and !$appProcess.HasExited) { $appProcess.Kill(); $appProcess.WaitForExit() }
        }
        if (Test-Path -LiteralPath $stopFile) { break }
        $restarts++
        Save-State 'Restarting'
        Start-Sleep -Seconds 5
    }
    Save-State 'Paused'
} finally {
    if ($appProcess -and !$appProcess.HasExited) { $appProcess.Kill(); $appProcess.WaitForExit() }
    $supervisorLock.Dispose()
}
