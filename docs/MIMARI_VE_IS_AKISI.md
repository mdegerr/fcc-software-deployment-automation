# Mimari ve İşlem Akışı

## Genel bakış

Sürüm Yükleme, Windows saha bilgisayarı ile UKB arasında çalışan bir orkestrasyon uygulamasıdır. Uygulama tek başına yeni bir uçuş yazılımı üretmez; yetkili ekip tarafından hazırlanmış eksiksiz TEZI paketini seçilen UKB’ye güvenli ve izlenebilir bir sırayla aktarır.

## Sistem bileşenleri

| Bileşen | Görevi |
|---|---|
| Windows Forms arayüzü | Platform, sürüm ve UKB seçimi; ilerleme, uyarı ve kullanıcı yönlendirmesi |
| AppConfig / Settings profili | Bilgisayara ve platforma özgü bağlantı değerlerinin yönetimi |
| VersionManager | Sürüm klasörlerinin bulunması, sıralanması ve TEZI paket yapısının doğrulanması |
| MoxaController | Power ve Recovery çıkışlarının okunması, yazılması, geri doğrulanması ve bağlantının canlı tutulması |
| UkbSerialMonitor | Easy Installer, kapanış, OFP sürümü ve Link Up mesajlarının seri porttan izlenmesi |
| HardwareAutoConfigurator | COM ve USB-NCM adaylarının belirlenmesi, Windows ağ hazırlığı ve ortam kontrolleri |
| TeziHttpServer | Seçilen TEZI paketinin UKB’ye yerel HTTP üzerinden sunulması |
| TeziMdnsAdvertiser | Easy Installer’ın paketi bulabilmesi için DNS-SD/mDNS duyurusu |
| UUU/Recovery araçları | Toradex Easy Installer ortamının OTG üzerinden RAM’de başlatılması |
| Logger / ExpertDiagnostics | Zaman damgalı log, checkpoint ve hata anı bilgisayar teşhisleri |

## İletişim kanalları

### Moxa Ethernet

Power ve Recovery hatları Moxa dijital çıkışları üzerinden yönetilir. Her yazma işleminden sonra aynı kanal geri okunur. Uygulama yalnızca arayüzde seçilen UKB profilinin kanal eşlemesini kullanır.

### Seri konsol

Seri kanal, hedefin hangi ortamda açıldığını ve işlemin gerçek durumunu doğrulayan bağımsız gözlem kanalıdır. Başlıca kanıtlar:

- Easy Installer başlangıcı,
- hedef root/prompt durumu,
- paket kurulumunun sonunda kapanış mesajı,
- normal açılışta OFP Version,
- fiziksel Ethernet Link Up veya PHY hata satırı.

### USB/OTG ve UUU

Recovery REAL durumunda UUU, Easy Installer imajını UKB’nin RAM ortamında başlatır. Bu işlem normal OFP yazılımını doğrudan çalıştırmaz; kurulum için geçici Easy Installer ortamını hazırlar.

### USB-NCM

Easy Installer başladıktan sonra PC ile UKB arasında sanal Ethernet bağlantısı oluşur. Uygulama:

- uygun adaptörü belirler,
- hedefe uygun IPv4 adresini hazırlar,
- hedef rotasını yönetir,
- erişimi doğrular,
- işlem sonunda yalnızca kendisinin yönettiği rotayı temizler.

### HTTP ve DNS-SD/mDNS

TEZI paketi PC’deki yerel HTTP sunucusundan sağlanır. Easy Installer, DNS-SD/mDNS duyurusu üzerinden feed bilgisini keşfeder. Uygulama paket manifesti ve dosya isteklerini ayrı checkpoint’lerle takip eder.

## 12 aşamalı otomasyon

| Aşama | İşlem | Başarı kanıtı |
|---:|---|---|
| 1 | Sürüm paketi hazırlanır | Geçerli TEZI yapısı, staging kopyası ve beklenen OFP sürümü |
| 2 | Donanım ve güvenli çıkışlar kontrol edilir | Moxa bağlantıları, yönetici yetkisi ve seçili hedef bilgileri |
| 3 | Recovery ve UKB gücü hazırlanır | Power OFF ve Recovery REAL geri okuması |
| 4 | Easy Installer USB üzerinden yüklenir | UUU sürecinin başarıyla tamamlanması |
| 5 | USB-NCM sanal ağı oluşturulur | Doğru adaptör, IPv4 adresi ve hedef erişimi |
| 6 | Yerel sürüm sunucusu başlatılır | HTTP dinleme adresi ve portunun hazır olması |
| 7 | Easy Installer doğrulanır | Seri konsol veya mDNS sorgu kanıtı |
| 8 | UKB’den PC’ye erişim test edilir | Easy Installer tarafından alınan HTTP health yanıtı |
| 9 | Sürüm paketi Easy Installer’a tanıtılır | Feed ve image manifest istekleri |
| 10 | Sürüm dosyaları aktarılır ve kurulur | Payload istekleri ve hedef kapanış mesajı |
| 11 | Recovery NORMAL yapılır, UKB açılır | Power/Recovery geri okuması ve normal boot başlangıcı |
| 12 | OFP ve ağ sonucu doğrulanır | Beklenen OFP Version; varsa fiziksel eth0 Link is Up |

