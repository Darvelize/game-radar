using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace GameRadar.App.Views;

public partial class RecommendationsView : ContentPage
{
    public RecommendationsView()
    {
        InitializeComponent();
    }

    // The link to the plans leaves the page: its menu goes with it, once its command ran (the
    // Click comes first, and a hidden menu's link loses its binding, so its command).
    private void OnSubscriptionsLinkClick(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() => SourcesButton.Flyout?.Hide());
}
