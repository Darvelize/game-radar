using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GameRadar.App.ViewModels;

// The tabs of the settings page. Remembered while the app runs (not saved):
// coming back to the settings, or back from the release notes, shows the
// same tab.
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsTabIndex))]
    public partial SettingsTab CurrentSettingsTab { get; set; }

    /// <summary>
    /// The current tab as a position, for the SelectedIndex of the TabbedPage
    /// (two-way: a click on a tab lands here). The tabs are in the enum's order.
    /// </summary>
    public int SettingsTabIndex
    {
        get => (int)CurrentSettingsTab;
        set
        {
            // -1 = no tab, e.g. while the page is being built: keep the current one.
            if (Enum.IsDefined((SettingsTab)value))
                CurrentSettingsTab = (SettingsTab)value;
        }
    }

    /// <summary>A link from elsewhere straight to a tab, e.g. "Add your library in Settings" → Accounts.</summary>
    [RelayCommand]
    private void OpenSettings(SettingsTab tab)
    {
        CurrentSettingsTab = tab;
        CurrentPage = AppPage.Settings;
    }
}
