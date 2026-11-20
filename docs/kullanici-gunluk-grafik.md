# Kullanıcı günlük baskı grafiği

Kullanıcılar listesindeki kullanıcı adına tıklanınca `/Users/Details/{id}` açılır. Düzenle bağlantısı aynı işlevini korur.
Varsayılan görünüm Türkiye saatine göre bugün dahil son 30 gündür. Tarihler dahil, aralık en fazla 366 gündür.
Gerçek Windows ve simülasyon kaynakları ayrı seçilir; varsayılan gerçek kayıtlardır.

Grafik ölçümü:
- Toplam çıktı: `Impressions` değerlerinin toplamı. Kopyalar zaten dahildir, tekrar çarpılmaz. Baskı yüzü sayısıdır.
- Belge / baskı işi: kuyruğu tamamlanan her kayıt bir iş sayılır; benzersiz dosya sayısı veya fiziksel teslim değildir.

Özet kartlarında belge/iş, doğrulanmış baskı yüzü, bilinen Windows belge sayfası ve toplamı doğrulanmamış iş sayısı yer alır.
Tamamı bilinmeyen gün sıfır olarak sunulmaz; bilinmiyor olur. Kısmen bilinen gün bilinen toplamla ve eksik bilgi uyarısıyla gösterilir.
Kayıtsız günler sıfırla doldurulur. İptal/hata/bilinmeyen durumdaki işler grafiğin tamamlanan iş toplamına eklenmez.

Kullanıcı kimliğiyle eşleşen geçmiş işler ve henüz kimlik atanmamış, tam normalize hesap eşleşmesi olan işler raporlanır.
Başka kullanıcıya atanmış işler dahil edilmez; rapor geçmiş kayıtlara yazmaz. Ortak hesap tek toplam olarak görünür.
UTC tarih aralığı filtrelenir; SQL UTC gün/saat bazında özetler, saat özetleri Türkiye günlerine birleştirilir.
Böylece bütün baskı olayları belleğe alınmaz ve gece yarısı sınırı doğru hesaplanır. Mevcut indeksler kullanılır; şema değişmedi.

Grafik yerel SVG ile oluşturulur; dış CDN veya grafik hizmetine kullanıcı verisi gönderilmez. Tema renklerini kullanır.
Dar ekranlarda yatay kaydırılır, ilk açılışta son günler görünür. Kesin değerler açılabilir günlük tabloda da vardır.
Mevcut SSE mekanizması yeni baskı ve tamamlanan ölçümleri sayfa yenilemeden yansıtır; filtre formu, kaydırma ve tablo açıklığı korunur.
Mevcut Admin yetki kuralı detay ekranını ve canlı handler'ını da korur.

Testler: Türkiye gece yarısı sınırı, iki uç tarihin dahil edilmesi, kayıtsız ve bilinmeyen günler,
kopyaların tekrar çarpılmaması, kaynak/durum/hesap ayrımı, kimlik ve geçmiş hesap eşleşmesi, geçersiz aralık,
404 ve oturumsuz erişim. Ayrı test veritabanında tarayıcıyla canlı grafik, ölçüm seçimi ve mobil görünüm kontrol edilir.
