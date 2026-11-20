[CmdletBinding()]
param(
    [string]$Root = 'C:\PrintMerkez',
    [Security.SecureString]$AdminPassword,
    [switch]$UseTestPassword
)
$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath($Root).TrimEnd('\')
if ($Root -eq [IO.Path]::GetPathRoot($Root).TrimEnd('\') -or $Root.StartsWith('\\')) { throw 'Yerel diskte uygulamaya ait bir klasör seçin.' }
if ($Root -eq [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')) { throw 'Paketi kurulum hedefinden ayrı bir klasöre açın.' }
$dataPath = Join-Path $Root 'Data'
$webPath = Join-Path $Root 'Web'
$collectorPath = Join-Path $Root 'Collector'
$logPath = Join-Path $Root 'Logs'
foreach ($targetPath in @($dataPath,$webPath,$collectorPath,$logPath)) {
    if (![IO.Path]::GetFullPath($targetPath).StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Hedef klasör doğrulanamadı.' }
}
if ((Test-Path -LiteralPath (Join-Path $dataPath 'printmerkez.db')) -or
    (Test-Path -LiteralPath (Join-Path $dataPath 'server-settings.json')) -or
    (Test-Path -LiteralPath (Join-Path $webPath 'PrintMerkez.Web.dll')) -or
    (Test-Path -LiteralPath (Join-Path $collectorPath 'PrintMerkez.Web.dll'))) {
    throw 'Mevcut kurulum bulundu. Bu betik yalnızca ilk kurulum içindir; dosyaların üzerine yazılmadı.'
}
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'DataSeed\printmerkez.db'))) { throw 'Veri yedeği paket içinde bulunamadı.' }
if ($UseTestPassword) {
    $AdminPassword = ConvertTo-SecureString '12345' -AsPlainText -Force
}
elseif (!$AdminPassword) {
    $AdminPassword = Read-Host 'Yeni sunucu yönetici şifresi (en az 12 karakter)' -AsSecureString
    $confirmation = Read-Host 'Yeni şifreyi tekrar girin' -AsSecureString
}
function Unwrap([Security.SecureString]$value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($value)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
$plainPassword = Unwrap $AdminPassword
try {
    if (!$UseTestPassword -and $plainPassword.Length -lt 12) { throw 'Şifre en az 12 karakter olmalı.' }
    if ($confirmation -and $plainPassword -cne (Unwrap $confirmation)) { throw 'Şifreler eşleşmiyor.' }
    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($salt) } finally { $rng.Dispose() }
    $derive = [Security.Cryptography.Rfc2898DeriveBytes]::new($plainPassword,$salt,210000,[Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $passwordHash = [Convert]::ToBase64String($derive.GetBytes(32)) } finally { $derive.Dispose() }
} finally { $plainPassword = $null }
foreach ($directory in @($webPath,$collectorPath,$dataPath,$logPath)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
foreach ($part in @('Web','Collector')) {
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $part) | Copy-Item -Destination (Join-Path $Root $part) -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DataSeed\printmerkez.db') -Destination (Join-Path $dataPath 'printmerkez.db')
$connection = 'Data Source='+(Join-Path $dataPath 'printmerkez.db')+';Default Timeout=30;Pooling=False'
$settings = @{
    Deployment = @{Mode='Server';DataDirectory=$dataPath;LogDirectory=$logPath;AllowRemote=$false;AllowedNetworks=@();AllowTestPassword=[bool]$UseTestPassword}
    ConnectionStrings = @{PrintDb=$connection}
    LocalAdmin = @{Username='admin';PasswordSalt=[Convert]::ToBase64String($salt);PasswordHash=$passwordHash}
    Development = @{SeedDemoData=$false;SeedScenarioData=$false}
    Mail = @{Mode='Preview'}
    Notifications = @{WorkerEnabled=$true}
    Agent = @{ApiKey='';AllowSimulation=$false}
    WindowsCollector = @{Enabled=$true;Server='Moi-PrintSrv.trnavm.com';AllowedEventServers=@('Moi-PrinSrv.trnavm.com');Queue='Moi İşletme Printer';PrinterId=24;PollSeconds=10;CursorDirectory=$dataPath}
    DeviceCollector = @{Enabled=$true;Url='https://10.60.23.4/';PrinterId=24;CertificateSha256='DCF3733F8FE75E7CDBE522304290299872F74E3F5440BE534791E66E9998E82E';MaxPages=3}
}
$settingsFile = Join-Path $dataPath 'server-settings.json'
$settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $settingsFile -Encoding UTF8
foreach ($role in @('Web','Collector')) {
    $profile = @{Deployment=@{Mode='Server';Role=$role;SettingsFile=$settingsFile};Development=@{SeedDemoData=$false;SeedScenarioData=$false};Logging=@{LogLevel=@{Default='Information';'Microsoft.AspNetCore'='Warning';'Microsoft.EntityFrameworkCore.Database.Command'='Warning'}}}
    if ($role -eq 'Collector') { $profile['Urls']='http://127.0.0.1:5181' }
    $profile | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path (Join-Path $Root $role) 'appsettings.Production.json') -Encoding UTF8
}
$sourceCursor = Join-Path $PSScriptRoot 'DataSeed\source-cursor.json'
if (Test-Path -LiteralPath $sourceCursor) {
    # Match .NET ToUpperInvariant exactly, without relying on PowerShell's current culture.
    $identity = $connection+'|'+$settings.WindowsCollector.Server.ToUpperInvariant()+'|'+$settings.WindowsCollector.Queue.ToUpperInvariant()+'|24'
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity)))).Replace('-','').ToLowerInvariant().Substring(0,24) }
    finally { $sha.Dispose() }
    Copy-Item -LiteralPath $sourceCursor -Destination (Join-Path $dataPath ('windows-cursor-'+$hash+'.json'))
}
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    & (Join-Path $webPath 'PrintMerkez.Web.exe') --environment Production --contentRoot $webPath --Maintenance:InitializeDatabase true
    if ($LASTEXITCODE -ne 0) { throw 'Veritabanı hazırlığı başarısız. IIS ve toplayıcı başlatılmadı.' }
} finally { $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment }
Write-Output 'Dosyalar ve veritabanı hazır. IIS sitesi ve Windows Servisi henüz oluşturulmadı.'
Write-Output 'Sonraki adım: IIS uygulama havuzu, klasör izinleri ve yalnızca yerel HTTP test sitesi.'
