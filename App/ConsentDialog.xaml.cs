using System.Windows;
using System.Windows.Threading;

namespace Mnet.App;

public partial class ConsentDialog : Window
{
    private readonly DispatcherTimer _timer;
    private int _seconds = 60;

    public ConsentDialog(string address)
    {
        InitializeComponent();
        AddressText.Text = address;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Loaded += (_, _) =>
        {
            Activate();
            AllowButton.Focus();
        };
    }

    private void Tick()
    {
        _seconds--;
        CountdownText.Text = $"Se rechazará automáticamente en {_seconds} s.";

        if (_seconds <= 0)
        {
            _timer.Stop();
            DialogResult = false;
        }
    }

    private void Allow_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = true;
    }

    private void Deny_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
