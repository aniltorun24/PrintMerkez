# Aylık baskı eşiği ve yönetici bildirimi

Bu özellik kullanıcı isteğiyle eklendi. Seçilen dönem aylık toplamdır. Çalışan/Yönetici rolleri organizasyon ve bildirim yönlendirmesi içindir; AD oturum açma veya uygulama erişim yetkisi sağlamaz. Production başlatma engeli ve yerel erişim sınırı korunur.

## Kullanım

1. Kullanıcılar ekranında çalışanların departman ve lokasyonunu seçin.
2. Bildirimi alacak kullanıcıya Yönetici rolü verin; departman, lokasyon ve geçerli e-posta adresini girin.
3. Yönetici atamaları ekranında departman + lokasyon için bu yöneticiyi seçin. Aynı birleşim için tek atama tutulur. Aktif ataması olan yöneticinin rolü veya organizasyonu değiştirilmeden önce atama kapatılmalı/değiştirilmeli.
4. Baskı uyarıları > Aylık uyarı kuralları bölümünde ad, eşik, kaynak ve aktiflik tanımlayın. Kaynağı Windows olan kurallar simülasyonları saymaz.
5. Yeni baskı olayları otomatik değerlendirilir. Daha önce gelen işler için Mevcut ayı değerlendir düğmesini kullanın. Hesabı henüz kullanıcı dizininde olmayan işler kaydedilmeye devam eder; kullanıcı eşleştirildikten sonra mevcut ay yeniden taranabilir.
6. Mail ve durum bağlantısında alıcıyı, metni, gönderim durumunu ve hata nedenini inceleyin. Örneklerin hepsi Simulation ve .invalid adreslidir.

## Hesaplama ve tekrar kontrolü

Türkiye saatine göre takvim ayı kullanılır. Eşik 500 ise 500'de uyarı oluşmaz; 501 ve üzerinde oluşur. Sadece QueueCompleted ve baskı adedi bilinen işler sayılır. Renk bilinmese de adet biliniyorsa sayıma dahildir. İptal/hata/bilinmeyen durum ve bilinmeyen adet sayılmaz. Impressions zaten kopyaları içerdiği için Copies ile tekrar çarpılmaz. Bu sayılar fiziksel çıktı teyidi değildir.

Tüm yazıcılardaki kullanıcı toplamı değerlendirilir. Kullanıcının bağlı olduğu lokasyon yönetici seçimini belirler; yazıcının lokasyonundan yönetici tahmin edilmez. Kullanıcının organizasyonu kuyruk oluşumundan sonra değişirse yanlış kapsama otomatik gönderim yapılmaz ve kayıt bekletilir. Alıcı yöneticinin aktifliği, rolü, organizasyonu ve e-postası gönderimden hemen önce yeniden kontrol edilir. Kendisine bildirim gönderilmez.

PolicyId + UserId + PeriodKey + Source benzersizdir. Aynı ay/kural/kullanıcı/kaynak için bir uyarı oluşturulur. Farklı kurallar bağımsızdır. Kural eşiği değiştirilirse daha önce oluşmuş uyarının eşik ve toplam anlık görüntüsü korunur; aynı ayda ikinci uyarı oluşturulmaz. Kural kapatılırsa henüz gönderilmemiş uyarı iptal edilir. Bildirim oluşmuş kuralın kaynağı değiştirilemez; yeni kural eklenmelidir. Geç gelen olay kendi olay ayına yazılır.

Baskı kaydı ve yeni uyarı aynı veritabanı işleminde kaydedilir. Outbox yazımı başarısız olursa iş de geri alınır; ajan aynı olayla tekrar deneyebilir. Mail sunucusu arızası bu işlemin dışında ele alınır ve kaydedilmiş baskıyı geri almaz. Önceki kayıtların hesap metinleri C# kurallarıyla yükseltmede normalleştirilir; yeni işlerde eşleşmiş kullanıcı kimliği de saklanır.

## Gönderim durumları

- Kuyrukta: işlenmeyi bekler.
- Eşleştirme bekliyor: kullanıcı organizasyonu veya yönetici/e-posta eksik/uyuşmuyor. Yaklaşık bir dakika sonra kontrol edilir; Kuyruğu kontrol et düğmesi beklemeyi öne alır.
- Önizleme / mail gönderilmedi: gerçek gönderim yapılmamıştır. Mail modu Preview olduğunda ve her simülasyonda bu sonuç oluşur. Bu kayıtlar SMTP açılınca topluca yeniden gönderilmez.
- SMTP sunucusu kabul etti: SMTP kabul yanıtı alınmıştır; alıcının okuduğu veya nihai posta kutusuna teslim edildiği garanti edilmez.
- Tekrar denenecek: henüz mail gönderilmeden bağlantı kurulamadı veya SMTP açıkça geçici ret verdi. Artan beklemeyle en fazla beş kez denenir.
- Gönderilemedi: kesin başarısızlık/yapılandırma sorunu. Düzeltmeden sonra Yeniden dene kullanılabilir; işlem denetim kaydına yazılır.
- Teslim durumu belirsiz: gönderim sırasında bağlantı kesildi veya yarım kalan bir gönderim tespit edildi. Olası çift maili önlemek için otomatik/tek tık yeniden gönderim yoktur; SMTP kayıtlarıyla incelemek gerekir.
- İptal edildi: kural kapatıldı veya kullanıcı işlemiyle iptal edildi. Devam eden veya tamamlanmış gönderim iptal edilemez. İptal işlemi denetim kaydına yazılır.

