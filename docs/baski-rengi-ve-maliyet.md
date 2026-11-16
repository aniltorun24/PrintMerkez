# Baskı rengi ve maliyet bağlantısı - 13 Kasım 2026

Maliyet, kopyalar dahil toplam baskı yüzü ile iş zamanında geçerli birim tarifenin çarpımıdır.
Örnek: toplam 3 siyah-beyaz yüz ve 15 kuruş/yüz tarife, 45 kuruş maliyet oluşturur. Kopya ikinci kez çarpılmaz.

## İki farklı renk bilgisi

- `RequestedColor`: Windows 805 olayının bildirdiği renk ayarı. Ekranda **Windows kaydı** olarak gösterilir.
  İlgili 307 kaydının sunucusu, iş kimliği, veri boyutu ve önceki 30 saniyelik zaman penceresiyle eşleştirilir.
  Tek bir aday bulunmuyorsa bilinmiyor kalır. Sürücü/uygulama seçimi dönüştürebileceğinden bu alan fiziksel renk kanıtı değildir.
- `Color`: cihazın tamamlanan iş detayında bildirdiği gerçek baskı modu. Maliyet bu alanla hesaplanır.

Pilot firmware'in `sJobColorMode` eşleştirmesi cihazın kendi `base.js` ve dil kaynağından doğrulandı:
1 tam renk, 2 gri ton, 3 siyah-beyazdır. 2/3 siyah-beyaz tarifeye, 1 renkli tarifeye gider.
Otomatik/karışık ve doğrulanmamış diğer kodlarda bütün işi tek fiyatla ücretlendirmek yerine renk bilinmiyor bırakılır.
Cihazın `_pp.JobDetail[9]` sonlandırma alanı da okunur; pilot monokrom cihazda bu değer 3'tür.
Belge adı ve belge içeriği bu işlemde kaydedilmez veya çalıştırılmaz.

Olivetti d-COPIA 255MF monokrom modeldir. Windows renkli talep bildirse bile cihaz siyah-beyaz baskı yaptıysa
maliyet siyah-beyaz tarifeden hesaplanır. Bu modele renkli baskı yeteneği kazandırıldığı anlamına gelmez.
[Üretici ürün bilgisi](https://www.olivetti.com/en/office/multifunction-printers/mfp-a3-bw/d-copia-255mf).

## Geç gelen bilgi ve fiyat geçmişi

Önceden kopya/toplam bilgisi alınmış ancak rengi eksik bir iş, aynı cihaz iş anahtarıyla tekrar bulunduğunda
renk bilgisi tamamlanır. Kullanıcı/departman geçmiş görüntüsü, olay kimliği ve toplam sayım korunur.
Hesaplanmış bir tutar tekrar fiyatlandırılmaz. Yeni renk ve fiyat bilgisi denetim kaydına yazılır.
Windows metadata tekrar okuması yalnızca eksik talep rengini tamamlar; kopya, gerçekleşen renk ve tutarı değiştirmez.

Yalnızca `EffectiveFromUtc <= OccurredAtUtc` koşulundaki en yeni tarife uygulanır.
Pilot kullanıcı tarifesi 13 Kasım 2026 12:23 Türkiye saati itibarıyla 15/30 kuruştur.
10:05 tarihli eski baskı, renk bilgisi tamamlanınca da bu sonraki tarifeden ücretlendirilmez;
ekranda **Geçerli tarife yok** görünmesi beklenen davranıştır.

Yeni baskılar Windows kaydından sonra cihaz okuyucusunun 30 saniyelik kontrolünde tamamlanır.
Adet/kopya/renk doğrulanamadığında fiyat tahmini yapılmaz. Geçmişteki belirsiz beş baskı bilinmiyor olarak kalır.
Renk modu ve tarihli tarife testleri sahte verilerle izole veritabanlarında çalışır; şirketin gerçek veritabanına
örnek maliyet satırı eklenmez. Bu geliştirmede yeni fiziksel baskı veya dış e-posta gönderilmez.