## Paket hazırlama

Kaynak paket doğrudan değiştirilmez. Uygulama:

1. seçilen sürümü uygulamaya özel geçici çalışma alanına kopyalar,
2. TEZI manifest yapısını kontrol eder,
3. otomatik kurulum için gerekli geçici manifest düzenlemelerini staging kopyasında yapar,
4. beklenen OFP sürümünü paketten çıkarır,
5. HTTP sunucusuna yalnızca staging kopyasını açar.

Kaynak sürüm klasörü bu işlemden etkilenmez.

## Hata yönetimi

### Zaman aşımı ve tekrar deneme

USB, UUU, Moxa, HTTP, mDNS ve seri doğrulamalarının ayrı zaman aşımı sınırları vardır. Tekrar deneme yalnızca güvenli kabul edilen aşamalarda yapılır.

### OTG kurtarma sırası

OTG aygıtı algılanamazsa kullanıcı yönlendirilir. Kontrollü kurtarma genel olarak:

1. UUU oturumunu sonlandırma,
2. UKB Power OFF,
3. Recovery NORMAL,
4. OTG çıkarma,
5. UKB’yi OTG olmadan normal açarak seri konsoldan doğrulama,
6. UKB’yi kapatma,
7. Recovery REAL,
8. OTG yeniden takma,
9. Windows USB aygıtlarını yeniden tarama,
10. yeni UUU oturumu

sırasını kullanır.

### Kurulum sonrası seri sessizlik kurtarması

Hedef yeniden açıldıktan sonra seri veri gelmezse uygulama uzun süre pasif beklemek yerine:

- seri portu yeniler,
- UKB’yi Recovery NORMAL durumunda kontrollü yeniden başlatır,
- seri aktiviteyi yeniden doğrular.

Bu kurtarma yalnızca kurulum tamamlandıktan sonraki normal açılış doğrulamasında uygulanır.

### İptal ve kapatma

İptal/kapatma isteğinde uygulama:

- aktif servisleri durdurur,
- yönettiği geçici rotayı temizler,
- mümkünse Power/Recovery çıkışlarını güvenli duruma getirir,
- seri ve Moxa kaynaklarını kapatır,
- işlemin hangi aşamada durduğunu loga yazar.

## İşlem sonunda hedef durumu

Başarılı yükleme sonunda:

- Recovery **NORMAL**,
- UKB Power **ON**,
- beklenen OFP sürümü doğrulanmış

olmalıdır.

Link Up kontrolü, yazılım sürüm doğrulamasından ayrı bir fiziksel ağ sonucudur. `Unable to connect to phy` veya Link Up zaman aşımı; kablo, karşı cihaz, PHY veya ağ portu durumunun ayrıca incelenmesi gerektiğini gösterir.

## İzlenebilirlik modeli

Arayüz konsolu operatör için sadeleştirilmiş mesajları gösterir. Ayrıntılı dosya logu ise:

- checkpoint kimliğini,
- START/SUCCESS/FAILED/RETRY/WARNING durumunu,
- geçen süreyi,
- hedef UKB ve bağlantı bilgilerini,
- seri/HTTP/mDNS kanıtını,
- hata türü ve mesajını

korur.

Yüksek hacimli düşük seviye seri tanı satırları arayüzde gizlenebilir; ayrıntılı log dosyasında saklanmaya devam eder.

## Tasarım sınırları

- Uygulama aynı anda paralel birden fazla UKB yüklemez.
- Her yükleme oturumu tek bir seçili UKB’ye aittir.
- Uygulama uçuş yazılımını derlemez veya değiştirmez.
- Paket içeriğinin işlevsel/onay durumu uygulamanın değil, yetkili paketleme sürecinin sorumluluğundadır.
- Fiziksel donanım arızaları yazılımla kesin olarak giderilemez; uygulama kanıt toplayıp güvenli tekrar deneyebilir.
