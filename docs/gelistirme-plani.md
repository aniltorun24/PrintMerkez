# Geliştirme kapsamı ve kararlar

## Bilinen bilgiler

11 lokasyon var; kullanıcı ve yazıcı adetleri bilinmiyor. Canon, Kyocera ve Olivetti cihazlar var. Mevcut kullanıcılar doğrudan IP üzerinden yazdırıyor. Kurumda AD ve Windows Server mevcut. SRV-PRINT ayrı ekip tarafından test amaçlı kurulacak. Mailde 2 vCPU, 4 GB RAM (gerektiğinde 8), 80 GB sistem ve 40 GB kuyruk diski istenmiş. Windows Server 2022/2025 seçimi ve IP kesin değil; 10.60.4.40 öneri. Yazılım hiçbir sunucuyu kurmaz veya mevcut cihaz/ağ yapılandırmasını değiştirmez.

## Teknoloji kararı

API ve web: ASP.NET Core .NET 10, Razor Pages. Agent: .NET Worker. Veri erişimi: EF Core. Yerel veri: SQLite. Merkezi üretim veritabanı henüz seçilmedi. Mimari ilk aşamada modüler tek web uygulaması + ayrı ajan olarak tutuldu. Blazor, NestJS ve Next.js kullanılmıyor.

## Hedef kapsam

Envanter, merkezi hareketler, kullanıcı/departman, maliyet, raporlar, yetki/kota, cihaz sağlığı, güvenli çıktı ve desteklenen cihazlarda MFP operasyonları. Özelliklerin kapsamda olması cihaz desteğinin doğrulandığı anlamına gelmez.

## İlk adımın kabulü

- Panel ve envanter sunucu olmadan açılır.
- Lokasyon ve yazıcı eklenir/düzenlenir ve yeniden başlatmada korunur.
- Olay API'si geçerli anahtar ve kayıtlı yazıcı ister.
- Aynı olay tekrar geldiğinde ikinci kayıt oluşmaz.
- Bilinmeyen metadata açıkça görünür; simülasyon gerçek veri gibi sunulmaz.
- Baskı geçmişi filtrelenir ve CSV alınır.

## Sonraki karar noktaları

AD grup adları, toplam yük, Windows sunucu sürümü, kuyruk/sürücü bilgileri, gerçek olay örnekleri, kesinti sırasında izin/engelleme politikası ve cihaz SDK erişimi pilotta netleşecek. Bu veriler olmadan üretim kapasitesi, fiziksel çıktı doğruluğu ve güvenli çıktı desteği taahhüt edilmeyecek.

## Lokasyon listesi

5. Levent, Bulvar Samsun AVM, Forum Kayseri, Forum Mersin, Korupark AVM, Mall of Antalya, Mall of İstanbul, Sera Kütahya AVM, Torium AVM, Torun Center, Zafer Plaza.
