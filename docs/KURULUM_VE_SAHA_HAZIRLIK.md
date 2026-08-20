# Kurulum ve Saha Hazırlık Rehberi

Bu rehber, Sürüm Yükleme uygulamasının yeni bir Windows 10/11 saha bilgisayarında güvenli şekilde kullanılabilmesi için gereken ön koşulları ve ilk kontrol adımlarını açıklar.

## 1. Dağıtım paketini edinme

Uygulama yalnızca projenin GitHub **Releases** bölümündeki doğrulanmış Windows saha paketinden kurulmalıdır.

1. İlgili sürümün ZIP paketini indirin.
2. ZIP paketini yerel bir klasöre tamamen çıkarın.
3. EXE’yi ZIP içinden doğrudan çalıştırmayın.
4. Paket içindeki SHA256 değerini Release sayfasındaki değerle karşılaştırın.
5. Uygulama klasörünün yazılabilir olduğundan emin olun; log ve yerel ayarlar bu klasörde tutulur.

## 2. Bilgisayar gereksinimleri

### İşletim sistemi

- Windows 10 veya Windows 11
- Yönetici yetkisine sahip kullanıcı hesabı
- Güncel USB, seri port ve ağ bağdaştırıcısı sürücüleri

Uygulama .NET çalışma zamanını kendi paketi içinde taşır; ayrıca .NET kurulması beklenmez.

### Donanım bağlantıları

- PC ile Moxa ağı arasında çalışan Ethernet bağlantısı
- PC ile UKB seri konsolu arasında USB/seri bağlantı
- PC ile UKB Recovery/OTG portu arasında veri aktarımını destekleyen USB kablo
- Gerekliyse UKB’nin fiziksel Ethernet bağlantısı

OTG bağlantısında USB hub ve uzatma kablosu kullanılmaması önerilir.

### Windows sürücüleri

Windows Aygıt Yöneticisi’nde aşağıdaki aygıtların hatasız görünmesi gerekir:

- UKB seri konsoluna karşılık gelen COM portu
- Recovery sırasında oluşan USB/UUU aygıtı
- Easy Installer başladıktan sonra oluşan USB-NCM ağ bağdaştırıcısı

Sarı ünlem bulunan veya sürekli bağlanıp ayrılan aygıtlarla yükleme başlatılmamalıdır.

## 3. Windows güvenlik ve ağ hazırlığı

Uygulama, yükleme sırasında yerel HTTP sunucusu ve DNS-SD/mDNS duyurusu çalıştırır. Bu nedenle:

- Uygulama yönetici olarak çalıştırılmalıdır.
- Windows Defender Güvenlik Duvarı uyarısı gelirse uygulamaya güvenilen saha ağında izin verilmelidir.
- Kurumsal güvenlik yazılımı uygulamanın yerel port açmasını veya USB-NCM adaptörüne adres vermesini engellememelidir.
- Moxa IP adreslerine erişim sağlayan Ethernet adaptörü etkin olmalıdır.
- VPN, sanal makine adaptörü veya başka USB-NCM aygıtları hedef adaptör seçimini etkileyebileceği için gereksiz olanlar kapatılmalıdır.

## 4. Sürüm deposu yapısı

Varsayılan sürüm deposu kullanıcının masaüstündeki `UKB Sürümleri` klasörüdür.

Önerilen yapı:

```text
Masaüstü
└── UKB Sürümleri
    └── PLATFORM_ADI
        ├── UKB_11.0.12.32
        │   └── tam TEZI paket klasörü
        └── UKB_11.0.12.31
            └── tam TEZI paket klasörü
```

Uygulama platform klasörünün altındaki sürüm klasörlerini listeler. Bir sürümün seçilebilir ve yüklenebilir olması için içinde geçerli, eksiksiz TEZI yapısı bulunmalıdır.

Gerçek yazılım paketleri Git deposuna veya uygulama dağıtım paketine eklenmemelidir.

