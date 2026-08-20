using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SurumYakma
{
    public class VersionListResult
    {
        public string[] Paths { get; set; }
        public string[] Names { get; set; }
    }

    public sealed class NaturalVersionNameComparer : IComparer<string>
    {
        public static readonly NaturalVersionNameComparer Instance = new NaturalVersionNameComparer();

        private NaturalVersionNameComparer() { }

        public int Compare(string left, string right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            MatchCollection leftParts = Regex.Matches(left, @"\d+|\D+");
            MatchCollection rightParts = Regex.Matches(right, @"\d+|\D+");
            int count = Math.Min(leftParts.Count, rightParts.Count);

            for (int i = 0; i < count; i++)
            {
                string leftPart = leftParts[i].Value;
                string rightPart = rightParts[i].Value;
                bool leftNumber = char.IsDigit(leftPart[0]);
                bool rightNumber = char.IsDigit(rightPart[0]);
                int comparison;

                if (leftNumber && rightNumber)
                {
                    string normalizedLeft = leftPart.TrimStart('0');
                    string normalizedRight = rightPart.TrimStart('0');
                    if (normalizedLeft.Length == 0) normalizedLeft = "0";
                    if (normalizedRight.Length == 0) normalizedRight = "0";

                    comparison = normalizedLeft.Length.CompareTo(normalizedRight.Length);
                    if (comparison == 0)
                        comparison = string.CompareOrdinal(normalizedLeft, normalizedRight);
                    if (comparison == 0)
                        comparison = leftPart.Length.CompareTo(rightPart.Length);
                }
                else
                {
                    comparison = string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);
                }

                if (comparison != 0)
                    return comparison;
            }

            return leftParts.Count.CompareTo(rightParts.Count);
        }
    }

    /// <summary>
    /// Sürüm dosyalarını bulma/hazırlama mantığı. Orijinal mantık (image.json
    /// ve wrapup.sh düzenleme, "*build.0" arama, KSIMSEK özel durumu vb.)
    /// olduğu gibi korunuyor; sadece Form1'den ayrılıp bağımsız test
    /// edilebilir hale getirildi.
    /// </summary>
    public class VersionManager
    {
        private readonly AppConfig _cfg;
        public VersionManager(AppConfig cfg) { _cfg = cfg; }

        /// <summary>Bir klasördeki tüm alt klasör isimlerini döndürür (proje/sürüm listeleri için).</summary>
        public static string[] GetSubDirNames(string path)
        {
            if (!Directory.Exists(path)) return new string[0];
            return Directory.GetDirectories(path).Select(p => Path.GetFileName(p)).ToArray();
        }

        public static bool IsOfpRepository(string path)
        {
            return Directory.Exists(path) &&
                   Directory.GetDirectories(path).Any(IsOfpPackage);
        }

        public static bool IsOfpPackage(string path)
        {
            return Directory.Exists(path) && File.Exists(Path.Combine(path, "ofp"));
        }

        public static bool IsTeziPackage(string path)
        {
            return Directory.Exists(path) &&
                   Path.GetFileName(path).EndsWith("build.0", StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(Path.Combine(path, "image.json")) &&
                   File.Exists(Path.Combine(path, "prepare.sh")) &&
                   File.Exists(Path.Combine(path, "wrapup.sh"));
        }

        public static string NormalizeProjectFamily(string projectName)
        {
            return NormalizeProjectFamily(projectName, null);
        }

        public static string NormalizeProjectFamily(
            string projectName,
            IEnumerable<ProjectPackageMapping> mappings)
        {
            string value = (projectName ?? "").Trim().Replace('-', '_').ToUpperInvariant();

            if (mappings != null)
            {
                foreach (ProjectPackageMapping mapping in mappings.OrderBy(mapping =>
                    (mapping?.EnvironmentName ?? "").Trim().EndsWith("*", StringComparison.Ordinal)))
                {
                    string pattern = (mapping?.EnvironmentName ?? "").Trim()
                        .Replace('-', '_')
                        .ToUpperInvariant();
                    string packagePrefix = (mapping?.PackageNamePrefix ?? "").Trim();
                    if (pattern.Length == 0 || packagePrefix.Length == 0)
                        continue;

                    bool wildcard = pattern.EndsWith("*", StringComparison.Ordinal);
                    string comparison = wildcard ? pattern.Substring(0, pattern.Length - 1) : pattern;
                    bool matches = wildcard
                        ? value.StartsWith(comparison, StringComparison.OrdinalIgnoreCase)
                        : value.Equals(comparison, StringComparison.OrdinalIgnoreCase);
                    if (matches)
                        return packagePrefix;
                }
            }

            // YFYK bilgisayarlarında ortam değişkeninin sürüm eki (örn. 7.2.4.X)
            // değişebilir. Bu proje ailesinin TEZI paketleri WCC adıyla üretilir.
            if (IsYfykProject(value))
                return "WCC";

            switch (value)
            {
                case "TUK": return "TUK";
                case "VTUK": return "VTUK";
                case "KIHA": return "KIHA";
                case "KS":
                case "KSIMSEK":
                case "KŞIMŞEK":
                case "K_SIMSEK":
                    return "KS";
                default: return value;
            }
        }

        public static bool IsYfykProject(string projectName)
        {
            string value = (projectName ?? "").Trim().Replace('-', '_').ToUpperInvariant();
            return Regex.IsMatch(value, @"^YFYK(?:\s+.*)?$", RegexOptions.CultureInvariant);
        }

        public static bool PackageMatchesProject(
            string packagePathOrName,
            string projectName,
            IEnumerable<ProjectPackageMapping> mappings = null)
        {
            string family = NormalizeProjectFamily(projectName, mappings);
            if (string.IsNullOrWhiteSpace(family))
                return false;

            string name = Path.GetFileName((packagePathOrName ?? "").TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            string prefix = family.ToLowerInvariant();
            return name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TeziPackageMatchesProject(
            string packagePath,
            string projectName,
            IEnumerable<ProjectPackageMapping> mappings = null)
        {
            return IsTeziPackage(packagePath) &&
                   (PackageMatchesProject(packagePath, projectName, mappings) ||
                    ImageNameMatchesProject(packagePath, projectName, mappings));
        }

        public static string[] GetTeziPackagePaths(
            string repositoryPath,
            string projectName,
            IEnumerable<ProjectPackageMapping> mappings = null,
            bool validateProjectPrefix = true)
        {
            if (!Directory.Exists(repositoryPath))
                return new string[0];

            return Directory.GetDirectories(repositoryPath, "*", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).StartsWith(".surumyakma-", StringComparison.OrdinalIgnoreCase))
                .Where(IsTeziPackage)
                .Where(path => !validateProjectPrefix || TeziPackageMatchesProject(path, projectName, mappings))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(Path.GetFileName, NaturalVersionNameComparer.Instance)
                .ToArray();
        }

        /// <summary>
        /// Sürüm deposundaki tam TEZI paketlerini kullanıcıya gösterilecek dış
        /// sürüm klasörü adıyla eşler. Örneğin
        /// TUK_7.1.12.26.0\tuk-Tezi_*build.0 yapısında görünen ad
        /// TUK_7.1.12.26.0, gerçek yükleme yolu ise içteki build.0 klasörüdür.
        /// </summary>
        /// <summary>
        /// Secili platform klasorunun yalnizca dogrudan alt klasorlerini surum olarak
        /// listeler. Kullaniciya dis klasor adi gosterilir; yukleme icin bu klasorun
        /// icindeki ilk gecerli tam TEZI paketinin gercek yolu kullanilir.
        /// Paket veya image name on eki platform adiyla karsilastirilmaz.
        /// </summary>
        public static VersionListResult GetSelectableTeziVersions(
            string repositoryPath,
            string projectName,
            IEnumerable<ProjectPackageMapping> mappings = null,
            bool validateProjectPrefix = false)
        {
            if (!Directory.Exists(repositoryPath))
                return new VersionListResult
                {
                    Paths = Array.Empty<string>(),
                    Names = Array.Empty<string>()
                };

            var versions = Directory.GetDirectories(repositoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).StartsWith(
                    ".surumyakma-",
                    StringComparison.OrdinalIgnoreCase))
                .Select(versionFolder => new
                {
                    Name = Path.GetFileName(versionFolder),
                    PackagePath = FindLoadableTeziPackage(versionFolder)
                })
                .OrderByDescending(item => item.Name, NaturalVersionNameComparer.Instance)
                .ToArray();

            return new VersionListResult
            {
                Paths = versions.Select(item => item.PackagePath ?? Path.Combine(repositoryPath, item.Name)).ToArray(),
                Names = versions.Select(item => item.Name).ToArray()
            };
        }

        private static string FindLoadableTeziPackage(string versionFolder)
        {
            if (IsTeziPackage(versionFolder))
                return versionFolder;

            return Directory.GetDirectories(versionFolder, "*", SearchOption.AllDirectories)
                .Where(IsTeziPackage)
                .OrderBy(path => Path.GetRelativePath(versionFolder, path)
                    .Count(character => character == Path.DirectorySeparatorChar ||
                                        character == Path.AltDirectorySeparatorChar))
                .ThenByDescending(Path.GetFileName, NaturalVersionNameComparer.Instance)
                .FirstOrDefault();
        }
        public static string GetExpectedOfpVersion(string teziPackagePath)
        {
            if (!IsTeziPackage(teziPackagePath))
                throw new InvalidOperationException("Beklenen OFP sürümü yalnızca geçerli TEZI paketinden okunabilir: " + teziPackagePath);

            string imageName = ReadJsonStringValue(Path.Combine(teziPackagePath, "image.json"), "name");
            Match match = Regex.Match(imageName, @"(?<!\d)(?<version>\d+(?:\.\d+){3,})(?!\d)");
            if (!match.Success)
                throw new InvalidOperationException(
                    "TEZI image.json name alanında doğrulanabilir OFP sürümü bulunamadı: " + imageName);
            return match.Groups["version"].Value;
        }

        public static string[] GetOfpVersionNames(string repositoryPath)
        {
            if (!Directory.Exists(repositoryPath))
                return new string[0];

            return Directory.GetDirectories(repositoryPath)
                .Where(IsOfpPackage)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>Flash bellekteki, seçili projeye ait sürümleri bulur. Item1: path, Item2: görünen isim.</summary>
        public VersionListResult GetVersionsInFlash(string driveRoot, string selectedProject)
        {
            if (selectedProject.Equals("OFP", StringComparison.OrdinalIgnoreCase))
            {
                string[] ofpDirs = Directory.Exists(driveRoot)
                    ? Directory.GetDirectories(driveRoot).Where(IsOfpPackage).ToArray()
                    : new string[0];
                return new VersionListResult
                {
                    Paths = ofpDirs,
                    Names = ofpDirs.Select(Path.GetFileName).ToArray()
                };
            }

            string[] versionDirs = Directory.Exists(driveRoot)
                ? Directory.GetDirectories(driveRoot, "*build.0").Where(IsTeziPackage).ToArray()
                : new string[0];

            var filteredPaths = new List<string>();
            var filteredNames = new List<string>();
            for (int i = 0; i < versionDirs.Length; i++)
            {
                filteredPaths.Add(versionDirs[i]);
                filteredNames.Add(Path.GetFileName(versionDirs[i]));
            }
            return new VersionListResult
            {
                Paths = filteredPaths.ToArray(),
                Names = filteredNames.ToArray()
            };
        }

        /// <summary>PC'deki sürüm klasörünü flash belleğe kopyalar ve autoinstall=true, poweroff -f ayarlarını yapar.</summary>
        public void PrepareVersionFromPc(string surumFolderPath, string destDriveRoot)
        {
            if (!Directory.Exists(surumFolderPath))
                throw new InvalidOperationException("Versiyon dosyası yok: " + surumFolderPath);

            if (IsOfpPackage(surumFolderPath))
            {
                CopyOfpPackageToFlash(surumFolderPath, destDriveRoot);
                return;
            }

            string sourceDir;
            if (IsTeziPackage(surumFolderPath))
            {
                sourceDir = surumFolderPath;
            }
            else
            {
                string[] buildDirs = Directory.GetDirectories(surumFolderPath, "*build.0")
                    .Where(IsTeziPackage)
                    .ToArray();
                if (buildDirs.Length != 1)
                    throw new InvalidOperationException(
                        buildDirs.Length == 0
                            ? "Tam TEZI güncelleme paketi bulunamadı: " + surumFolderPath
                            : "Klasörde birden fazla TEZI paketi bulundu; doğrudan yüklenecek *build.0 klasörünü seçin: " + surumFolderPath);
                sourceDir = buildDirs[0];
            }

            if (!IsTeziPackage(sourceDir))
                throw new InvalidOperationException("Güncelleme dosyası yok: " + surumFolderPath);
            string destPath = Path.Combine(destDriveRoot, Path.GetFileName(sourceDir));
            if (Directory.Exists(destPath))
                throw new InvalidOperationException(
                    "Aynı sürüm flash bellekte zaten mevcut. Mevcut sürümü listeden seçin veya belleği kontrollü biçimde temizleyin: " +
                    destPath);

            long requiredBytes = GetDirectorySize(sourceDir);
            string destinationVolume = Path.GetPathRoot(Path.GetFullPath(destDriveRoot));
            long availableBytes = new DriveInfo(destinationVolume).AvailableFreeSpace;
            if (availableBytes < requiredBytes)
                throw new IOException(
                    "Flash bellekte yeterli boş alan yok. Gerekli: " + requiredBytes +
                    " bayt, kullanılabilir: " + availableBytes + " bayt.");

            string stagingPath = Path.Combine(destDriveRoot, ".surumyakma-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyDirectory(sourceDir, stagingPath);
                PrepareSelectedVersion(destDriveRoot, stagingPath);
                Directory.Move(stagingPath, destPath);
            }
            catch
            {
                if (Directory.Exists(stagingPath))
                    Directory.Delete(stagingPath, true);
                throw;
            }
        }

        /// <summary>
        /// Kaynak TEZI paketini değiştirmeden yerel ağ yayını için doğrulanmış bir
        /// staging kopyası oluşturur. Dönen klasör HTTP sunucusuna verilir.
        /// </summary>
        public string PrepareVersionForNetwork(string sourcePath, string stagingRoot)
        {
            if (!IsTeziPackage(sourcePath))
                throw new InvalidOperationException(
                    "Ağdan yükleme yalnızca tam TEZI *build.0 paketiyle yapılabilir: " + sourcePath);
            if (string.IsNullOrWhiteSpace(stagingRoot))
                throw new ArgumentException("Ağ staging klasörü boş olamaz.", nameof(stagingRoot));

            Directory.CreateDirectory(stagingRoot);
            long requiredBytes = GetDirectorySize(sourcePath);
            string volume = Path.GetPathRoot(Path.GetFullPath(stagingRoot));
            long availableBytes = new DriveInfo(volume).AvailableFreeSpace;
            if (availableBytes < requiredBytes)
                throw new IOException(
                    "Ağ aktarım staging alanında yeterli boş yer yok. Gerekli: " + requiredBytes +
                    " bayt, kullanılabilir: " + availableBytes + " bayt.");

            string id = Guid.NewGuid().ToString("N");
            string preparingPath = Path.Combine(stagingRoot, ".preparing-" + id);
            string readyPath = Path.Combine(stagingRoot, "package-" + id);
            try
            {
                CopyDirectory(sourcePath, preparingPath);
                string imageJson = Path.Combine(preparingPath, "image.json");
                string wrapup = Path.Combine(preparingPath, "wrapup.sh");
                RemoveLicenseForUnattendedInstall(imageJson);
                SetJsonBoolean(imageJson, "autoinstall", true);
                EnsurePowerOff(wrapup);
                ValidatePreparedNetworkPackage(preparingPath);
                Directory.Move(preparingPath, readyPath);
                Logger.Checkpoint(
                    "NETWORK_PACKAGE_PREPARE",
                    "OK",
                    $"source={sourcePath}; staging={readyPath}; bytes={requiredBytes}");
                return readyPath;
            }
            catch
            {
                if (Directory.Exists(preparingPath))
                    Directory.Delete(preparingPath, true);
                throw;
            }
        }

        private static void ValidatePreparedNetworkPackage(string packageRoot)
        {
            string imageJsonPath = Path.Combine(packageRoot, "image.json");
            JsonObject image = ReadJsonObject(imageJsonPath);
            if (image["autoinstall"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("Ağ staging image.json dosyasında autoinstall=true doğrulanamadı.");
            if (image.ContainsKey("license") || image.ContainsKey("license_title"))
                throw new InvalidOperationException("Ağ staging image.json dosyasında etkileşimli lisans alanı kaldı.");
            if (image["config_format"] != null)
            {
                string value = image["config_format"].ToString();
                if (!int.TryParse(value, out int format) || format < 1)
                    throw new InvalidOperationException("TEZI config_format değeri geçersiz: " + value);
            }

            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectPackageReferences(image, null, references);
            string root = Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string reference in references)
            {
                if (Uri.TryCreate(reference, UriKind.Absolute, out Uri uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    continue;
                string full = Path.GetFullPath(Path.Combine(packageRoot, reference.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("TEZI paket referansı paket dışına çıkıyor: " + reference);
                if (!File.Exists(full))
                    throw new FileNotFoundException("TEZI image.json tarafından kullanılan dosya bulunamadı: " + reference, full);
                if (new FileInfo(full).Length == 0)
                    throw new InvalidOperationException("TEZI image.json tarafından kullanılan dosya boş: " + reference);
            }
            Logger.Checkpoint("NETWORK_PACKAGE_MANIFEST_VALIDATE", "SUCCESS",
                $"root={packageRoot}; referencedFiles={references.Count}; autoinstall=true; interactiveLicense=false");
        }

        private static void CollectPackageReferences(JsonNode node, string propertyName, ISet<string> references)
        {
            if (node == null) return;
            if (node is JsonValue value)
            {
                if (IsPackageFileReferenceProperty(propertyName) &&
                    value.TryGetValue<string>(out string path) && !string.IsNullOrWhiteSpace(path))
                    references.Add(path.Trim());
                return;
            }
            if (node is JsonObject obj)
            {
                foreach (KeyValuePair<string, JsonNode> item in obj)
                    CollectPackageReferences(item.Value, item.Key, references);
                return;
            }
            if (node is JsonArray array)
                foreach (JsonNode item in array) CollectPackageReferences(item, propertyName, references);
        }

        private static bool IsPackageFileReferenceProperty(string propertyName)
        {
            return propertyName != null && new[]
            {
                "filename", "filelist", "image_filename", "u_boot_env", "prepare_script", "wrapup_script",
                "error_script", "icon", "marketing", "releasenotes"
            }.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Flash bellekte zaten duran bir sürümü, üzerine kurulacak şekilde işaretler (autoinstall=true, poweroff -f).</summary>
        public void PrepareVersionAlreadyOnFlash(string versionPath)
        {
            if (!Directory.Exists(versionPath))
                throw new InvalidOperationException("Versiyon dosyası yok: " + versionPath);

            if (IsOfpPackage(versionPath))
            {
                VerifyOfpFile(versionPath);
                return;
            }

            PrepareSelectedVersion(Path.GetPathRoot(versionPath), versionPath);
        }

        private static void CopyOfpPackageToFlash(string sourcePath, string destDriveRoot)
        {
            VerifyOfpFile(sourcePath);
            string destPath = Path.Combine(destDriveRoot, Path.GetFileName(sourcePath));
            if (Directory.Exists(destPath))
                throw new InvalidOperationException(
                    "Aynı OFP sürümü flash bellekte zaten mevcut. Flash bellekteki sürümü seçin: " + destPath);

            long requiredBytes = GetDirectorySize(sourcePath);
            string destinationVolume = Path.GetPathRoot(Path.GetFullPath(destDriveRoot));
            long availableBytes = new DriveInfo(destinationVolume).AvailableFreeSpace;
            if (availableBytes < requiredBytes)
                throw new IOException(
                    "Flash bellekte yeterli boş alan yok. Gerekli: " + requiredBytes +
                    " bayt, kullanılabilir: " + availableBytes + " bayt.");

            string stagingPath = Path.Combine(destDriveRoot, ".surumyakma-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyDirectory(sourcePath, stagingPath);
                VerifyOfpFile(stagingPath);
                Directory.Move(stagingPath, destPath);
            }
            catch
            {
                if (Directory.Exists(stagingPath))
                    Directory.Delete(stagingPath, true);
                throw;
            }
        }

        private static void VerifyOfpFile(string packagePath)
        {
            string ofpPath = Path.Combine(packagePath, "ofp");
            if (!File.Exists(ofpPath) || new FileInfo(ofpPath).Length < 4)
                throw new InvalidOperationException("OFP paketi geçersiz veya boş: " + ofpPath);

            byte[] header = new byte[4];
            using (FileStream stream = File.OpenRead(ofpPath))
            {
                if (stream.Read(header, 0, header.Length) != header.Length ||
                    header[0] != 0x7F || header[1] != (byte)'E' ||
                    header[2] != (byte)'L' || header[3] != (byte)'F')
                    throw new InvalidOperationException("OFP dosyası geçerli bir ELF çalıştırılabilir dosyası değil: " + ofpPath);
            }
        }

        private void PrepareSelectedVersion(string driveRoot, string selectedVersionPath)
        {
            // Aynı bellekte yalnızca seçilen sürüm otomatik kurulmalıdır.
            foreach (string versionDir in Directory.GetDirectories(driveRoot, "*build.0"))
            {
                string imageJson = Path.Combine(versionDir, "image.json");
                if (File.Exists(imageJson))
                    SetJsonBoolean(imageJson, "autoinstall", false);
            }

            string selectedJson = Path.Combine(selectedVersionPath, "image.json");
            string selectedWrapup = Path.Combine(selectedVersionPath, "wrapup.sh");
            if (!File.Exists(selectedJson) || !File.Exists(selectedWrapup))
                throw new InvalidOperationException("Seçilen sürümde image.json veya wrapup.sh bulunamadı: " + selectedVersionPath);

            RemoveLicenseForUnattendedInstall(selectedJson);
            SetJsonBoolean(selectedJson, "autoinstall", true);
            EnsurePowerOff(selectedWrapup);
        }

        private static void RemoveLicenseForUnattendedInstall(string imageJsonPath)
        {
            JsonObject image = ReadJsonObject(imageJsonPath);
            bool removed = image.Remove("license");
            removed = image.Remove("license_title") || removed;
            if (!removed)
                return;

            WriteJsonObject(imageJsonPath, image);
            Logger.Info(
                "TEZI otomatik kurulumu için flash kopyasındaki license/license_title referansları kaldırıldı; lisans dosyası korunuyor.");
        }

        private static void SetJsonBoolean(string file, string propertyName, bool value)
        {
            JsonObject image = ReadJsonObject(file);
            if (!image.ContainsKey(propertyName))
                throw new InvalidOperationException(file + " dosyasında \"" + propertyName + "\" ayarı bulunamadı.");
            image[propertyName] = value;
            WriteJsonObject(file, image);
        }

        private static JsonObject ReadJsonObject(string file)
        {
            JsonObject image = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
            if (image == null)
                throw new InvalidOperationException("TEZI image.json kök nesnesi geçersiz: " + file);
            return image;
        }

        private static void WriteJsonObject(string file, JsonObject image)
        {
            string json = image.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json + "\n", new System.Text.UTF8Encoding(false));
        }

        private static string ReadJsonStringValue(string file, string propertyName)
        {
            string json = File.ReadAllText(file);
            Match match = Regex.Match(
                json,
                "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"(?<value>[^\"]+)\"",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                throw new InvalidOperationException(file + " dosyasında \"" + propertyName + "\" alanı bulunamadı.");

            return match.Groups["value"].Value;
        }

        private static bool ImageNameMatchesProject(
            string packagePath,
            string projectName,
            IEnumerable<ProjectPackageMapping> mappings = null)
        {
            string imageJson = Path.Combine(packagePath, "image.json");
            if (!File.Exists(imageJson))
                return false;

            try
            {
                string imageName = ReadJsonStringValue(imageJson, "name");
                return PackageMatchesProject(imageName, projectName, mappings);
            }
            catch
            {
                return false;
            }
        }

        private static void EnsurePowerOff(string file)
        {
            var lines = File.ReadAllLines(file).ToList();
            lines.RemoveAll(line => line.Trim().Equals("poweroff -f", StringComparison.OrdinalIgnoreCase));

            int exitIndex = lines.FindIndex(line => line.Trim().Equals("exit 0", StringComparison.OrdinalIgnoreCase));
            if (exitIndex < 0)
                lines.Add("poweroff -f");
            else
                lines.Insert(exitIndex, "poweroff -f");

            WriteUnixText(file, lines.ToArray());
        }

        private static void WriteUnixText(string file, string[] lines)
        {
            File.WriteAllText(file, string.Join("\n", lines) + "\n", new System.Text.UTF8Encoding(false));
        }

        public static void LineChanger(string file, int lineIndex, string newText)
        {
            string[] lines = File.ReadAllLines(file);
            if (lineIndex < 0 || lineIndex >= lines.Length)
                throw new InvalidOperationException($"{file} dosyasında {lineIndex + 1}. satır bulunamadı (dosya formatı değişmiş olabilir).");
            lines[lineIndex] = newText;
            WriteUnixText(file, lines);
        }

        public static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destinationFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destinationFile, true);
                VerifyCopiedFile(file, destinationFile);
            }
            foreach (string dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }

        private static long GetDirectorySize(string directory)
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }

        private static void VerifyCopiedFile(string sourceFile, string destinationFile)
        {
            var sourceInfo = new FileInfo(sourceFile);
            var destinationInfo = new FileInfo(destinationFile);
            if (sourceInfo.Length != destinationInfo.Length)
                throw new IOException("Kopyalanan dosyanın boyutu doğrulanamadı: " + destinationFile);

            using (SHA256 sha = SHA256.Create())
            using (FileStream sourceStream = File.OpenRead(sourceFile))
            using (FileStream destinationStream = File.OpenRead(destinationFile))
            {
                byte[] sourceHash = sha.ComputeHash(sourceStream);
                destinationStream.Position = 0;
                byte[] destinationHash = sha.ComputeHash(destinationStream);
                if (!sourceHash.SequenceEqual(destinationHash))
                    throw new IOException("Kopyalanan dosyanın SHA-256 doğrulaması başarısız: " + destinationFile);
            }
        }
    }
}
