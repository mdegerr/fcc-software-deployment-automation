# Sürüm Yükleme — Sürüm Notları

Bu dosya yayımlanan sürümler arasındaki işlevsel farkları kayıt altında tutar. Mevcut uygulama sürümü **v1.0.3** olarak korunmuştur.

## v1.0.3 — Güncel

- Easy Installer yeniden yüklenirken mevcut TEZI oturumunun HTTP isteği algılanırsa gereksiz UUU/Recovery işlemi iptal edilerek mevcut oturum güvenle kullanılır.
- Gerçek Recovery yeniden yüklemesinden sonra USB-NCM adresi tekrar doğrulanır; HTTP ve mDNS servisleri yeni adaptöre yeniden bağlanır.
- Windows USB-NCM adaptörü değişebilen arayüz adı yerine kalıcı PnP kimliğiyle eşleştirilir.
- Adaptör seçim sırası yeni adaptör, hedef IP sahibi ve doğrulanmış kalıcı PnP kimliği olacak şekilde sağlamlaştırıldı.
- Yükleme başlamadan yönetici yetkisi, USB-NCM IPv4 ağı, HTTP portu ve seçili COM görünürlüğü checkpoint loglarıyla denetlenir.
- COM portunun görünmemesi ağdan yüklemeyi gereksiz yere engellemez; ayrıntılı uyarı olarak kaydedilir.
- Windows 10/11 ve farklı saha bilgisayarlarındaki ağ/adaptör farklarına karşı regresyon kapsamı genişletildi.
- Küçültülen ana pencerenin görev çubuğundan kaybolması ve kapanış sırasında gizli proses olarak kalması engellendi.
- İkinci uygulama açılışı mevcut pencereyi geri getirir; güvenli kapanış toplam 20 saniyelik üst süreyle sınırlandırılır.
- Kapanış sırasında tüm pencerenin solması kaldırıldı; yalnız düğmeler pasifleşir ve ilerleme çubuğu güvenli kapanışı hareketli olarak gösterir.
- Easy Installer feed içindeki `image.json` adresi göreli yol yerine sunucu IP/portunu içeren mutlak ve her oturuma özel URL olarak yayımlanır.
- HTTP JSON yanıtlarına `no-store`, `no-cache`, `must-revalidate`, `Pragma` ve `Expires` başlıkları eklenerek farklı Easy Installer önbellek davranışları sınırlandırıldı.
- `image_list.json` alındığı hâlde `image.json` istenmezse uygulama VNC penceresi açmadan üç kontrollü ağ listesi yenilemesi yapar; başarısızlık nedeni ayrı checkpoint’lerle kaydedilir.
- Seri konsol `ERR FORMAT` döndürürse desteklenmeyen kabuk komutu tekrar gönderilmez; akış doğrudan resmi Zeroconf yöntemine geçer.
- UUU, OTG recovery aygıtını 20 saniye içinde göremezse mevcut UUU/libusb süreci tamamen sonlandırılır; aynı süreç içinde kör güç çevrimi yapılmaz.
- Kullanıcı iki ayrı popup ile yönlendirilir: önce OTG yalnızca çıkarılır; UKB OTG olmadan Recovery NORMAL durumunda açılır ve seri konsoldan normal OFP açılışı doğrulanır.
- Normal açılış doğrulandıktan sonra UKB tekrar kapatılır, Recovery REAL hazırlanır ve ikinci popup ile OTG yeniden taktırılır.
- OTG yeniden takıldıktan sonra Windows PnP aygıt ağacı yeniden taranır ve her denemede tamamen yeni bir UUU süreci başlatılır.
- Temiz OTG kurtarma sırası otomatik testte `Power OFF → Recovery NORMAL → kablo çıkar → NORMAL boot doğrula → Power OFF → Recovery REAL → kablo tak → Power ON → PnP tarama → yeni UUU` olarak sabitlendi.
- 33 otomatik regresyon ve sıralı UKB ağ yaşam döngüsü simülasyonu başarıyla tamamlandı.

## v1.0.2

- Türkçe ve İngilizce arayüz seçimi eklendi; seçilen dil Settings.json profiline kaydedilir.
- İngilizce seçildiğinde kullanıcı mesajları, işlem durumu ve log mesajları İngilizce üretilir.
- İngilizce arayüzde kalan karışık Türkçe/İngilizce metinler giderildi; kelime içi hatalı çeviri engellendi.
- Ana ekran, bağlantı ayarları, gelişmiş seçenekler ve OTG/hata/başarı popup metinleri İngilizce regresyon testine bağlandı.
- Dil değiştirildiğinde platformdaki **(Varsayılan)** eki İngilizce görünümde **(Default)** olarak yenilenir.
- Ana işlem düğmesinin İngilizce metni dinamik UKB hedef seçimiyle birlikte korunur.
- Ana ekran, UKB ayarları ve gelişmiş seçenekler için üç doğrulanmış İngilizce görsel README dosyasına eklendi.
- OTG kablosu algılanmadığında işlem kullanıcıya modal uyarı verir. Kablo takılıp kullanıcı **Tamam** demeden sonraki Recovery denemesi başlamaz.
- TUK saha kaydında görülen USB geri besleme/Recovery zamanlama durumu için Power OFF bekleme, Recovery NORMAL→REAL yeniden hazırlama ve UKB tarafında OTG çıkar-tak yönlendirmesi eklendi.
- OTG aygıtı bekleme ve USB bulk timeout tekrarları aynı doğrulanmış güvenli güç/Recovery sırasını kullanır.
- Başarılı sürüm yüklemesinden sonra Recovery **NORMAL**, UKS POWER **ON** bırakılır.
- Sonuç penceresine renkli LINK IS UP durumu eklendi:
  - LINK görüldü: yeşil.
  - LINK görülmedi: kırmızı uyarı; OFP doğrulandıysa yükleme yine başarılı kabul edilir.
- Masaüstü\\UKB Sürümleri kök yolu Bağlantı Ayarları > Gelişmiş Seçenekler bölümünden değiştirilebilir.
- Dil ve sürüm deposu yolu platform profiline kalıcı olarak kaydedilir.
- USB Bulk(W) / Bulk(R) zaman aşımı seçici yeniden deneme ve güvenli Moxa çevrimi korunmuştur.
- İşlem konsolu satır bazlı çizim yerine 125 ms aralıklarla toplu çizime geçirildi; ekrandaki geçmiş sınırlandırılırken tam kayıtlar log dosyalarında korunur.
- Seri, UUU ve HTTP teknik çıktılarındaki gereksiz çift dil çevirisi kaldırıldı.
- Log dosyası yazımı UI thread’inden ayrılarak 100 ms’lik toplu arka plan yazımına geçirildi.
- TR/EN geçişinde tek kare yeniden çizim ve yalnızca açıkken Yardım içeriği yenileme uygulanarak geçiş donması azaltıldı.
- TEZI HTTP aktarım tamponu 512 KB, TCP gönderim tamponu 1 MB olarak optimize edildi.
- 12.000 konsol satırı stres testi ve üç ardışık tam regresyon turu başarıyla tamamlandı.- Yeni davranışlar için regresyon testleri eklenmiştir.

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
- GitHub etiketi ve sürüm başlığı uygulama sürümüyle aynı olmalıdır (örnek: v1.0.3).
