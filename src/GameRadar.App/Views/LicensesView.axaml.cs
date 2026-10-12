using Avalonia;
using Avalonia.Controls;

namespace GameRadar.App.Views;

public partial class LicensesView : ContentPage
{
    public LicensesView()
    {
        InitializeComponent();
    }

    // Each time the page is shown (Page lifecycle, raised by the TabbedPage):
    // it opens at its top, even after scrolling down the last time.
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        Scroller.Offset = default(Vector);
    }
}
