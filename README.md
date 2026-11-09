# PrintMerkez

11 lokasyonlu kurum için merkezi yazıcı yönetimi uygulaması. İlk geliştirme adımı: sunucu kurulumu beklenmeden çalışan Razor Pages paneli, kalıcı envanter ve olay kabul API'si.

## Başlatma

Gereksinim: .NET 10 SDK. Visual Studio kullanılıyorsa .NET 10 destekleyen sürüm ve ASP.NET geliştirme bileşenleri.

Proje dizininde PowerShell açın:

~~~powershell
./scripts/start-web.ps1
~~~

Panel: http://127.0.0.1:5180

Script yalnızca mevcut işlem için geliştirme ortamını ve API anahtarını ayarlar. Anahtar proje kökündeki Git dışında tutulan .dev-agent-key dosyasında oluşur; paylaşmayın. Yönetim paneli bu aşamada sadece yerel bilgisayardan erişilebilir. Production ortamında uygulama bilinçli olarak başlamaz; AD ve rol kontrolü sonraki aşamada eklenecek.

Visual Studio ile açılacak dosya: PrintMerkez.sln. Web başlangıç projesidir. Visual Studio'da doğrudan çalıştırıldığında panel açılır, fakat anahtar ayarlanmadan olay API'si 503 döndürür. Tam yerel akış için yukarıdaki scripti kullanın.

İsteğe bağlı, ikinci PowerShell penceresinde:

~~~powershell
./scripts/start-simulator.ps1 -PrinterId 1
~~~

Simülatör her 10 saniyede örnek baskı olayı gönderir. Gerçek yazıcıya bağlanmaz, fiziksel baskı başlatmaz ve Windows olaylarını okumaz. Başarısız aktarımda aynı olay kimliğiyle tekrar dener; bellekteki bekleyen kayıt süreç kapanınca kaybolur. Kalıcı kuyruk gerçek ajan aşamasında eklenecek. Durdurmak için Ctrl+C. Yeni kayıtları görmek için paneli yenileyin.

## Bu sürümde çalışanlar

- Türkçe ve küçük ekranlara uyumlu ASP.NET Core Razor Pages yönetim paneli.
- Lokasyon ekleme ve düzenleme; paylaşılan 11 mülk adı başlangıç verisi olarak yüklenir.
- Yazıcı ekleme/düzenleme, model, lokasyon, IP, kuyruk adı; IP ve mükerrer ad kontrolü.
- Olivetti d-COPIA 255MF KX örnek pilot envanter kaydı. Model metni kullanıcıdan alınmıştır; cihaz yeteneklerinin doğrulandığı anlamına gelmez. IP ve kuyruk uydurulmamıştır.
- SQLite üzerinde EF Core ile kalıcı kayıtlar; veri dosyası src/PrintMerkez.Web/App_Data/printmerkez.db.
- Kullanıcı/yazıcı/olay, lokasyon, kaynak ve Türkiye saatine göre tarih filtreleri; 25 kayıtlık sayfalama.
- Filtrelere uyan CSV dışa aktarımı; en fazla 5.000 kayıt ve elektronik tablo formül koruması.
- Anahtarlı baskı olayı API'si, doğrulama ve Server + EventId üzerinden tekilleştirme.
- Yerel servis simülatörü ve iş akışı testleri.

## Veri anlamı

Başlangıçta 12 simülasyon olayı yüklenir. Bunlar her satırda etiketlenir; panel toplamları simülasyonları içerir. Gerçek kayıtlar için geçmiş ekranındaki kaynak filtresini kullanın. Lokasyon adları paylaşılan mülk listesinden alınmıştır. Pilot yazıcı ilk lokasyona örnek olarak eşleştirilmiştir; gerçek cihaz konumu henüz doğrulanmamıştır.

Impressions: kopyalar dahil raporlanan toplam baskı yüzü. Fiziksel kağıt adedi değildir ve Copies ile tekrar çarpılmaz. Bilinmeyen adet, kopya ve renk bilgileri sıfır olarak kabul edilmez. QueueCompleted yalnızca kuyruk durumudur; fiziksel çıktı kanıtı değildir. Olay zamanı UTC saklanır, arayüz ve tarih filtreleri Europe/Istanbul kullanır. Yazıcı taşındığında baskı kaydının eski lokasyonu korunur. Belge içeriği ve belge başlığı toplanmaz.

