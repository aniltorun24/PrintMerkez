$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$keyPath = Join-Path $projectRoot '.dev-agent-key'
if (!(Test-Path -LiteralPath $keyPath)) {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [IO.File]::WriteAllText($keyPath, [Convert]::ToBase64String($bytes))
}
$previousKey = $env:Agent__ApiKey
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
try {
    $env:Agent__ApiKey = [IO.File]::ReadAllText($keyPath).Trim()
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    dotnet run --no-launch-profile --project (Join-Path $projectRoot 'src/PrintMerkez.Web') -- --urls http://127.0.0.1:5180
} finally {
    $env:Agent__ApiKey = $previousKey
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
}