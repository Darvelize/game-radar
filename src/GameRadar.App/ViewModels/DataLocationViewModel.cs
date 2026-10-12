using CommunityToolkit.Mvvm.Input;
using GameRadar.App.Resources;
using GameRadar.Core.Settings;

namespace GameRadar.App.ViewModels;

/// <summary>
/// Settings › Data: where GameRadar keeps its files, and the button that
/// opens that folder. The "how to go portable" help is a fixed text in the view.
/// </summary>
public sealed partial class DataLocationViewModel(DataFolder data, Action<Uri> openBrowser)
{
    public bool IsPortable => data.IsPortable;

    /// <summary>A whole sentence per mode (the word order differs from one language to another).</summary>
    public string Summary => Localization.Format(
        data.IsPortable ? Strings.Data_Location_Portable : Strings.Data_Location_Normal, data.Root);

    [RelayCommand]
    private void OpenFolder()
    {
        // Created if nothing was ever saved: the button must open something.
        Directory.CreateDirectory(data.Root);
        // A file URI: the app's launcher opens it in the Windows file explorer.
        openBrowser(new Uri(data.Root + Path.DirectorySeparatorChar));
    }
}
