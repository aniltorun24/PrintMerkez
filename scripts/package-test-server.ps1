[CmdletBinding()]
param([string]$Destination = [Environment]::GetFolderPath('Desktop'))
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$tag = 'PrintMerkez-TestSunucu-'+(Get-Date -Format 'yyyyMMdd-HHmmss')
$stage = Join-Path $projectRoot ('output\'+$tag)
$web = Join-Path $stage 'Web'
$collector = Join-Path $stage 'Collector'
$seed = Join-Path $stage 'DataSeed'
New-Item -ItemType Directory -Path $web,$collector,$seed -Force | Out-Null
dotnet publish (Join-Path $projectRoot 'src\PrintMerkez.Web\PrintMerkez.Web.csproj') -c Release -r win-x64 --self-contained false -o $web --nologo
if ($LASTEXITCODE -ne 0) { throw 'Sunucu yayın paketi derlenemedi.' }
# A single tested codebase, deployed as independent IIS and Windows Service processes.
Get-ChildItem -LiteralPath $web | Copy-Item -Destination $collector -Recurse -Force
foreach ($role in @('Web','Collector')) {
    $profile = @{Deployment=@{Mode='Server';Role=$role;SettingsFile='C:\PrintMerkez\Data\server-settings.json'};Development=@{SeedDemoData=$false;SeedScenarioData=$false}}
    if ($role -eq 'Collector') { $profile['Urls']='http://127.0.0.1:5181' }
    $profile | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path (Join-Path $stage $role) 'appsettings.Production.json') -Encoding UTF8
    $devFile = Join-Path (Join-Path $stage $role) 'appsettings.Development.json'
    if (Test-Path -LiteralPath $devFile) { Remove-Item -LiteralPath $devFile }
}
[xml]$webConfig = Get-Content -LiteralPath (Join-Path $web 'web.config') -Raw
$aspnet = $webConfig.configuration.location.'system.webServer'.aspNetCore
$environmentVariables = $webConfig.CreateElement('environmentVariables')
$variable = $webConfig.CreateElement('environmentVariable')
$variable.SetAttribute('name','ASPNETCORE_ENVIRONMENT'); $variable.SetAttribute('value','Production')
$environmentVariables.AppendChild($variable) | Out-Null
$dotnetVariable = $webConfig.CreateElement('environmentVariable')
$dotnetVariable.SetAttribute('name','DOTNET_ENVIRONMENT'); $dotnetVariable.SetAttribute('value','Production')
$environmentVariables.AppendChild($dotnetVariable) | Out-Null
$aspnet.AppendChild($environmentVariables) | Out-Null
$webConfig.Save((Join-Path $web 'web.config'))
$configureText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Configure-TestServer.ps1'),[Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $stage 'Configure-TestServer.ps1'),$configureText,[Text.UTF8Encoding]::new($true))
$oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT='Development'
    $localDatabase = Join-Path $projectRoot 'src\PrintMerkez.Web\App_Data\printmerkez.db'
    & (Join-Path $web 'PrintMerkez.Web.exe') --environment Development --contentRoot (Join-Path $projectRoot 'src\PrintMerkez.Web') --ConnectionStrings:PrintDb ('Data Source='+$localDatabase) --Maintenance:ExportDirectory $seed
    if ($LASTEXITCODE -ne 0) { throw 'Veri yedeği alınamadı.' }
} finally { $env:ASPNETCORE_ENVIRONMENT=$oldEnvironment }
@'
PRINTMERKEZ TEST SUNUCUSU PAKETI
1. ZIP dosyasini sunucuda C:\PrintMerkez\Kurulum klasorune acin.
2. Web, Collector, DataSeed ve Configure-TestServer.ps1 ayni klasorde gorunmeli.
3. Sonraki adimi birlikte uygulamadan dosyalari calistirmayin.

Web: IIS arayuzu. Collector: ayni uygulamanin sadece toplayici/bildirim rolu; ayri Windows Servisi.
DataSeed: gercek verinin tutarli yedegi ve veritabanindan ONCE alinan devam noktasi.
Sunucu sifresi pakette yok; Configure-TestServer.ps1 calistirildiginda secilecek.
Yerel test icin Configure-TestServer.ps1 -UseTestPassword komutu admin / 12345 ayarlar; sifre sorulmaz.
Test sifresi seciliyken baska bilgisayarlardan erisim kapali kalir.
Ilk kurulum yalnizca sunucunun kendisinden HTTP testi icindir. Ag erisimi varsayilan kapali.
Mevcut bilgisayarinizdaki toplayici calismaya devam eder. Sunucudaki toplayiciyi daha sonra kontrollu devralmada baslatacagiz.
Bu paket ilk test kurulumudur; SQL Server/AD/HTTPS ve yuk testi tamamlanmis uretim dagitimi degildir.
'@ | Set-Content -LiteralPath (Join-Path $stage 'OKU.txt') -Encoding UTF8
$manifest = Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    @{Path=$_.FullName.Substring($stage.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding UTF8
$zipPath = Join-Path $Destination ($tag+'.zip')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Output ('Yayin paketi: '+$zipPath)
