# Başka bilgisayarda projeyi açma

GitHub kaynak kodu, migration'lar, kullanım notları ve otomatik testleri içerir.
Gerçek veritabanı, API anahtarı, çalışma logları, yayımlanmış uygulama ve Windows görev kaydı GitHub'a gönderilmez.

## Kaynak kodu

.NET 10 SDK ve Git yüklü bilgisayarda:

```powershell
git clone https://github.com/aniltorun24/PrintMerkez.git
cd PrintMerkez
dotnet restore PrintMerkez.sln
dotnet test PrintMerkez.sln
```

Visual Studio ile `PrintMerkez.sln` açılır. Yalnızca kodun klonlanması mevcut baskı geçmişini ve tarifeleri getirmez.
Kolektör ayarındaki envanter kimliği bu veritabanındaki yazıcıya bağlıdır; boş veritabanında aynı kimliğin var olduğu varsayılmaz.

## Mevcut verilerle devam etme

1. Eski bilgisayarda `./scripts/stop-background.ps1` ile aktarımı duraklatın. İki bilgisayarda birbirinden bağımsız
   veritabanlarıyla eşzamanlı veri toplamayı başlatmayın.
2. Yerel taşıma yedeğindeki `printmerkez.db` dosyasını, yeni bilgisayardaki
   `src/PrintMerkez.Web/App_Data/printmerkez.db` konumuna uygulama kapalıyken yerleştirin.
   Yeni bilgisayarda daha önce veri oluştuysa onu ayrıca yedekleyin; doğrudan üstüne yazmayın.
3. Print server ve yazıcıya şirket ağından erişilebildiğini doğrulayın. Yeni Windows oturumunun da
   sunucu olay günlüklerini okuma yetkisi bulunmalıdır.
4. `./scripts/start-web.ps1` ile başlatın ve `http://127.0.0.1:5180` adresini kontrol edin.
   Eksik API anahtarı yeni bilgisayarda otomatik oluşturulur; eski anahtarı paylaşmanız gerekmez.
5. Arka plan çalışması için `./scripts/install-background.ps1` çalıştırın. Windows görevi yeni bilgisayarda yeniden
   oluşturulur; Git'ten kendiliğinden gelmez. Visual Studio ile F5 kullanmadan önce arka planı durdurun.

Taşıma yedeği yalnızca SQLite backup API ile alınmış tutarlı veritabanını içerir. Çalışan veritabanının yalnızca
`.db` dosyasını kopyalamak WAL içindeki son değişiklikleri kaçırabilir. Yedek anından sonra oluşan veriler,
sunucu/cihaz geçmişinde hâlâ tutuluyorsa tekrar okunabilir. Silinmiş cihaz geçmişi yeniden üretilemez.
Makineye bağlı devam dosyaları ve süreç kimlikleri taşınmaz; var olan olay kimlikleri tekrar sayımı önler.
Yedek kurum içi kullanıcı ve baskı bilgileri içerebilir; GitHub'a eklenmemelidir.

## Yalnızca temiz kod incelemesi

Veritabanını taşımadan çalıştıracaksanız toplayıcıları o PowerShell oturumu için kapatın:

```powershell
$env:WindowsCollector__Enabled = 'false'
$env:DeviceCollector__Enabled = 'false'
./scripts/start-web.ps1
```

Bu durumda boş yerel veritabanı migration'larla oluşur; gerçek kayıtlar kendiliğinden taşınmış olmaz.
