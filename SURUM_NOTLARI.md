# Sürüm Notları

Bu belge yayımlanan sürümlerdeki kullanıcıya ve saha güvenilirliğine etki eden değişiklikleri özetler.

## v1.0.3 — Güncel

### Saha bilgisayarı uyumluluğu

- Windows 10/11 bilgisayarları arasındaki COM ve USB-NCM farklılıklarına karşı otomatik donanım algılama geliştirildi.
- USB-NCM adaptörü değişebilen görünen ad yerine kalıcı PnP kimliğiyle takip edilir.
- Yeni adaptör, aynı hedef IP’nin sahibi ve daha önce doğrulanmış PnP kimliği önceliklendirilir.
- Yönetici yetkisi, seçili COM, USB-NCM IPv4 durumu, HTTP portu ve ağ rotaları checkpoint kayıtlarına eklendi.
- HTTP sunucusu için alternatif port denemeleri ve güvenlik duvarı teşhisleri güçlendirildi.

### Easy Installer ve paket keşfi

- TEZI feed adresleri oturuma özel mutlak URL olarak üretilir.
- Manifest yanıtlarında önbelleği engelleyen HTTP başlıkları kullanılır.
- Easy Installer feed’i görüp image manifestini istemezse kontrollü yenileme uygulanır.
- Seri konsol desteklenmeyen komuta `ERR FORMAT` döndürürse aynı komut tekrarlanmaz; resmi Zeroconf akışı kullanılır.
- Paket aktarımı HTTP istekleri, payload kanıtları ve kapanış mesajıyla ayrı ayrı doğrulanır.

### OTG ve Recovery güvenilirliği

- UUU aygıt zaman aşımında eski süreç tamamen sonlandırılır.
- Kullanıcı OTG çıkarma ve yeniden takma adımlarında ayrı pencerelerle yönlendirilir.
- OTG olmadan normal UKB açılışı seri konsoldan doğrulanmadan yeni Recovery denemesi başlatılmaz.
- Her tekrar denemede Windows USB aygıtları yeniden taranır ve yeni UUU oturumu oluşturulur.

### Kurulum sonrası doğrulama

- Kurulum sonunda Power OFF → Recovery NORMAL → Power ON sırası geri okumayla doğrulanır.
- UKB’nin güç kapalı boşalma beklemesi art arda yüklemelerde güvenilir açılış için uzatıldı.
- Normal açılışta seri veri gelmezse COM portu yenilenir ve UKB Recovery NORMAL durumunda bir kez kontrollü yeniden başlatılır.
- Beklenen ve okunan OFP sürümü karşılaştırılır.
- Fiziksel `eth0 Link is Up` sonucu OFP doğrulamasından ayrı raporlanır.
- `Unable to connect to phy` gibi fiziksel Ethernet hata kanıtları ayrıntılı loga eklenir.

### Arayüz ve işletilebilirlik

- Pencerenin görev çubuğundan kaybolması ve kapandıktan sonra prosesin açık kalması engellendi.
- İkinci uygulama örneği açıldığında mevcut pencere öne getirilir.
- Kapanışta tüm pencereyi soldurmak yerine düğmeler pasifleştirilir.
- Yüksek hacimli konsol çıktıları toplu çizilir; ayrıntılı log kaydı korunur.
- Teknik seri tanı satırları operatör konsolundan süzülür, dosya logunda tutulur.

## v1.0.2

- Türkçe ve İngilizce arayüz desteği eklendi.
- Ana ekran, ayarlar, uyarılar ve sonuç pencereleri iki dil için düzenlendi.
- UKB1–UKB6 hedef tablosu ve tekli hedef seçimi oluşturuldu.
- Masaüstü sürüm deposu ve platform klasörleri otomatik keşfedilir.
- Sürüm klasörleri doğal sürüm sırasıyla listelenir; en güncel sürüm varsayılan seçilir.
- OTG algılanmadığında kullanıcı yönlendirme ve kontrollü tekrar deneme akışı eklendi.
- Başarılı kurulum sonrası Recovery NORMAL, Power ON durumu ve OFP doğrulaması uygulandı.
- İşlem konsolu ve log yazımı arayüz akıcılığı için arka planda toplu işlenmeye başladı.

## v1.0.1

- Farklı saha bilgisayarlarında USB-NCM adaptörü ve HTTP sunucusu seçimi iyileştirildi.
- Alternatif HTTP portları eklendi.
- Yönetici yetkisi, Windows Defender ve ağ adaptörü sorunları için ayrıntılı teşhis kayıtları oluşturuldu.
- Uygulama dosya ve ürün sürümü v1.0.1 olarak yayımlandı.

## v1.0.0

- Ağ üzerinden TEZI paket aktarımı yapan ilk kararlı sürüm.
- Moxa Power/Recovery kontrolü, seri port izleme, USB-NCM, yerel HTTP, mDNS ve OFP doğrulaması tek akışta birleştirildi.
- Platform profilleri, sürüm seçimi ve UKB hedef yapılandırması eklendi.

## Yayınlama ilkesi

- Kaynak kod ve saha uygulaması birbirinden ayrı tutulur.
- Çalıştırılabilir saha paketi GitHub Releases bölümünde yayımlanır.
- Her saha paketi SHA256 değeriyle doğrulanır.
- Gerçek OFP/TEZI paketleri, loglar, kullanıcıya özel ayarlar, parolalar ve hassas saha verileri Git geçmişine eklenmez.
- Uygulama sürümü, Git etiketi ve Release başlığı aynı sürüm numarasını kullanır.
