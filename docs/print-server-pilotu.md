# Print server ilk veri doğrulaması

12 Kasım 2026 kontrolü: `10.60.4.40`, DNS `Moi-PrinSrv.trnavm.com`, bilgisayar adı `MOI-PRINSRV`.
Windows Server 2025 Standard, Print Spooler çalışıyor. Gerçek kuyruk adı `Moi İşletme Printer`,
paylaşım `\\MOI-PRINSRV\moi-isletme-255mf`, sürücü `Olivetti d-COPIA 255MF KX`, yazıcı portu `10.60.23.4`.
Bu değerler sunucudan okunmuştur; yazıcının lokasyonu ayrıca doğrulanmalıdır. İlk lokasyona otomatik atama yapılmaz.

## Salt okunur kontrol

Proje klasöründe:

```powershell
./scripts/read-print-server.ps1
```

Mevcut Windows kimliğiyle uzaktaki Operational günlüğünü okur. Parola istemez veya saklamaz.
API anahtarı gerektirmez, API'ye göndermez, veritabanını veya sunucu ayarlarını değiştirmez.
Varsayılan olarak son 50 adet 307 kaydını inceler, yalnızca seçilen kuyruğun kayıtlarını gösterir.
Başka kuyruklar ve desteklenmeyen olaylar ayrı sayılır. Kayıt sınırına ulaşılırsa daha eski işler bu kontrolde yer almayabilir.
Belge içeriği, belge başlığı ve ham XML yazdırılmaz/kaydedilmez. Çıktı kullanıcı hesabı ve istemci bilgisayarını içerir;
kurum dışıyla paylaşılmamalıdır. Yetki hatasında olay günlüğü okuma izni altyapı ekibiyle düzenlenmelidir.

```powershell
./scripts/read-print-server.ps1 -MaxEvents 100
```

## Gerçek cihaz doğrulaması

1. Operational günlüğünü etkinleştirin. Eski, kaydedilmemiş baskılar geri gelmez.
2. Test bilgisayarında yazıcıyı sunucu paylaşımı üzerinden kullanın. Yazıcının IP'sine doğrudan baskı bu sunucudan geçmez.
3. İlk olarak 1 sayfa, 1 kopya gönderin. Ardından kontrollü olarak 2 sayfa/1 kopya ve 2 sayfa/2 kopya deneyin.
4. Her iş için gönderilen kullanıcı, zaman, kuyruk ve `ReportedPages` değerini fiziksel testle karşılaştırın.
5. Çift taraflı baskı kullanılıyorsa onu da ayrıca doğrulayın. Bildirilen sayfa ile fiziksel kağıt aynı ölçü değildir.

307 sürüm 0 şeması sunucunun provider metadata'sından okunmuştur. `param3` hesap, `param4` istemci,
`param5` yazıcı, `param6` port, `param8` raporlanan sayfa değeridir. İstemci adı domain olarak yorumlanmaz.
`param2` belge başlığıdır ve sonuç modeline alınmaz. Sıfır/eksik sayfa bilinmiyor olarak kalır.
Kopya ve renk bilgisi bu olaydan tahmin edilmez. Bu pilot `ReportedPages` değerini henüz uygulamanın aylık sayacına eklemez.
Olay kimliği sunucu/kanal/kayıt kimliği/UTC zamanından üretilir; günlük temizlenince yeniden başlayan kayıt numaraları
farklı zamanda farklı kimlik üretir. Bu, uzun süreli kesintilerde silinen kayıtların kurtarılmasını sağlamaz.

12 Kasım ilk gerçek test: masaüstündeki TXT dosyası sunucu bağlantısından 1 kopya gönderildi.
Sunucunun 307 kaydında 1 sayfa raporlandı. Gerçek UserData alanlarının `Param3` gibi büyük `P` ile
geldiği görüldü; ayrıştırıcı hem şablondaki `param3` hem gerçek kayıttaki `Param3` biçimini destekler.
Bu tek sayfalık kontrol, çoklu kopya/çift taraflı baskı hesabının doğrulandığı anlamına gelmez.

## Sonraki entegrasyon sınırı

Bu araç tek seferlik, sınırlı ve salt okunur bir pilot okuyucudur. Sürekli veri toplama veya Windows Service değildir.
Gerçek testlerden sonra kuyruk ile uygulama envanter kimliği ve gerçek lokasyon eşleştirilecek;
API aktarımı, kalıcı yerel tampon, kaldığı yerden devam, yeniden deneme ve günlük silinmesi tespiti eklenecektir.
AD eşleştirmesi ve gerçek SMTP gönderimi bu kontrolün kapsamına girmez. Gerçek baskılar üzerinde otomatik dış mail gönderilmez.
