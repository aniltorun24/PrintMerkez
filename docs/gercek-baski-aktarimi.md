# Gerçek baskı aktarımı - yerel pilot

Güncelleme: [Cihaz iş kaydıyla kopya doğrulaması](cihaz-kopya-dogrulamasi.md) eklendi.
Eşleştirilebilen ve tamamlandığı doğrulanan işlerin kopya/toplam bilgisi cihazdan tamamlanır;
aşağıdaki Windows alanı tek başına toplam kabul edilmez.

## Sayım düzeltmesi - 13 Kasım 2026

Kullanıcı tek sayfadan 3 kopya aldığı işi doğruladı. Aynı işin 307 olayında belge sayfası 1,
805 olayında Copies 1 bildirildi. Dolayısıyla mevcut sürücü akışında bu alanlarla kopyalar dahil
gerçek toplam çıkarılamaz. Önceki pilotun `Impressions=Param8` eşleştirmesi kaldırıldı.
`ReportedDocumentPages` ham Windows sayfa alanını korur; `Impressions` ve `Copies` doğrulanamadığında null kalır.
Eski `win307-` kayıtlarındaki değerler yeni alana taşınır, sayım/maliyet değerleri geri çekilir;
diğer kaynaklardan gelen toplamlar değiştirilmez. Veri yükseltme öncesi yedek alınır ve düzeltme denetim kaydı oluşturulur.
Panel ve CSV belge sayfası ile toplam çıktı adedini ayrı gösterir. Aylık eşik hesabı, doğrulanmamış toplamları içermez.
Bu değişiklik kopya sayısını otomatik elde etme problemini çözmez; yanlış kesinlikte raporlamayı durdurur.
Sonraki aşama sürücü/istemci baskı ayarları ve cihazın iş muhasebe kaydıyla kontrollü karşılaştırmadır.

13 Kasım 2026: Kullanıcı yazıcının Mall of İstanbul'a ait olduğunu doğruladı.
Envanter kimliği 24, lokasyon kimliği 7 olarak eşleştirildi ve geliştirme yapılandırmasında aktarım etkinleştirildi.
Sunucuda saklanan 5 gerçek olay / 7 raporlanan sayfa aktarıldı. Uygulama yeniden başlatıldıktan sonra
aynı kayıtların çoğalmadığı doğrulandı. Yeni fiziksel baskı gönderilmedi. Sayılar bu kontrol anına aittir.

12 Kasım 2026 itibarıyla web uygulamasına Windows PrintService toplayıcısı eklendi.
Uygulama açıkken mevcut Windows hesabının yetkileriyle sunucunun 307 kayıtlarını 10 saniyede bir okur.
Sunucuya yazılım kurulmaz ve fiziksel baskı başlatılmaz. Ajan projesindeki salt okunur kontrol aracı ayrıca korunur.
Bu sürüm hâlâ yerel pilot içindir; AD ile giriş tamamlanmadan ağ üzerinden yönetim paneli sunulmaz.

## Yapılandırma

`src/PrintMerkez.Web/appsettings.Development.json` içindeki `WindowsCollector` bölümünde:

- `Enabled`: bağlantıyı etkinleştirir.
- `Server`: `Moi-PrinSrv.trnavm.com`.
- `Queue`: `Moi İşletme Printer` (sunucudaki gerçek kuyruk adı).
- `PrinterId`: panelde gerçek lokasyonuna kaydedilmiş envanter kimliği.
- `PollSeconds`: varsayılan 10; izin verilen 5-300 saniye.

`PrinterId` ile seçilen yazıcının `QueueName` alanı yapılandırmadaki kuyrukla uyuşmalıdır.
Uygulama yanlış eşleştirmeyi otomatik tahmin etmez. Sunucuya erişemediğinde tekrar dener; genel bakışta hata gösterir.
Başarılı kontrol zamanı ve bağlantı durumu genel bakıştadır. Yeni sayıları görmek için sayfayı yenileyin.
Yalnızca yapılandırılan kuyruk içeri alınır. Diğer lokasyonlardaki IP yazıcıları kendiliğinden takip edilmiş olmaz.