Development:SeedDemoData=false ayarı (PowerShell'de $env:Development__SeedDemoData='false') yeni boş veritabanında örnek veri yüklenmesini kapatır. Var olan örnek kayıtları silmez. Temiz deneme için farklı bir SQLite yolu ConnectionStrings:PrintDb ayarıyla verilebilir. Başlangıç verileri boş lokasyon tablosuna eklenir. Önceki sürümdeki değiştirilmemiş Lokasyon 01–11 kayıtları yeni adlarla güncellenir; kimlikleri ve ilişkileri korunur. Kullanıcının düzenlediği lokasyon adları değiştirilmez.

Bu ilk sürümde EnsureCreated kullanılır; şema yükseltme/migration uygulanmaz. Gerçek pilot verisi toplanmadan önce veritabanı seçimi ve EF migration geçişi tamamlanmalıdır. SQLite yerel geliştirme içindir; 11 lokasyonun üretim veritabanı kararı değildir.

## Çözüm yapısı

- src/PrintMerkez.Core: veri modelleri, olay sözleşmesi ve etiketler.
- src/PrintMerkez.Web: Razor Pages, olay kabul API'si, EF Core ve yerel SQLite.
- src/PrintMerkez.Agent: açıkça etkinleştirilen olay simülatörü; Windows ajanının başlangıç projesi.
- tests/PrintMerkez.Tests: gerçek HTTP/form ve SQLite iş akışı testleri.

UI ve API aynı ASP.NET Core uygulamasındadır. Razor sayfaları veritabanını sunucu tarafında kullanır; kendi API'sine HTTP çağrısı yapmaz. Ajan API'ye olay gönderir. Worker henüz Windows Service olarak kurulmaz.

## Olay API'si

POST /api/v1/print-events

Header: X-Agent-Key: yerel anahtar

~~~json
{
  "eventId": "unique-record-id",
  "server": "SRV-PRINT",
  "printerId": 1,
  "account": "DOMAIN\\user",
  "occurredAt": "2026-11-06T14:00:00+03:00",
  "impressions": null,
  "copies": null,
  "color": "Unknown",
  "status": "QueueCompleted",
  "source": "Simulation"
}
~~~

PrinterId kayıtlı envanter kimliğidir. Server normalleştirilir. EventId, sunucu içinde olayın benzersiz kimliği olmalı; Windows olay tipi numarası tek başına kullanılmamalı. Aynı sunucu ve kimlik tekrar gelirse ilk kayıt korunur, güncelleme yapılmaz. Kimlik üretiminde gerçek toplayıcının kanal, record id ve günlük sıfırlanmasını ele alması gerekecek.

Yanıtlar: 201 yeni kayıt; 200 tekrar; 400 geçersiz veri; 401 anahtar hatalı/eksik; 503 sunucuda anahtar tanımlanmamış. GET /health yerel uygulama durumunu döndürür. API anahtarı en az 32 karakter olmalıdır. HTTP yalnızca yerel geliştirme içindir; ağ erişiminde TLS ve ajan kimliği ayrıca tasarlanacak.

## Doğrulama

~~~powershell
dotnet build PrintMerkez.sln
dotnet test PrintMerkez.sln
~~~

Testler birbirinden ayrı geçici SQLite dosyaları kullanır; geliştirme envanterini değiştirmez.

## Sıradaki işler

1. Windows PrintService olaylarını örnek XML üzerinden okuyacak ayrıştırıcı ve testler; gerçek sunucuda alan doğrulaması.
2. Yerel kalıcı ajan tamponu, yeniden başlatma sonrası devam ve bağlantı kopması testleri.
3. AD hesabı/departman eşleştirme, yönetici/rapor rolleri ve oturum açma.
4. Birim maliyet tarifeleri ve bilinmeyen metadata için hesaplama politikası.
5. Baskı öncesi denetim için ayrı teknik pilot; yetki ve kota rezervasyonu/sonlandırma.
6. Lokasyon bazlı sunucu/ajan yapılandırması, merkezi veritabanı ve migration.
7. Toner/sayaç, PIN/kart, beklet-bırak, fotokopi/tarama için model ve üretici API/SDK doğrulaması.

Sunucu kurulumu altyapı ekibinin sorumluluğundadır. Sunucu açıldığında ilk entegrasyon: SRV-PRINT üzerindeki Olivetti kuyruğundan bir test olayını API'ye aktarmak ve kullanıcı/zaman/adet alanlarını karşılaştırmak. Bu sürüm gerçek baskıyı izleme veya engelleme iddiasında bulunmaz.

## Teknik kaynaklar

- [Microsoft Razor Pages](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/index)
- [Microsoft Razor Pages ve EF Core](https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/intro?view=aspnetcore-10.0)