İşleyici yaklaşık 10 saniyede bir en fazla 10 kayıt alır. Her kayıt koşullu veritabanı güncellemesiyle sahiplenilir; iki işleyici aynı kaydı eşzamanlı gönderemez. Beş dakikadan uzun Sending kayıtları belirsiz duruma alınır, körlemesine yeniden gönderilmez. SMTP ağ işlemleri 20 saniyelik üst süreyle sınırlanır. Uygulama durduğunda kuyruk veritabanında kalır. Web uygulaması/IIS durdurulursa gönderim de durur; canlı işletimde sürekli çalışan barındırma veya ayrı worker planlanmalıdır.

## SMTP yapılandırması

Varsayılan Mail:Mode=Preview'dır; dışarı gönderim yapılmaz. Simülasyonlar SMTP modu açıkken de kesinlikle dışarı gönderilmez. .invalid örnek adreslerine gerçek gönderim engellenir.

Şirketten şu bilgiler alınmalı: SMTP sunucu adı, port, StartTls veya SslOnConnect, izinli gönderen adresi, gerekliyse servis hesabı/kimlik doğrulama yöntemi ve relay izinleri. Mevcut adaptör SMTP kullanıcı/parola veya kurumca izinli relay kullanır. OAuth2 zorunluysa ayrıca entegrasyon gerekir.

Yapılandırma anahtarları:

~~~text
Mail:Mode = Smtp
Mail:Host = şirketin vereceği sunucu
Mail:Port = şirketin vereceği port
Mail:Security = StartTls veya SslOnConnect
Mail:From = izinli gönderen adresi
Mail:Username = gerekiyorsa servis hesabı
Mail:Password = gizli yapılandırmadan
Notifications:WorkerEnabled = true
~~~

Ortam değişkenlerinde : yerine __ kullanılır; örneğin Mail__Password. Parolayı kaynak koda veya Git'e eklemeyin. TLS 1.2/1.3 kullanılır, sertifika doğrulaması atlanmaz, STARTTLS yoksa şifresiz gönderime düşülmez. Gerçek SMTP ve izinli pilot posta kutusuna teslim testi şirket bilgileri geldikten sonra yapılacaktır. Bu geliştirmede şirket adreslerine mail gönderilmedi.

## Örnekler ve yükseltme

Geliştirme örnek verisi açıksa bildirim örnekleri bir kez eklenir: mevcut DEMO çalışanlara örnek lokasyon, aynı departman/lokasyonda .invalid adresli örnek yöneticiler ve simülasyon için aylık 20 yüzlük kural. Örnek aktif çalışanlara mevcut ayda 15 ve 10 yüzlük iki sentetik olay eklenir. Eski baskılar korunur. Bu olaylar gerçek baskı değildir ve SMTP açılmış olsa da dışarı gönderilmez.

UsageManagerNotifications migration mevcut kayıtları koruyarak yeni alan ve tabloları ekler. Yerel bootstrap önce yedek alır. SQLite, ilişki değişiklikleri için bazı tabloları yeniden oluşturabilir; yükseltme sırasında uygulamayı zorla kapatmayın. Merkezi üretim sağlayıcısı seçilmeden canlı dağıtım yapılmaz.

## Doğrulama

59 otomatik test: önceki 31 regresyon kontrolü; eşik eşitliği/aşımı, kopya hesabı, kaynak ayrımı, iptal ve eksik adet, İstanbul ay sınırı, tekrar ve eşzamanlı baskılar, eksik/yanlış yönetici, e-posta/rol/lokasyon, organizasyon değişimi, gönderim öncesi alıcı doğrulama, kuyruk sahiplenme, güvenli tekrar deneme sınırı, belirsiz teslim, kapatılan kural, atomik outbox, mevcut ay taraması, formlar/CSRF, denetimli iptal ve tekrar, örnek verinin tekrar yüklenmemesi ve yerel SMTP sunucusunda STARTTLS zorunluluğu. Başarı/başarısız gönderim sonuçları sahte göndericiyle test edilir; gerçek şirket SMTP teslimi henüz doğrulanmamıştır.

Teknik kaynaklar: [MailKit](https://mimekit.net/docs/html/T_MailKit_Net_Smtp_SmtpClient.htm), [ASP.NET Core arka plan servisleri](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0).
