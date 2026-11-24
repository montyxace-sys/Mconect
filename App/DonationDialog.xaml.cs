using System.Diagnostics;
using System.Windows;

namespace Mconect.App;

public partial class DonationDialog : Window
{
    public const string PayPalUrl = "https://paypal.me/MsoftGenuino";
    public const string LiberapayUrl = "https://liberapay.com/Msoft/donate";

    public DonationDialog()
    {
        InitializeComponent();
    }

    private void PayPal_Click(object sender, RoutedEventArgs e)
    {
        Open(PayPalUrl);
        CloseWithPreference();
    }

    private void Liberapay_Click(object sender, RoutedEventArgs e)
    {
        Open(LiberapayUrl);
        CloseWithPreference();
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private void CloseWithPreference()
    {
        if (DontShowAgain.IsChecked == true) Services.DonationPrefs.Hide();
        Close();
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Clipboard.SetText(url);
            MessageBox.Show("No se pudo abrir el navegador. Enlace copiado al portapapeles.",
                "Mconect", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
