# Kopya sayısının cihaz iş kaydıyla tamamlanması

13 Kasım 2026 pilotu: Olivetti d-COPIA 255MF, `10.60.23.4`, envanter kimliği 24.
Kullanıcının tek sayfanın 3 kopyasını aldığı baskıda Windows 307 sayfa alanı 1 ve 805 kopya alanı 1 bildirdi.
Cihazın salt okunur web iş detayında aynı kullanıcıya ait işte Orijinal Sayfalar 1, Kopya Sayısı 3/3,
Yazdırılan Sayfalar 3/3 ve tamamlanmış durum görüldü. Cihaz kaydı 032980'dir.
Gerçek uygulamada Windows kayıt kimliği 31 / uygulama işi 458 ile eşleşti; belge 1, kopya 3,
toplam 3 olarak kaydedildi. Masaüstü ve 390 piksel mobil görünümde doğrulandı; 99 otomatik test geçti.
Yeni fiziksel baskı veya dış e-posta gönderilmedi. Cihaz geçmişinde bulunmayan daha eski 5 iş bilinmiyor olarak korundu.

## İşleyiş

Windows toplayıcısı kullanıcı, zaman ve belge sayfasını almaya devam eder. Ayrı cihaz okuyucusu 30 saniyede bir
cihazda halen saklanan iş geçmişini okur. Yalnızca envanterde eşleştirilmiş tek IP/yazıcı için çalışır.
Toplamı bilinmeyen Windows kayıtlarının kullanıcıları için detaylar alınır; belge başlığı ve içeriği saklanmaz.

Eşleştirme şu kontrolleri birlikte kullanır:

- Aynı yazıcı, kullanıcı hesabı ve belge sayfa sayısı.
- Cihazın güncel saati ile yanıtın alındığı UTC zamanından düzeltilmiş kabul/bitiş aralığı.
- Hem Windows işi hem cihaz işi için tek aday bulunması. En yakın işi seçip tahmin yapılmaz.
- İşin cihazda başarıyla tamamlanması; tamamlanan/istenen kopya ve sayfa değerlerinin tutarlı olması.
- Bir cihaz işinin yalnızca bir Windows kaydına bağlanması; veritabanında yazıcı/cihaz iş anahtarı tekildir.

Cihaz zamanları dakika hassasiyetindedir; kabul zamanından önce 2 dakika, bitiş dakikasından sonra 1 dakika
tolerans kullanılır. Aynı hesapla yakın zamanlarda aynı uzunlukta birden çok iş gönderilirse eşleştirme belirsiz
kalabilir. Bu durumda kopya ve toplam doldurulmaz. Cihaz saatinin geçmişten bugüne değişmesi de yeniden doğrulama gerektirir.

Doğrulanan işte `Copies=3`, `Impressions=3`, `ReportedDocumentPages=1` saklanır. Kopya adedi ikinci kez
çarpılmaz. Kopya sayısı baskı geçmişinde ayrı sütundur; toplamın altında cihaz iş kaydıyla doğrulandığı belirtilir.
Eşleştirme, zaman ve kaynak anahtarı denetim kaydına yazılır. İşin geçmiş ad-soyad/departman görüntüsü değiştirilmez.

Yeni toplam mevcut aylık eşik hesabını tekrar değerlendirir; dönem/kullanıcı/kural tekilliği tekrar bildirimi önler.
Bu, gerçek kullanıcı-departman-yönetici eşleştirmesi ve gerçek eşik kuralı tanımlandığında çalışır.
SMTP hâlâ önizleme modundadır; bu geliştirme şirket yöneticisine dış e-posta göndermez.

## Sınırlar

Bu adaptör pilot cihazın gözlemlenen web arayüzüne özgüdür; tüm Canon/Kyocera/Olivetti modelleri için
doğrulanmış genel bir API değildir. Firmware arayüzü değişirse okuyucu hata gösterir ve tahminle devam etmez.
Parola gerektirmeyen, cihazın normalde sunduğu salt okunur iş kaydı kullanılır. Yönetici ayarı değiştirilmez.
HTTPS sertifikası, yapılandırmadaki SHA-256 parmak iziyle ve belirli cihaz IP'siyle doğrulanır;
sertifika değişiminde yeni parmak izi altyapı ekibiyle doğrulanmalıdır. Genel sertifika kontrolü kapatılmaz.

Cihaz iş geçmişi sınırlıdır. Artık cihazda bulunmayan eski işlerin kopyası geri üretilemez; bu işler bilinmiyor kalır.
Varsayılan tarama en fazla 3 sayfa/30 iştir; sınır veya desteklenmeyen kayıt varsa genel bakışta uyarı gösterilir.
Uygulama uzun süre kapalı kalırsa hem Windows hem cihaz günlüklerinin saklama süresi veri kaybına yol açabilir.
Çift taraflı, sayfa başına birden çok belge sayfası, kısmi/iptal işler ve diğer sürücüler için ek fiziksel karşılaştırma gerekir.
Şüpheli veya tutarsız kayıtlar aylık adede ve maliyete eklenmez.
Sonraki güncellemede [renk ve maliyet bağlantısı](baski-rengi-ve-maliyet.md) eklendi; cihazın doğruladığı renk fiyata bağlanır.

Temel cihaz iş geçmişi açıklaması: [Olivetti kullanım kılavuzu](https://www.olivetti.com/system/files_force/products/manuals/d-copia_255mf_og_595201_en.pdf?download=1).
