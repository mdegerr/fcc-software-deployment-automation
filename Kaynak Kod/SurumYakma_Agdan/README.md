# UKB Sürüm Yükleme Otomasyonu — Ağdan Aktarım Sürümü

Bu kaynak ağdan aktarım için ayrılmış ikinci uygulamadır. Çalışan USB bellekli
sürüm `SurumYakmav_1.1` ve `Surum Yakma` klasörlerinde korunur; bu proje onları
değiştirmez.

İlk aşamada seçilen TEZI paketi kaynak değiştirilmeden yerel staging alanına
kopyalanır, SHA-256 ile doğrulanır ve PC'nin USB-NCM adresinde HTTP üzerinden
yayınlanır. UKB Easy Installer → PC erişimi laboratuvarda doğrulanmadan otomatik
kurulum aşaması etkinleştirilmeyecektir.

Bu proje UKB sürüm hazırlama, MOXA üzerinden recovery/power kontrolü ve Toradex UUU aktarımını tek arayüzde toplar. RealVNC kullanılmaz ve uygulama tarafından hiçbir zaman açılmaz.

## Teknoloji

- Windows 10/11
- .NET 8 WinForms
- x86 süreç: `MXIO_NET.dll` 32 bit olduğu için zorunludur
- Yapılandırma: `appsettings.json`

Windows 7 hedefi ve eski `.NET Framework 4.x` proje biçimi kaldırılmıştır.

> Geliştirme bilgisayarında şu anda .NET 10 SDK bulunmadığı için doğrulanmış hedef `net8.0-windows` olarak tutulmuştur. Üretim bakım ömrü için SDK hazır olduğunda .NET 10 LTS'e yükseltme önerilir.

## Otomatik akış

1. Program `UAV_PROJECT_NAME` değerine uygun tam TEZI paketlerini bulur, en yenisini seçer; operatör çıkarılabilir flash belleği seçer.
2. Ham `OFP_*` klasörleri üretim yüklemesine alınmaz; yalnızca `image.json`, `prepare.sh` ve `wrapup.sh` içeren `*build.0` paketleri kabul edilir.
3. PC'deki sürüm flash belleğe kopyalanır ve her dosya SHA-256 ile doğrulanır.
4. PC'deki ana sürüm dosyaları değiştirilmez.
5. TEZI paketlerinde yalnızca seçilen sürüm `autoinstall=true` yapılır ve `wrapup.sh` içine `exit 0` öncesinde `poweroff -f` eklenir.
   Paket lisans ekranı içeriyorsa Toradex unattended kurulum yöntemine uygun olarak flash kopyasındaki `license` ve `license_title` referansları otomatik kaldırılır; bu işlem lisansların otomatik kabulü anlamına gelir. PC'deki kaynak paket ve lisans dosyası korunur.
6. Recovery rölesi açılır ve flash belleğin PC'den çıkarılması beklenir.
7. Operatör flash belleği UKB sürüm portuna takıp ana düğmeye bir kez daha basarak fiziksel bağlantıyı bildirir.
8. UKB gücü açılır; `recovery\uuu.exe recovery` doğrudan çalıştırılır.
9. UKB'nin recovery ağı ve COM22 seri konsolundaki Easy Installer başlangıcı doğrulanır.
10. TEZI paketinin otomatik kurulumu, seri kapanış mesajı ve ağdan ayrılma birlikte izlenir.
11. Recovery NORMAL yapılır; UKB normal açılır ve seri konsoldaki `OFP Version` değeri seçilen paketin sürümüyle karşılaştırılır.
12. Doğrulama başarılıysa güç kapatılır ve cihaz kapalı bırakılır.

`recovery-windows.bat` içindeki `pause` otomasyona dahil değildir. BAT'ın yaptığı gerçek işlem doğrudan çalıştırılır.

UUU, MOXA, seri konsol, yüzde/adım, uyarı ve hata kayıtları uygulama içindeki **İşlem Konsolu**nda zaman damgasıyla gösterilir. RealVNC ve Tera Term süreçleri açılmaz.

