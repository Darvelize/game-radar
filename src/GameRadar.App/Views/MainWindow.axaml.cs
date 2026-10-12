using Avalonia.Controls;
using Avalonia.Threading;
using GameRadar.App.ViewModels;

namespace GameRadar.App.Views;

public partial class MainWindow : Window
{
    // "il y a 2 h" labels go stale while the app stays open: refresh them.
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };

    public MainWindow()
    {
        InitializeComponent();
        // DispatcherTimer ticks on the UI thread: safe to touch bound properties.
        _clock.Tick += (_, _) => (DataContext as MainWindowViewModel)?.RefreshRelativeTimes();
        Opened += (_, _) => _clock.Start();
        Closed += (_, _) => _clock.Stop();
    }
}
