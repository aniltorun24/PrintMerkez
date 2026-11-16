# Geliştirme bilgisayarında arka plan çalışması

14 Kasım 2026: Kurulum kullanıcının kendi bilgisayarına yapıldı. Yazıcı envanter kimliği 24 korunur;
sunucu bağlantısı `Moi-PrintSrv.trnavm.com` olarak güncellenmiştir. Önceki sunucu adı yalnızca eski
olayların doğrulanması için açık izin listesinde tutulur. Yeni yazıcı veya ikinci sunucu envanteri açılmaz.
Farklı bilgisayardan gönderilen `cyk` baskısının sunucuda bulunduğu, önceki kaynak adı kontrolünde
takıldığı ve bu güncellemeden sonra mevcut yazıcıya aktarıldığı doğrulanmıştır.

## Nasıl çalışır?

- Kullanıcı oturum açınca `PrintMerkez-Local-1931` Windows görevi başlar. Görev mevcut kullanıcının
  etkileşimli kimliğini kullanır; parola saklanmaz. Print server okuma yetkileri aynı kimlikle kullanılır.
- Penceresiz PowerShell gözeticisi, `runtime/local/PrintMerkez.Web.exe` yayımlanmış sürümünü başlatır.
  Visual Studio veya Codex sohbetinin açık kalması gerekmez. Uygulama `127.0.0.1:5180` adresindedir.
- Uygulama kapanırsa gözetici yaklaşık 5 saniye sonra tekrar açar. HTTP yanıtı üç ardışık kontrolde
  alınamazsa süreç yeniden başlatılır. Print server ağ hatası uygulama çökmesi sayılmaz; toplayıcı yeniden dener.
- Gözetici beklenmedik şekilde sonlanırsa Windows görevinin dakikalık kontrolü yeniden başlatır. Çalışan göreve
  ikinci örnek eklenmez; manuel duraklatma bayrağı varsa yeni uygulama başlatılmaz. Kalan eski
  çocuk süreç yalnızca dosya yolu ve eski üst süreç kimliği doğrulanırsa temizlenir.
- Eşzamanlı gözeticiler dosya kilidiyle engellenir. Windows görevi de ikinci örneği başlatmaz.
- Görev için çalışma süresi sınırı kaldırılmıştır; bataryaya geçişte otomatik durdurulmaz.
- Başlatıcı işletim sistemindeki Windows PowerShell'i kullanır; Codex'in kurulum veya sürüm klasörüne bağlı değildir.

Bu, şimdilik bir **kullanıcı oturumu geliştirme kurulumu**dur. Bilgisayar kapalıyken, uyurken veya kullanıcı
oturumu kapatıldığında veri toplanmaz. Ekranı kilitlemek oturumu kapatmaz. Yeniden oturum açılınca uygulama
kaldığı yerden devam eder; sunucu/cihaz geçmişinden silinmiş kayıtlar geri getirilemez.
Bu kurulum Windows başlangıcında, oturum açılmadan çalışan bir sunucu servisi değildir.
Son aşamada sürekli açık print server için servis hesabı ve dağıtım planı ayrıca hazırlanacaktır.

## Kullanım

Proje klasöründe PowerShell açarak:

```powershell
./scripts/background-status.ps1    # Durum ve aktarım bilgileri
./scripts/stop-background.ps1      # Kontrollü durdur ve otomatik yeniden açılmayı duraklat
./scripts/start-background.ps1     # Yeniden başlat / duraklatmayı kaldır
./scripts/install-background.ps1   # Kod değişikliğini yayınla, kurulumu güncelle ve başlat
./scripts/uninstall-background.ps1 # Otomatik başlangıcı kaldır; veri ve logları koru
```

Visual Studio ile F5 kullanmadan önce arka planı durdurun. İkisi aynı 5180 portunu aynı anda kullanamaz.
Kod değişikliği çalışan yayımlanmış sürüme kendiliğinden geçmez; `install-background.ps1` ile güncellenir.
Uygulama yayımlanan klasöründen, veri ise mevcut `src/PrintMerkez.Web/App_Data/printmerkez.db` dosyasından
okunur. Yayın paketine veritabanı, geçmiş kopyaları ve devam dosyaları kopyalanmaz; veriler yerinde korunur.
API anahtarı komut satırına yazılmaz, dosyadan yalnızca alt sürecin ortamına aktarılır. SMTP önizleme modundadır.

Durum dosyaları ve işlem logları: `src/PrintMerkez.Web/App_Data/background/`.
`status.json` süreç kimlikleri ve yeniden başlatma sayısını, `logs/supervisor.log` yaşam döngüsünü,
`logs/app-*.log` ve `logs/error-*.log` uygulama çıktılarını içerir. `/health` hem uygulama canlılığını hem de
Windows/cihaz toplayıcılarının son kontrol ve hata durumunu verir; `status=ok` tek başına veri aktarımı kanıtı değildir.
Loglar kurum içi bilgiler içerebilir; kurum dışıyla paylaşılmamalıdır.

Kurulum Windows görev kaydı için izin bulamazsa mevcut kullanıcının Başlangıç klasörü kısayoluna geçebilir.
Bu bilgisayarda görev kaydı başarıyla oluşturulmuştur; Başlangıç kısayolu yedeği kullanılmamaktadır.
Otomatik oturum açma veya güç/uyku ilkeleri değiştirilmemiştir.

## Doğrulama

- 111 otomatik test geçti; açıkça izin verilmiş eski sunucu adı ve farklı kaynağın reddi de test edildi.
- Yayın kurulumu, yerel panel ve gerçek sunucudan eksik kaydın alınması doğrulandı.
- Uygulama zorla sonlandırıldı; gözeticinin farklı süreç kimliğiyle yeniden açtığı gözlendi.
- Gözetici de zorla sonlandırıldı; dakikalık Windows tetikleyicisi farklı gözetici ve uygulama süreç kimlikleriyle
  toparlandı. Testte tetikleyiciye kalan süre nedeniyle kurtarma 19 saniyede gerçekleşti; süre en fazla yaklaşık bir dakikadır.
- Kontrollü durdurma ve güncelleme sonrası başlatma denendi. Gerçek bilgisayar yeniden başlatması yapılmadı;
  oturum açılış tetikleyicisi, kimlik, süre/batarya ve yeniden deneme ayarları Windows görevinden kontrol edildi.

Kimlik seçimi için [Microsoft görev oturum tipleri](https://learn.microsoft.com/en-us/windows/win32/taskschd/principal-logontype)
esas alındı. Ağ erişimi sağlamayan S4U kullanılmadı.
