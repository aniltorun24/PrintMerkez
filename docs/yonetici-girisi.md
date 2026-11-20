# Geçici yönetici girişi

AD bağlantısı kurulana kadar yönetim paneli ortak `admin` hesabıyla açılır. Şifre uygulamada açık metin
yerine tuzlanmış PBKDF2-SHA256 özeti olarak saklanır (`LocalAdmin` ayarı, 210.000 yineleme).
Bu hesap AD kullanıcısı değildir; denetim kayıtlarında yapan kişi `admin` görünür.

- Giriş: `/Login`. Yönetim sayfaları, CSV indirme, canlı görünüm ve SSE bağlantısı Admin rolü ister.
- Oturum en fazla 8 saat geçerlidir; kalıcı giriş seçeneği yoktur. Üst çubukta POST/antiforgery korumalı çıkış bulunur.
- Hatalı kullanıcı ve şifre aynı mesajı verir. IP başına dakikada en fazla 10 giriş isteği kabul edilir.
- Formlar antiforgery korumasını kullanır. Başarılı giriş yalnızca yerel dönüş adreslerini kabul eder.
- Çerez HttpOnly/SameSite=Strict kullanır; HTTPS üzerinden açıldığında Secure olur. Mevcut yerel HTTP pilotu korunur.
- Oturumlu cevaplar no-store kullanır; mevcut output cache varsayılanları oturumlu cevapları paylaşmaz.
- Oturum süresi dolan SSE bağlantısı kapanır; canlı veri isteğinin 401/403 cevabı tarayıcıyı girişe götürür.
- Veri toplayıcılar tarayıcı oturumundan bağımsız çalışır. Baskı API'si kendi X-Agent-Key doğrulamasını sürdürür.
- `/health` oturumsuz yalnızca temel çalışma bilgisi verir; toplayıcı ayrıntıları için yönetici oturumu gerekir.

Mevcut yalnızca yerel bilgisayardan erişim ve geliştirme ortamı kısıtları devam eder. Şirket ağına dağıtım,
HTTPS ve AD geçişi ayrı dağıtım çalışmasıdır. Ortak hesabı bilen kişiler aynı yetkilere sahiptir.

Uygulama testleri gerçek çerezle giriş yapar; uygulama kodunda test için kimlik doğrulama atlaması bulunmaz.
LoginTests; doğrudan URL/CSV/canlı erişim engeli, hatalı şifre, doğru giriş, yerel dönüş adresi,
antiforgery, çıkış, giriş denemesi sınırı ve ajan API'sinin bağımsızlığını doğrular.
