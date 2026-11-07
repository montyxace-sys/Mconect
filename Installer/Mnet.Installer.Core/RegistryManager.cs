using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace Mnet.Installer.Core
{
    public static class RegistryManager
    {
        public static void RegisterInstall(InstallLayout layout, string installDirectory,
            InstallOptions options, long estimatedBytes)
        {
            var exe = Path.Combine(installDirectory, ProductInfo.ExeName);
            var uninstaller = Path.Combine(installDirectory, ProductInfo.UninstallerName);

            using (var key = layout.Hive.CreateSubKey(layout.UninstallKeyPath))
            {
                key.SetValue("DisplayName", ProductInfo.Name);
                key.SetValue("DisplayVersion", ReadVersion(exe));
                key.SetValue("Publisher", ProductInfo.Publisher);
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("InstallLocation", installDirectory);
                key.SetValue("UninstallString", "\"" + uninstaller + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize",
                    (int)Math.Min(int.MaxValue, Math.Max(1, estimatedBytes / 1024)), RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                key.SetValue("URLInfoAbout", ProductInfo.Url);
                key.SetValue("Comments", ProductInfo.Description);
            }

            SetRunAsAdministrator(layout, exe, options.RunAsAdministrator);
        }

        public static void UnregisterAll(InstallLayout layout, string installDirectory)
        {
            var exe = Path.Combine(installDirectory, ProductInfo.ExeName);
            SetRunAsAdministrator(layout, exe, false);

            try
            {
                layout.Hive.DeleteSubKeyTree(layout.UninstallKeyPath, false);
            }
            catch
            {
                // Si la clave está bloqueada, Windows la eliminará al reiniciar el explorador.
            }
        }

        private static void SetRunAsAdministrator(InstallLayout layout, string exe, bool enabled)
        {
            try
            {
                using (var layers = layout.Hive.CreateSubKey(layout.AppCompatLayersPath))
                {
                    if (enabled)
                    {
                        layers.SetValue(exe, "~ RUNASADMIN", RegistryValueKind.String);
                    }
                    else
                    {
                        layers.DeleteValue(exe, false);
                    }
                }
            }
            catch
            {
                // Equipos con la clave protegida: la opción se ignora sin romper la instalación.
            }
        }

        private static string ReadVersion(string exe)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                var parts = (info.FileVersion ?? string.Empty).Split('.');
                if (parts.Length >= 3)
                {
                    return parts[0] + "." + parts[1] + "." + parts[2];
                }
            }
            catch
            {
            }

            return ProductInfo.Version;
        }
    }
}
