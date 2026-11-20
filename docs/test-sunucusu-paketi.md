# Windows Server 2025 test paketi

Bu aşama sunucuya aktarılacak paketi hazırlar; uzak sunucuda IIS sitesi, Windows Servisi veya ağ erişimi oluşturmaz.
Sunucuda IIS ve .NET 10 Hosting Bundle kurulu olmalıdır. SDK gerekmez.

## Dosyalar ve roller

`scripts/package-test-server.ps1`, aynı kaynak kodu iki bağımsız süreç için yayınlar:
- `Web`: Production / Deployment:Mode=Server / Deployment:Role=Web. IIS arayüzü, yetkili Razor Pages, SSE.
  Veri toplayıcı ve bildirim işçisi bu süreçte başlatılmaz.
- `Collector`: Production / Deployment:Role=Collector. Windows Servisi `PrintMerkezCollector` için hazır yürütülebilir dosya.
  Sadece veri toplama, bildirim ve yerel sağlık ucu vardır; giriş/yönetim/API uçları yayınlanmaz.
  Varsayılan sağlık adresi `http://127.0.0.1:5181` olur.
- `DataSeed`: gerçek veritabanının SQLite Backup API ile alınmış tutarlı kopyası; kaynak veritabanına yazılmaz.
  Devam noktası veritabanından önce okunur; olası tekrarlar mevcut Server/EventId benzersizliğiyle ayıklanır.
- `Configure-TestServer.ps1`: ilk kurulum dosyalarını hazırlar; mevcut kuruluma veya veritabanına yazmayı reddeder.
- `manifest.json`: paket dosyalarının SHA256 değerleri. `OKU.txt`: kısa aktarım yönergesi.

## Adım adım ilk kurulum

1. ZIP'i sunucuda `C:\PrintMerkez\Kurulum` içine açın. Web, Collector ve DataSeed doğrudan bu klasörün altında olmalı.
2. Sonraki aşamada yönetici Windows PowerShell'den `Configure-TestServer.ps1` çalıştırılır.
   Yerel testte `Configure-TestServer.ps1 -UseTestPassword` seçilirse şifre sormadan `admin / 12345` ayarlanır;
   `Deployment:AllowTestPassword=true` olur ve başka bilgisayarlardan erişim kapalı kalır.
   En az 12 karakterli yeni admin şifresi iki kez istenir; düz şifre dosyaya veya komut satırına yazılmaz.
   Betik Web/Collector dosyalarını kopyalar, veriyi Data içine aktarır, özeti dış ayar dosyasına yazar ve veritabanını hazırlar.
   IIS sitesi veya servis kaydı oluşturmaz ve toplayıcıları başlatmaz.
3. IIS uygulama havuzu ve klasör izinleri hazırlanır. İlk site yalnızca `127.0.0.1:8080` bağlantısıyla test edilir.
   Default Web Site'a ve Print and Document Services rolüne müdahale gerekmez.
4. Veri toplama devralması ayrıca yapılır. Bilgisayardaki ve sunucudaki toplayıcılar bağımsız veri kopyalarına
   kontrolsüz şekilde birlikte yazdırılmaz. Gerekirse geçiş anında güncel veri paketi yeniden alınır.
5. Servis hesabının yerel PrintService Operational günlüğünü okuma yetkisi ve yazıcının HTTPS erişimi doğrulanır.
   Servis kaydı şu rolü çalıştırmalıdır: Collector\PrintMerkez.Web.exe, Production ortamı ve kendi contentRoot'u.
   Windows Servisi otomatik başlangıç/kurtarma ayarlarıyla kaydedilir; web uygulama havuzunda Collector rolü çalıştırılmaz.
6. Başka bilgisayarlardan erişim HTTPS sertifikası, IIS binding, kurum ağ listesi ve güvenlik duvarı ayarıyla ayrıca açılır.

## Kalıcı veriler ve durum

Veritabanı `C:\PrintMerkez\Data\printmerkez.db`; ortak ayarlar `Data\server-settings.json` dosyasındadır.
Kurulum yeni dosya yoluna göre Windows devam noktasını yeniden adlandırır; kuyruk/sunucu/PrinterId=24 korunur.
Sunucuda olay günlüğü, bilgisayarın tam adı eşleşirse yerel oturumla okunur. Geliştirme bilgisayarında uzak okuma devam eder.
SQLite yerel diskte WAL ve 30 saniyelik bekleme ile test edilir. Ağ paylaşımı üzerinde veritabanı kullanılmaz.
Bu bir SQL Server geçişi veya şirket çapı kapasite onayı değildir.

Collector, Data klasöründe süreç kilidi tutar ve 5 saniyede bir atomik durum dosyası yazar.
IIS bu dosyayı okuyarak gerçek servis durumunu gösterir. Dosya yoksa, servis durmuşsa veya 45 saniyeden eskiyse uyarı verir.
Canlı ekran izleyicisi aynı yerel veritabanındaki iş/denetim değişikliklerini ayrı süreçten de algılar.
IIS'in yeniden başlaması Collector sürecini durdurmaz. Servis kapalıyken arayüz eski durumu başarılı gibi sunmaz.

Web oturum anahtarları `Data\Keys` altında kalır, Windows DPAPI ile makineye bağlı korunur.
Klasör ACL'leri IIS uygulama havuzuna ve servis hesabına göre kurulmalıdır; anahtarlar kaynak koduna/ZIP'e alınmaz.
Tanılama günlükleri Logs klasöründe rol/gün bazındadır, 14 gün ve günlük rol başına 10 MB ile sınırlandırılır.
E-posta modu Preview'dur; gerçek SMTP veya AD bağlantısı bu paketle açılmaz.

Sunucu ayarları açıkça verilmezse Production başlangıcı reddedilir. Geliştirme şifresi 12345 yalnızca açıkça seçilmiş yerel test modunda kabul edilir.
Uzaktan erişim varsayılan kapalıdır; açıldığında HTTPS ve tanımlı ağ aralığı birlikte gerekir.
Geliştirme bilgisayarının mevcut yalnızca yerel erişim ve arka plan çalışma düzeni korunur.

## Doğrulama

160 test: önceki iş akışları, rol ayrımı, ağ/HTTPS koşulları, yerel/uzak olay oturumu seçimi,
tek toplayıcı kilidi, durmuş/eski heartbeat ve kaynak veriyi değiştirmeyen yedek.
Yayın dosyaları ayrı test klasöründe Windows PowerShell 5.1 ile yapılandırılır; iki Production süreci açılarak
giriş, canlı veri, Collector'da yönetim uçlarının kapalı oluşu, web yeniden başlatıldığında servis bağımsızlığı
ve oturum anahtarlarının korunması kontrol edilir. Gerçek sunucudaki IIS/SCM/izin testleri sonraki kurulum aşamasıdır.

[Microsoft IIS yayını](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
ve [Windows Servisi rehberi](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-10.0).