## Güvenlik davranışı

- Kritik aktarım/kurulum aşamasında iptal düğmesi devre dışıdır.
- Kritik aşamada hata olursa program UKB gücünü otomatik kesmez.
- Bu durumda yeni yükleme engellenir ve onaylı seri port/kurtarma prosedürü istenir.
- Sabit disk hedef olarak seçilemez; yalnızca çıkarılabilir bellek kabul edilir.
- İki UKB etkinse power ve recovery kanallarının farklı olması doğrulanır.

## Yapılandırma

`SurumYakma/appsettings.json` içindeki şu değerler gerçek test düzeneğine göre doğrulanmalıdır:

- `PowerBoxIp`, `RelayBoxIp`
- `PowerSlot`, `PowerChannel1`, `PowerChannel2`
- `RecoverySlot`, `RecoveryChannel1`, `RecoveryChannel2`
- `UkbTargetIp`
- `SurumlerPath`
- `RecoveryNetworkTimeoutSeconds`
- `MoxaConnectTimeoutMs` (SuperSonic ile aynı üretim değeri: `10000`)
- `MoxaReconnectAttempts` (`2005 / socket disconnect` durumunda otomatik yeniden bağlantı)
- `MoxaKeepAliveIntervalSeconds` (uygulama açıkken güvenli kanal okumalarıyla bağlantıyı canlı tutma aralığı; varsayılan `15`)
- `SerialPortName` (doğrulanan hedefte `COM22`)
- `SerialBaudRate` (doğrulanan hedefte `115200`)
- `SerialRecoveryTimeoutSeconds`, `SerialBootTimeoutSeconds`
- zaman aşımı değerleri

`SurumlerPath` boş bırakılırsa klasör arayüzden seçilebilir.

### SEL bilgisayarına özel yapılandırma

Aynı uygulama klasörü farklı SEL bilgisayarlarında kullanılabilir. Uygulama önce
`appsettings.<BILGISAYAR_ADI>.json` dosyasını arar; bulamazsa `appsettings.json`
dosyasına döner. Örneğin bilgisayar adı `SEL05` ise `appsettings.SEL05.json`
kullanılır. Dosya adı ayrıca `SURUM_YAKMA_CONFIG` ortam değişkeniyle açıkça
seçilebilir. Bu yöntem IP, MOXA slot/kanal ve COM portu farklı istasyonlarda kodu
yeniden derleme ihtiyacını kaldırır.

Her çalıştırmada `Uygulama\logs\surumyakma_YYYYAAGG_SSddss_mmm_PID.log` biçiminde
ayrı ayrıntılı oturum logu oluşturulur. UI terminalindeki tüm satırlar tarih-saatle
kaydedilir; ayrıca yalnızca dosyada görünen `CHECKPOINT` ve hata stack trace kayıtları
bulunur. Uygulama klasörü yazılamazsa `%LOCALAPPDATA%\SurumYakma\logs` kullanılır.

## Derleme ve test

```powershell
dotnet restore .\SurumYakma\SurumYakma.sln
dotnet build .\SurumYakma\SurumYakma.sln -c Release
dotnet run --project .\SurumYakma\SurumYakma.Tests\SurumYakma.Tests.csproj -c Release
```

Üretimden önce MOXA slot/kanal eşlemesi enerjisiz veya güvenli test yüküyle doğrulanmalı; ardından gerçek UKB olmayan bir test donanımında uçtan uca prova yapılmalıdır.

## Paket kuralı ve kalan fiziksel doğrulama

Eklenen örnek `OFP_*` klasörleri arşiv/derleme çıktısıdır; üretim yükleme girdisi değildir. Uygulama gerçek flash bellekte gözlenen `tuk/vtuk/kiha/ks-Tezi_*+build.0` biçimindeki tam TEZI paketlerini kullanır. COM22 seri izleme ve sürüm karşılaştırması kodlanmıştır; gerçek UKB ile yetkili uçtan uca test henüz yapılmalıdır.
