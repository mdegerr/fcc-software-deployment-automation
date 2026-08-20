# Güvenlik Politikası

## Kapsam

Bu proje UKB güç, Recovery, seri bağlantı, USB-NCM ağı ve hedef yazılım yükleme işlemlerine müdahale eder. Güvenlik veya emniyet etkisi bulunan bulgular herkese açık issue içinde ayrıntılı saha verisi paylaşılmadan proje sahibiyle özel kanaldan iletilmelidir.

## Paylaşılmaması gereken içerikler

- gerçek OFP/TEZI yazılım paketleri,
- kullanıcı adı ve parolalar,
- saha `Settings.json` dosyaları,
- kurum içi IP/kanal eşlemeleri,
- cihaz seri numarası ve tanımlayıcıları,
- ayrıntılı saha logları.

## Desteklenen sürüm

Güvenlik ve saha güvenilirliği düzeltmeleri güncel kararlı Release üzerinde yapılır. Eski sürümlerde görülen problem önce güncel sürümle tekrar doğrulanmalıdır.

## Emniyet ilkesi

Doğrulanmamış paketle, bilinmeyen kanal eşlemesiyle veya yetkisiz hedef donanım üzerinde yükleme başlatılmamalıdır. Kritik aşamada güç kesilmesi hedef yazılımı kullanılamaz duruma getirebilir.
