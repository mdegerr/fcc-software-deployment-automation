# Sorun Giderme Rehberi

## Önce hangi kayıtlar kontrol edilmeli?

1. Ana ekrandaki aşama adı
2. Durum bildirimi
3. Oturuma özel ayrıntılı log
4. Ana `surumyakma.log`
5. Windows Aygıt Yöneticisi
6. Windows Ağ Bağlantıları
7. Moxa bağlantı ve kanal geri okuma bilgileri

## Uygulama sürüm listesini göstermiyor

Kontrol edin:

- Doğru platform seçilmiş mi?
- Sürüm deposu yolu doğru mu?
- Platform klasörü mevcut mu?
- Sürüm klasörünün içinde eksiksiz TEZI yapısı var mı?
- İsteğe bağlı paket ön adı doğrulaması gereksiz yere açık mı?

Başlatma düğmesinin pasif kalması çoğunlukla yüklenebilir TEZI yapısının bulunmadığını gösterir.

## Moxa bağlantısı kurulamıyor

Kontrol edin:

- Power Box ve Relay Box IP adresleri,
- PC’nin Moxa ağına bağlı Ethernet adaptörü,
- IP erişimi,
- slot ve kanal değerleri,
- Moxa cihazının başka uygulamada kilitli kalıp kalmadığı.

Bağlantının yeşil görünmesi kanal eşlemesinin doğru olduğunu tek başına garanti etmez; ilk saha kullanımında fiziksel çıkış doğrulaması yapılmalıdır.

## COM portu açılmıyor

Olası nedenler:

- Tera Term veya başka terminal portu kullanıyor.
- Yanlış UKB seçilmiş.
- USB/seri sürücüsü eksik.
- COM numarası başka bilgisayarda değişmiş.
- Kablo veya USB portu bağlantıyı kesiyor.

Bağlantı Ayarlarında yalnızca Windows’ta aktif görünen doğru COM seçilmelidir.

## OTG aygıtı bulunamıyor

- Kabloyu UKB tarafında çıkarıp doğrudan yeniden bağlayın.
- USB hub/uzatma kullanmayın.
- Farklı USB portu deneyin.
- Aygıt Yöneticisi’nde yeni USB aygıtının oluştuğunu kontrol edin.
- Uygulamanın yönlendirdiği Recovery NORMAL/REAL sırasını bozmayın.

Uygulama kontrollü OTG kurtarma penceresi gösterirse adımları belirtilen sırayla tamamlayın.

## USB-NCM ağı oluşmuyor

Kontrol edin:

- Easy Installer gerçekten açılmış mı?
- USB-NCM adaptörü Aygıt Yöneticisi’nde hatasız mı?
- Uygulama yönetici olarak mı çalışıyor?
- VPN/sanal adaptörler hedef seçimini etkiliyor mu?
- Aynı hedef IPv4 adresi eski bir USB-NCM adaptöründe kalmış mı?
- Kurumsal güvenlik politikası adaptör adresi değişimini engelliyor mu?

## HTTP sunucusu başlatılamıyor

Olası nedenler:

- Seçilen PC IP adresi herhangi bir aktif adaptöre ait değil.
- Port başka süreç tarafından kullanılıyor.
- Windows Defender Güvenlik Duvarı veya güvenlik yazılımı engelliyor.
- USB-NCM adaptörü henüz hazır değil.

Uygulama alternatif HTTP portlarını deneyebilir. Sorun devam ederse logdaki seçilen adaptör, IP ve port bilgisini kontrol edin.

## Easy Installer paketi görmüyor

Kontrol edin:

- HTTP health testi başarılı mı?
- mDNS sorgusu alındı mı?
- `image_list.json` ve `image.json` istekleri logda var mı?
- Paket staging klasörüne eksiksiz hazırlanmış mı?
- Windows güvenlik duvarı UDP 5353 ve yerel HTTP trafiğine izin veriyor mu?

## 12. aşamada OFP bekleniyor

Normal açılıştan seri veri gelmelidir. Uygulama seri sessizliği algıladığında portu yenileyip UKB’yi Recovery NORMAL durumunda bir kez kontrollü yeniden başlatır.

Hâlâ veri yoksa:

- Power kanalının fiziksel olarak UKB’yi beslediğini,
- Recovery kanalının gerçekten NORMAL olduğunu,
- doğru COM portunun seçildiğini,
- seri kablonun bağlı olduğunu

kontrol edin.

## OFP doğrulandı, Link Up görülmedi

Bu iki sonuç birbirinden ayrıdır:

- OFP doğrulaması yazılımın doğru sürümle açıldığını gösterir.
- Link Up fiziksel Ethernet bağlantısının hazır olduğunu gösterir.

Seri logda `eth0: Unable to connect to phy` görülüyorsa kablo, karşı cihaz, port, PHY veya enerji durumu kontrol edilmelidir. Uygulamanın Link Up üretmesi mümkün değildir; yalnızca UKB kernel mesajını doğrular.

## Uygulama yanıt vermiyor gibi görünüyor

- Konsolun yoğun teknik çıktı üretmesi arayüz çizimini geciktirebilir.
- Uygulamanın aşama ve durum bilgisini kontrol edin.
- Hemen Görev Yöneticisi’nden sonlandırmayın.
- İptal düğmesini kullanın.
- Kapatma sırasında düğmelerin pasifleşmesi güvenli kaynak temizliğinin sürdüğünü gösterir.

## Log paylaşım standardı

Teşhis için aşağıdakileri tek klasörde paylaşın:

- son oturuma ait ayrıntılı log,
- ana log,
- ekran görüntüsü,
- kısa test notu,
- platform,
- UKB numarası,
- COM portu,
- kaldığı aşama.

Yazılım paketinin içeriği gizliyse paket paylaşılmamalıdır.
