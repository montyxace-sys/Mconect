using System;

namespace Mconect.Installer.Core
{
    public static class ProductInfo
    {
        public const string Name = "Mconect";
        public const string Publisher = "MSoft";
        public const string Url = "https://msoft.com.es";
        public const string Description = "Escritorio remoto directo y cifrado";
        public const string ExeName = "Mconect.exe";
        public const string UninstallerName = "Mconect.Uninstall.exe";

        public static string Version
        {
            get
            {
                var version = typeof(ProductInfo).Assembly.GetName().Version;
                return version == null ? "0.1.0" : version.ToString(3);
            }
        }
    }

    public sealed class InstallerException : Exception
    {
        public InstallerException(string message) : base(message)
        {
        }

        public InstallerException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
