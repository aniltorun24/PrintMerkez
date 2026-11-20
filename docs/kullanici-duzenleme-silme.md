# Kullanıcı düzenleme ve silme

Kullanıcılar listesi artık tam genişliktedir. Düzenle bağlantısı `/Users/Edit/{id}` sayfasını açar.
Yeni kullanıcı ekleme de aynı sayfanın kimliksiz `/Users/Edit` görünümündedir; liste içinde kayıt formu yoktur.
Arama ve liste sayfası düzenleme/iptal/kayıt sonrasında korunur. Eski `/Users/{id}` adresleri düzenleme sayfasına yönlenir;
eski listeye POST ile kayıt değiştirme yolu 405 döndürür.

Edit PageModel açıkça Admin politikasını ister. Uygulama çerezindeki Admin rolü yetkilidir; dizindeki Çalışan/Yönetici
bildirim rolü uygulama yetkisi değildir. Butonları gizlemekle yetinilmez; doğrudan GET ve POST istekleri de korunur.
Tüm formlarda antiforgery vardır. Kayıt sürümü değişmiş eski formlar 409 ile reddedilir.

Silme, düzenleme sayfasındaki ayrı bir POST formudur ve açık onay kutusu ister. `DeletedAtUtc` kaydedilir,
kullanıcı pasif yapılır ve listeden/günlük detaydan/yeni baskı eşleştirmelerinden çıkarılır. Baskı, denetim,
eski yönetici ataması ve bildirim geçmişinin yabancı anahtarları korunur; AD'ye silme veya değiştirme isteği gönderilmez.
AD aktarımı arşivlenmiş kimliği yeniden etkinleştirmez. Aynı hesabı yeniden eklemek yeni bir kişi oluşturmaz.
Bu sürümde arşivden geri alma arayüzü yoktur.

Aktif yönetici ataması varsa önce atamanın kapatılması veya değiştirilmesi gerekir. İşlenmekte olan bildirim varsa
silme bekletilir. Kullanıcının Pending/Retry/Blocked bildirimleri atomik işlem içinde Cancelled olur; sonuçlanmış
bildirimler değiştirilmez. Silme ve iptal sayısı denetim kaydına yazılır. Gerçek yazıcı erişimi bu işlemle engellenmez.

183 test geçti. Yeni kontroller: ayrı düzenleme sayfası, kayıt, AD kimliğinin korunması, anonim/yetkisiz erişim,
bildirim rolü Yönetici olsa bile Admin yetkisi olmadan engelleme, antiforgery/onay, aktif yönetici ve gönderim koruması,
eski form çakışması, geçmişin korunması, bildirim iptali ve AD'den silinmiş kaydın geri gelmemesi.
Tarayıcıda izole veritabanıyla ekleme-düzenleme-silme, aramanın korunması ve mobil/tema görünümü doğrulandı.
Doğrulama sırasında gerçek kullanıcı silinmedi.
