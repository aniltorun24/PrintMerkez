# Canlı baskı izleme

Genel bakış ve Baskı geçmişi sayfaları yeni baskıları ve sonradan tamamlanan kopya/renk/maliyet bilgilerini
sayfanın tamamını yeniden yüklemeden gösterir. Kullanıcı ilk kez güncel sürümü açmalıdır; daha önce açık kalan
eski bir sekmenin bir kez yenilenmesi gerekir. Form, uygulanan filtre, sayfalama ve yatay kaydırma korunur.
İş kaydı beklenirken yazılan ancak henüz uygulanmamış filtre metni silinmez.

## İşleyiş

- Tarayıcı aynı kaynaktaki `/live/events` üzerinden SSE bağlantısı kurar. Bildirim baskı içeriği taşımaz;
  yalnızca sürüm değişikliği bilgisidir. Yeniden bağlantıda tam güncel görünüm alınır.
- Tüm tarayıcılar için süreç başına tek izleyici, aktif izleyici varsa 2 saniyede bir Jobs ve AuditEntries
  tablolarının son kimliklerini indekslerden kontrol eder. Her istemci için ayrı SQL taraması yapılmaz.
- Yeni işler Jobs kimliğini; kopya, fiyat ve yönetim değişiklikleri uygulamanın denetim kaydını değiştirir.
  Uygulamanın kayıt/denetim yolunu atlayan doğrudan SQL değişiklikleri bu bildirimin sözleşmesi değildir.
- Bildirimler birleştirilir; yavaş bağlantının belleğinde en fazla bir bekleyen bildirim tutulur.
- Yalnızca veri bölümleri `?handler=Live` ile alınır. Sunucu anlık görüntüleri 2 saniyelik output cache kullanır;
  sorgu parametreleri ayrı anahtardır ve varsayılan politika kimliği doğrulanmış cevapları ortak önbelleğe almaz.
- ETag değişmediyse 304 ile gövde gönderilmez. ETag süreç neslini, veri sürümünü ve filtreleri içerir.
- Tarayıcı aynı anda tek veri isteği gönderir; aradaki bildirimleri sıraya yığmaz. Yanıt gelmezse zaman aşımı
  ve artan bekleme ile yeniden dener. Sunucu yeniden başlarsa yeni süreç nesli eski önbelleği geçersiz kılar.

## Kullanım

Üstteki canlı bağlantı göstergesi güncelliği belirtir. “Güncellemeyi duraklat” ile uzun kayıtları okuyabilir,
“Canlı güncellemeyi başlat” ile son veriye dönebilirsiniz. Metin seçimi veya veri bölümündeki bir bağlantıya
odaklanma sırasında görünüm değişimi bekletilir. Sekme gizlendiğinde bağlantı durur; geri dönüldüğünde toparlanır.
Ağ kesilince son veriler korunur ve hata durumu gösterilir. Kalp atışı kaybolursa bağlantı yeniden kurulur.

Ekran güncellemesi veri toplama aralıklarını değiştirmez: Windows yaklaşık 10, cihaz iş kaydı yaklaşık 30
saniyede bir okunur. Veri uygulamaya geldikten sonra canlı bildirim ve görüntü yenilemesi birkaç saniye alabilir.
Bu, fiziksel yazıcıdan sıfır gecikmeli akış iddiası değildir.

## Doğrulama ve sınırlar

115 otomatik test; filtre ayrımı, ETag/304, SSE bildirimi ve 10.000 değişiklikte sınırlı kuyruk kontrolünü kapsar.
Ayrı deneme veritabanında gerçek tarayıcı ile yeni kayıt, geç maliyet, duraklatma/devam, ağ kesintisi/toparlanma,
uygulanmamış filtre metni ve sayfalama kontrol edildi. 100 işlik denemede görünür tablo 25 satırla sınırlı kaldı;
100 ayrı sayfa yüklemesi yerine 6 parça isteğiyle güncellendi. Gerçek kurumsal veritabanına deneme işi eklenmedi.

Şirket çapında kapasite garantisi için kullanıcı/filtre çeşitliliği, merkezi veritabanı ve gerçek donanımla yük
testi ayrıca gereklidir. Tek süreçli SQLite geliştirme ölçümü üretim kapasitesi olarak sunulmaz.
IIS/ters vekil dağıtımında SSE tamponlama/zaman aşımı ayarları, AD yetkilendirmesi ve anlık görüntülerin
kullanıcı erişim kapsamı doğrulanmalıdır. Bu sürüm mevcut yerel erişim kısıtını korur.

[Microsoft output cache belgesi](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output?view=aspnetcore-10.0)
ve [SSE protokolü](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events/Using_server-sent_events).
