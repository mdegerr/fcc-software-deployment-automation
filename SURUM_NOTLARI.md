# Sürüm Yükleme — Sürüm Notları

Bu dosya yayımlanan sürümler arasındaki işlevsel farkları kayıt altında tutar. Mevcut uygulama sürümü **v1.0.2** olarak korunmuştur.

## v1.0.2 — Güncel

- Türkçe ve İngilizce arayüz seçimi eklendi; seçilen dil Settings.json profiline kaydedilir.
- İngilizce seçildiğinde kullanıcı mesajları, işlem durumu ve log mesajları İngilizce üretilir.
- OTG kablosu algılanmadığında işlem kullanıcıya modal uyarı verir. Kablo takılıp kullanıcı **Tamam** demeden sonraki Recovery denemesi başlamaz.
- Başarılı sürüm yüklemesinden sonra Recovery **NORMAL**, UKS POWER **ON** bırakılır.
- Sonuç penceresine renkli LINK IS UP durumu eklendi:
  - LINK görüldü: yeşil.
  - LINK görülmedi: kırmızı uyarı; OFP doğrulandıysa yükleme yine başarılı kabul edilir.
- Masaüstü\\UKB Sürümleri kök yolu Bağlantı Ayarları > Gelişmiş Seçenekler bölümünden değiştirilebilir.
- Dil ve sürüm deposu yolu platform profiline kalıcı olarak kaydedilir.
- USB Bulk(W) / Bulk(R) zaman aşımı seçici yeniden deneme ve güvenli Moxa çevrimi korunmuştur.
- Yeni davranışlar için regresyon testleri eklenmiştir.

## v1.0.1

- Farklı saha bilgisayarlarında USB-NCM/HTTP sunucusu kurulumu için ağ bağdaştırıcısı ve port seçimi iyileştirildi.
- Yönetici yetkisi, Windows Defender ve bağdaştırıcı sorunlarının teşhisi ayrıntılı checkpoint loglarına eklendi.
- Dosya adı, ürün adı ve sürüm bilgileri v1.0.1 olarak yayımlandı.

## v1.0.0

- Ağdan TEZI paket aktarımı yapan ilk kararlı sürüm.
- Moxa Power/Recovery kontrolü, seri port izleme, USB-NCM, yerel HTTP sunucusu, OFP doğrulama ve işlem logları tek akışta birleştirildi.
- UKB1–UKB6 hedef yapılandırması, platform profilleri ve sürüm seçimi eklendi.

## Yayınlama kuralı

- Çalıştırılabilir uygulama, kaynak kod ve bu sürüm notu birlikte arşivlenir.
- logs, gerçek sürüm paketleri, kurum içi IP/kanal değerleri ve hassas Settings.json içerikleri herkese açık GitHub deposuna yüklenmez.
- GitHub etiketi ve sürüm başlığı uygulama sürümüyle aynı olmalıdır (örnek: v1.0.2).
