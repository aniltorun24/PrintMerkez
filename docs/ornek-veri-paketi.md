# Otomatik örnek veri paketi

Şirket bilgisi beklenirken mevcut ekranları denemek için geliştirme ortamına ilişkili test kayıtları eklenir. Yeni ürün modülü eklenmez; mevcut envanter, kullanıcı/departman, maliyet ve denetim işlevleri örneklenir. Kota engelleme, AD girişi, kart/PIN veya gerçek cihaz haberleşmesi yapılmış gibi kayıt üretilmez.

Development:SeedDemoData ve Development:SeedScenarioData ayarları true ise uygulama açılışında bir kez yüklenir. İkinci ayar appsettings.Development.json içinde açıktır. Testing ortamında otomatik testler bunu kapatır. İkisinden biri false ise yeni yükleme yapılmaz; önceden yüklenen kayıtlar silinmez.

## İçerik

- Sekiz örnek departman: Muhasebe, Finans, İnsan Kaynakları, Bilgi İşlem, Satın Alma, Operasyon, Teknik Hizmetler, Yönetim. Adları [Örnek] ile başlar; gerçek şirket departmanları olduğu iddia edilmez.
- Yükleme anındaki her departmana üç DEMO hesaplı kullanıcı; mevcut departmanlar da bu kapsamdadır. Başlangıçta yalnızca sekiz örnek departman varsa toplam 24 kullanıcı eklenir.
- Her mevcut lokasyona iki örnek yazıcı. 11 lokasyonda toplam 22 yeni örnek yazıcı; mevcut Pilot Olivetti kaydı korunur. Model metinleri doğrulanmamış örnek cihaz olarak işaretlenir. IP adresi ve gerçek kuyruk adı verilmez.
- Son örnek yazıcı hariç her örnek yazıcıya iki tarihli tarife; 11 lokasyonda 42 örnek tarife. Tutarlar sentetiktir, şirketin gerçek maliyetleri değildir. Son örnek yazıcı tarifesiz senaryoyu gösterir.
- Yazıcı başına 20 olay; 11 lokasyonda toplam 440 yeni baskı kaydı. Kaynak her zaman Simulation, sunucu etiketi DEMO-SCENARIO-V1 olur. Yaklaşık son 40 güne yayılan renkli, siyah-beyaz, renk/adet/kopya bilgisi eksik, tamamlanmış, iptal ve hata durumları bulunur.
- Kullanıcı ve departman eşleştirmesi, olay zamanına göre tarife seçimi ve maliyet hesabı mevcut CostCalculator üzerinden yapılır.
- Yönetim eklemeleri gerçek uygulama kayıt yolunu kullandığından denetim geçmişine düşer. Yeni eklenen örnek kullanıcılardan biri pasife alınarak düzenleme geçmişi de örneklenir. Bu işaret baskı engellemez.

## Verilerin korunması

Paket tek veritabanı işlemi içinde yüklenir. Herhangi bir adım hata verirse kısmi kayıtlar geri alınır. Tamamlandığında AuditEntries içine DemoScenario / scenario-v1 tamamlanma kaydı yazılır. Sonraki başlangıçlar bu kaydı görüp tekrar yüklemez; kullanıcı tarafından sonradan düzenlenen örnekler de geri değiştirilmez. Mevcut kayıtlar silinmez veya fiyatlandırılmaz. Daha sonra eklenen departmanlar için her açılışta otomatik kullanıcı üretimi yapılmaz.

## İnceleme

Panel: http://127.0.0.1:5180/?Source=Simulation

Baskı geçmişi: http://127.0.0.1:5180/Jobs?Source=Simulation

Kullanıcılar, Departmanlar, Yazıcı envanteri, Maliyet tarifeleri ve Denetim kayıtları ekranlarından ilişkileri inceleyin. Windows kayıtları görünümü boş kalabilir; bu örnekler gerçek Windows baskısı değildir. Önceki 12 başlangıç baskısı korunur, bu nedenle toplam simülasyon sayısı başka ekleme yoksa 452 olur.
