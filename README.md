# PrintMerkez

Başka bilgisayarda devam etmek için: [Klonlama, veri taşıma ve başlatma](docs/yeni-bilgisayarda-baslatma.md).

11 lokasyonlu kurum için merkezi yazıcı yönetimi uygulaması. Sunucu kurulumu beklenmeden çalışan Razor Pages paneli, kalıcı envanter, kullanıcı/departman dizini, tarihli maliyet tarifeleri, denetim geçmişi ve olay kabul API'si.

Güncel özellikler, veri yükseltme ve deneme akışı: [v0.2 kullanım notları](docs/v0.2-yonetim-ve-maliyet.md).

İlişkili otomatik test verileri: [Örnek veri paketi](docs/ornek-veri-paketi.md).

Aylık yönetici bildirimleri: [Kullanım, eşleştirme ve SMTP ayarları](docs/aylik-yonetici-bildirimleri.md).

Gerçek print server için salt okunur ilk kontrol: [Print server pilotu](docs/print-server-pilotu.md).

**12 Kasım güncellemesi:** [Gerçek baskı aktarımı](docs/gercek-baski-aktarimi.md) eklendi. Örnek veri üretimi kapalıdır;
simülasyon API'de varsayılan olarak reddedilir. Aşağıdaki eski simülasyon örnekleri yalnızca ayrı test yapılandırması içindir.

**13 Kasım güncellemesi:** Olivetti pilotunda [kopya ve toplamın cihaz iş kaydıyla doğrulanması](docs/cihaz-kopya-dogrulamasi.md)
eklendi. Windows belge sayfası ile kopyalar dahil toplam birbirinden ayrıdır.

Renk ve tarife entegrasyonu: [Gerçek baskı rengi ve maliyet hesabı](docs/baski-rengi-ve-maliyet.md).

**14 Kasım güncellemesi:** [Geliştirme bilgisayarında otomatik arka plan çalışması](docs/yerel-arka-plan.md)
kuruldu. Sunucu adı `Moi-PrintSrv` olarak güncellendi; eski olaylar ve mevcut yazıcı envanteri korundu.

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

Başlangıçta 12 simülasyon olayı yüklenir ve etiketlenir. Panel ve geçmiş varsayılan olarak Windows kayıtlarını gösterir. Örnekleri incelemek için kaynak filtresinden Simülasyon seçin. Lokasyon adları paylaşılan mülk listesinden alınmıştır. Pilot yazıcı ilk lokasyona örnek olarak eşleştirilmiştir; gerçek cihaz konumu henüz doğrulanmamıştır.

Impressions: kopyalar dahil raporlanan toplam baskı yüzü. Fiziksel kağıt adedi değildir ve Copies ile tekrar çarpılmaz. Bilinmeyen adet, kopya ve renk bilgileri sıfır olarak kabul edilmez. QueueCompleted yalnızca kuyruk durumudur; fiziksel çıktı kanıtı değildir. Olay zamanı UTC saklanır, arayüz ve tarih filtreleri Europe/Istanbul kullanır. Yazıcı taşındığında baskı kaydının eski lokasyonu korunur. Belge içeriği ve belge başlığı toplanmaz.

Development:SeedDemoData=false ayarı (PowerShell'de $env:Development__SeedDemoData='false') yeni boş veritabanında örnek veri yüklenmesini kapatır. Var olan örnek kayıtları silmez. Temiz deneme için farklı bir SQLite yolu ConnectionStrings:PrintDb ayarıyla verilebilir. Başlangıç verileri boş lokasyon tablosuna eklenir. Önceki sürümdeki değiştirilmemiş Lokasyon 01–11 kayıtları yeni adlarla güncellenir; kimlikleri ve ilişkileri korunur. Kullanıcının düzenlediği lokasyon adları değiştirilmez.

EF Core migration kullanılır. Bilinen v0.1 veritabanı yedeklenip veri kaybı olmadan yükseltilir; ayrıntılar v0.2 notlarındadır. SQLite yerel geliştirme içindir; 11 lokasyonun üretim veritabanı kararı değildir.

## Çözüm yapısı

- src/PrintMerkez.Core: veri modelleri, olay sözleşmesi ve etiketler.
- src/PrintMerkez.Web: Razor Pages, olay kabul API'si, EF Core ve yerel SQLite.
- src/PrintMerkez.Agent: açıkça etkinleştirilen olay simülatörü ve tek seferlik, salt okunur Windows PrintService pilot okuyucusu. Sürekli Windows ajanı henüz değildir.
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

1. Windows PrintService 307 ayrıştırıcısı ve web içi kalıcı devam noktasıyla aktarım eklendi; çoklu kopya/çift taraflı cihaz doğrulaması ve uzun kesinti pilotu sürer.
2. Yerel kalıcı ajan tamponu, yeniden başlatma sonrası devam ve bağlantı kopması testleri.
3. AD hesabı/departman eşleştirme, yönetici/rapor rolleri ve oturum açma.
4. Maliyet mutabakatı, ek raporlar ve tarihsel verinin kontrollü tamamlama politikası (tarihli tarifeler eklendi).
5. Baskı öncesi denetim için ayrı teknik pilot; yetki ve kota rezervasyonu/sonlandırma.
6. Lokasyon bazlı sunucu/ajan yapılandırması ve merkezi veritabanı seçimi (SQLite migration eklendi).
7. Toner/sayaç, PIN/kart, beklet-bırak, fotokopi/tarama için model ve üretici API/SDK doğrulaması.

Sunucu kurulumu altyapı ekibinin sorumluluğundadır. Sunucu açıldığında ilk entegrasyon: SRV-PRINT üzerindeki Olivetti kuyruğundan bir test olayını API'ye aktarmak ve kullanıcı/zaman/adet alanlarını karşılaştırmak. Bu sürüm gerçek baskıyı izleme veya engelleme iddiasında bulunmaz.

## Teknik kaynaklar

- [Microsoft Razor Pages](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/index)
- [Microsoft Razor Pages ve EF Core](https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/intro?view=aspnetcore-10.0)
