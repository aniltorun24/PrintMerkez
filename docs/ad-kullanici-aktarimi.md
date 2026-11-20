# AD kullanıcı rehberi aktarımı

20.11.2026 tarihinde `trnavm.com` alanı mevcut Windows hesabıyla salt okunur sorgulandı.
613 AD kullanıcı nesnesinden 4 yerleşik/sistem hesabı hariç tutuldu. 608 yeni hesap ve mevcut 1 hesap eşleştirildi.
Uygulamada 609 AD hesabı (594 aktif / 15 pasif), 40 departman ve mevcut 11 lokasyon vardır.
Manager alanı tüm hesaplarda boştur; bu işlem yönetici rolü veya bildirim alıcısı atamaz.
Bu rehber aktarımı AD oturum açma entegrasyonu değildir; geçici admin girişi korunur.

## Eşleştirme kuralları

- Önce kalıcı AD objectGUID, ardından aynı alandaki tam normalize SAM/DOMAIN\\SAM hesabı eşleştirilir.
- Hesap çakışırsa veya mevcut AD kimliği başka bir nesneye aitse otomatik birleştirilmez.
- Yeni kayıtlar Çalışan bildirim rolüyle eklenir. Mevcut rol, yönetici ataması ve dolu manuel departman/lokasyon korunur.
- Ad, geçerli e-posta ve aktif/pasif bilgisi AD'den gelir. Eksik e-posta uydurulmaz; mevcut manuel e-posta silinmez.
- Departman alanı boşsa uygun Users altındaki OU'dan tamamlanır. Türkçe/ASCII karşılıkları bilinen adlarda birleştirilir.
- MOI, MOA, KORUPARK, TORIUM, SAMSUN BULVAR vb. açık OU/ofis eşleşmeleri mevcut lokasyonlara bağlanır.
  OU ile ofis çelişirse veya Hilton/Kavacık/Merkez gibi mevcut 11 lokasyonla doğrulanmış eşleşme yoksa boş kalır.
- 215 hesapta departman, 151 hesapta lokasyon, 201 hesapta geçerli e-posta eksiktir. Kaynak dosya bu bilgileri doldurmaya yeterli değildir.
- AD objectGUID, alan adı, SAM, DN, kaynak departman/ofis ve son aktarım zamanı ayrıca saklanır.
- Kısa hesap ile TRNAVM\\hesap aynı doğrulanmış AD kullanıcısına çözümlenir; yabancı alan adı aynı kişi sayılmaz.
  Günlük grafik ve aylık kullanım aynı kuralları kullanır. Baskı geçmişinin eski kişi/departman alanlarına yazılmaz.

## Uygulama ve tekrar aktarım

`scripts/export-directory.ps1` AD yönetim modülü olan bilgisayarda çalışır; AD'ye yazmaz ve parola toplamaz.
JSON yalnızca git/publish dışında kalan App_Data/directory-import altında tutulur. İçe aktarma web uç noktası değildir.
Bakım komutu `--Maintenance:ImportDirectoryFile <dosya> --Maintenance:DirectoryImportPreview true` ile ön kontrol verir.
`false` seçilirse önce veritabanı yedeklenir, tek işlem içinde kayıtlar ve denetim özeti yazılır.
Yinelenen GUID/hesap veya bozuk alan varsa işlem başlamadan reddedilir; yeniden aktarım yeni kopyalar üretmez.
AD'de dosyada bulunmayan yerel kullanıcılar silinmez. Zamanlanmış senkronizasyon bu aşamada açılmamıştır.

Kullanıcılar ekranı artık 50 kayıtlık sayfalama kullanır; eski 200 kayıt sınırı kaldırılmıştır.
Kullanıcı detayında kaynak AD departmanı/ofisi ve son aktarım görülebilir. Tüm hesapların uygulamaya giriş yetkisi yoktur.

172 otomatik test geçti: aktarım ön kontrolü, tekrar aktarım, sistem hesapları, çakışmalar, manuel alanların korunması,
Türkçe/kaçışlı DN, alan adı ayrımı, grafik toplamları, 200 üstü sayfalama ve önceki tüm iş akışları.
Gerçek arayüzde liste, son sayfa, mevcut hesabın geçmiş baskı grafiği ve toplayıcıların çalışması doğrulanır.
Sunucuda henüz uygulama veritabanı bulunmadığından, taşınacak güncel test paketi bu rehber verilerini içerir.