## Sayım ve veri anlamı

- `ReportedDocumentPages`, Windows'un bildirdiği `Param8` sayfa sayısıdır; pozitif değerler aktarılır, sıfır/eksik değer bilinmiyor kalır.
- `Impressions` kopyalar dahil toplamdır. Mevcut Windows okuyucusu güvenilir toplam alamadığı için bunu null bırakır.
- Kopya sayısı ve renk 307 olayından çıkarılmaz; bilinmiyor gösterilir. Adet tekrar kopyayla çarpılmaz.
- Çoklu kopya ve çift taraflı sürücü davranışı ayrıca karşılaştırılmalıdır. Bu değer fiziksel kağıt veya teslim kanıtı değildir.
- Belge başlığı ve belge içeriği veritabanına ya da devam dosyasına yazılmaz.
- Kullanıcı hesabı geldiği gibi saklanır. İstemci bilgisayar adı domain yerine kullanılmaz; AD bağlantısı kurulmuş sayılmaz.
- Renk ve gerçek tarife bilinmediğinden maliyet uydurulmaz. Kullanıcı/departman eşleştirmesi yoksa açıkça belirtilir.
- Mevcut aylık uyarılar yalnızca tanımlı gerçek kullanıcı, kural ve yönetici eşleştirmeleriyle çalışır. SMTP hâlâ önizlemededir.

## Yeniden başlatma ve hata davranışı

İlk çalıştırmada günlükte halen saklanan 307 olayları eskiden yeniye taranır. Her kontrolde en fazla 200 kayıt okunur;
artan kayıtlar sonraki kontrolde alınır. Envanterde aynı kuyruğa ait gerçek baskılar görünür.
Devam noktası `App_Data/windows-cursor-*.json` içinde tutulur ve yalnızca başarılı veritabanı kabulünden sonra ilerler.
Veritabanına yazılıp devam noktası kaydedilemeden kapanırsa olay tekrar okunur; sunucu/olay kimliği tekilliği çift sayımı önler.
Dosya kimliği sunucu, kuyruk, yazıcı ve veritabanı ayarına bağlıdır. Dosya bozuksa tahmin ederek devam etmek yerine hata gösterilir.
Desteklenmeyen/bozuk olaylar sessizce atlanmaz; aktarım durur ve panel uyarır.
Devam noktasındaki olay kaybolmuş veya numarası başka bir olay için yeniden kullanılmışsa saklanan kayıtlar yeniden taranır;
panelde olası veri boşluğu uyarısı kalır. Günlükten silinmiş veriler geri getirilemez; saklama boyutu altyapı ekibiyle planlanmalıdır.
Uygulama kapalıyken düzenli okuma durur; tekrar açıldığında sunucu günlüğünde kalan kayıtları alır.

## Örnek verilerin temizlenmesi

`Development:SeedDemoData` ve `Development:SeedScenarioData` kapatılmıştır.
Olay API'si varsayılan olarak simülasyonu reddeder; yalnızca ayrı test ortamında `Agent:AllowSimulation=true` ile açılabilir.
Temizlik tek seferlik, açık bakım komutuyla yapılır. Önce SQLite backup API ile tutarlı yedek alınır;
simülasyon işleri ve yalnızca örnek olduğu belirlenmiş ilişkili kayıtlar transaction içinde temizlenir.
Gerçek Windows işleri ve onların başvurduğu kayıtlar korunur. Şirkete ait lokasyonlar silinmez.
Yedekler `App_Data/backups/` altındadır; kişisel veriler içerebileceğinden kurum dışıyla paylaşılmamalıdır.

Teknik dayanak: [Microsoft olay yer imleri](https://learn.microsoft.com/en-us/windows/win32/wes/bookmarking-events).
