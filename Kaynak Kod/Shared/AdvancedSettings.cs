using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SurumYakma
{
    public sealed class ProjectPackageMapping
    {
        public string EnvironmentName { get; set; } = "";
        public string PackageNamePrefix { get; set; } = "";

        public static List<ProjectPackageMapping> CreateDefaults()
        {
            return new List<ProjectPackageMapping>
            {
                new ProjectPackageMapping { EnvironmentName = "YFYK*", PackageNamePrefix = "WCC" },
                new ProjectPackageMapping { EnvironmentName = "ANKA_3", PackageNamePrefix = "ANKA_3" },
                new ProjectPackageMapping { EnvironmentName = "OPERATIF", PackageNamePrefix = "OPERATIF" },
                new ProjectPackageMapping { EnvironmentName = "ANKA_X", PackageNamePrefix = "ANKAX" },
                new ProjectPackageMapping { EnvironmentName = "KIHA", PackageNamePrefix = "KIHA" },
                new ProjectPackageMapping { EnvironmentName = "TUK", PackageNamePrefix = "TUK" },
                new ProjectPackageMapping { EnvironmentName = "SUPERSIMSEK", PackageNamePrefix = "SUPERSIMSEK" },
                new ProjectPackageMapping { EnvironmentName = "SIMSEK", PackageNamePrefix = "SIMSEK" },
                new ProjectPackageMapping { EnvironmentName = "KSIMSEK", PackageNamePrefix = "KS" },
                new ProjectPackageMapping { EnvironmentName = "TUK_VTOL", PackageNamePrefix = "VTUK" },

                // Eski bilgisayar adlarıyla geriye dönük uyumluluk.
                new ProjectPackageMapping { EnvironmentName = "VTUK", PackageNamePrefix = "VTUK" },
                new ProjectPackageMapping { EnvironmentName = "KS", PackageNamePrefix = "KS" }
            };
        }
    }

    internal static class ProtectedSecret
    {
        private const int CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Length;
            public IntPtr Data;
        }

        [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob input,
            string description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            out DataBlob output);

        [DllImport("Crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob input,
            IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            out DataBlob output);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return "";

            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            DataBlob input = Allocate(bytes);
            DataBlob output = default;
            try
            {
                if (!CryptProtectData(
                    ref input,
                    "UKB Easy Installer",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                byte[] protectedBytes = new byte[output.Length];
                Marshal.Copy(output.Data, protectedBytes, 0, output.Length);
                return Convert.ToBase64String(protectedBytes);
            }
            finally
            {
                FreeInput(input);
                if (output.Data != IntPtr.Zero)
                    LocalFree(output.Data);
            }
        }

        public static string Unprotect(string protectedValue)
        {
            if (string.IsNullOrWhiteSpace(protectedValue))
                return "";

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(protectedValue);
            }
            catch (FormatException)
            {
                return "";
            }

            DataBlob input = Allocate(bytes);
            DataBlob output = default;
            try
            {
                if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output))
                    return "";

                byte[] plainBytes = new byte[output.Length];
                Marshal.Copy(output.Data, plainBytes, 0, output.Length);
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                FreeInput(input);
                if (output.Data != IntPtr.Zero)
                    LocalFree(output.Data);
            }
        }

        private static DataBlob Allocate(byte[] bytes)
        {
            var blob = new DataBlob
            {
                Length = bytes.Length,
                Data = Marshal.AllocHGlobal(bytes.Length)
            };
            Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }

        private static void FreeInput(DataBlob blob)
        {
            if (blob.Data != IntPtr.Zero)
            {
                for (int i = 0; i < blob.Length; i++)
                    Marshal.WriteByte(blob.Data, i, 0);
                Marshal.FreeHGlobal(blob.Data);
            }
        }
    }
}