## 5. İlk çalıştırma kontrol listesi

Uygulamayı açtıktan sonra aşağıdaki sırayı izleyin:

- [ ] Üst bölümde doğru platform seçili.
- [ ] Sürüm listesinde yüklemek istediğiniz klasör görünüyor.
- [ ] Yüklenecek hedef UKB doğru seçilmiş.
- [ ] Bağlantı Ayarları içindeki COM portu Windows’ta görünen portla aynı.
- [ ] Baud rate doğru; standart değer çoğu ortamda 115200.
- [ ] Power Box ve Relay Box IP adresleri doğru.
- [ ] Seçili UKB’nin Power MOD/kanal bilgileri doğru.
- [ ] Seçili UKB’nin Recovery MOD/kanal bilgileri doğru.
- [ ] UKB hedef IP ve HTTP sunucu IP ayarları saha ağıyla uyumlu.
- [ ] Uygulama Moxa bağlantılarını başarılı gösteriyor.
- [ ] Tera Term veya başka bir seri terminal kapalı.
- [ ] OTG kablosu doğru UKB’ye bağlı.
- [ ] İşlem konsolunda kritik bir uyarı bulunmuyor.

Bu kontroller tamamlanmadan yükleme başlatılmamalıdır.

## 6. Settings.json davranışı

Bağlantı Ayarları ekranında kaydedilen bilgisayar ve platform profilleri `Settings.json` dosyasında tutulur.

- Dosya uygulama klasöründe oluşturulur/güncellenir.
- COM, Moxa, slot, kanal ve platform eşlemeleri bilgisayara göre değişebilir.
- Başka bilgisayara taşınan `Settings.json` körlemesine kullanılmamalıdır.
- Parola veya hassas saha bilgisi içeren dosyalar GitHub’a yüklenmemelidir.
- Ayarlar değiştirildikten sonra **Kaydet ve Bağlan** işlemi uygulanmalıdır.

## 7. Yükleme sırasında

- UKB, Moxa, Ethernet, seri ve OTG kablolarına gereksiz müdahale etmeyin.
- Uygulama açıkken aynı COM portunu başka programla açmayın.
- Uygulamanın yönlendirdiği OTG çıkar/tak adımlarını verilen sırayla uygulayın.
- Paket aktarımı başladıktan sonra güç kesmeyin.
- İşlem takılmış görünüyorsa önce aşama adını ve durum mesajını kontrol edin.
- Gerekirse **İptal** düğmesini kullanın; uygulamayı Görev Yöneticisi’nden zorla sonlandırmayın.

## 8. Başarılı işlem ölçütü

Yükleme yalnızca dosya aktarımıyla tamamlanmış sayılmaz. Uygulama:

1. Easy Installer’ın hedefi kapattığını,
2. Recovery’nin NORMAL olduğunu,
3. UKB’nin yeniden açıldığını,
4. seri konsolda beklenen OFP sürümünün görüldüğünü

doğrular.

Fiziksel Ethernet kablosu ve PHY hazırsa ayrıca `eth0 ... Link is Up` mesajı beklenir. OFP doğrulaması başarılı olduğu hâlde Link Up görülmezse kurulum sonucu korunabilir; fakat fiziksel Ethernet bağlantısı ayrıca kontrol edilmelidir.

## 9. Saha testi sonrasında saklanacak kayıtlar

Bir problem yaşanırsa aşağıdaki bilgiler birlikte saklanmalıdır:

- ayrıntılı oturum logu,
- ana `surumyakma.log`,
- hata ekran görüntüsü,
- seçilen platform ve UKB numarası,
- kullanılan COM portu,
- işlemin kaldığı aşama,
- kablo/aygıt durumuyla ilgili kısa operatör notu.

Gerçek yazılım paketi paylaşılmadan da çoğu hata bu kayıtlarla teşhis edilebilir.
