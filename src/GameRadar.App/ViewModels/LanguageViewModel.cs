using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.App.Resources;

namespace GameRadar.App.ViewModels;

// The "Langue" setting, applied at the next start (see Localization).
public sealed partial class LanguageViewModel : ObservableObject
{
    private readonly SettingsSaver _saver;
    private readonly NoticeCenter _notices;
    private readonly Action _restart;

    public LanguageViewModel(SettingsSaver saver, NoticeCenter notices, Action restart)
    {
        _saver = saver;
        _notices = notices;
        _restart = restart;
        SelectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == saver.Settings.Language) ?? LanguageOptions[0];
    }

    /// <summary>A choice of the "Langue" list: null code = the Windows language.</summary>
    public sealed record LanguageOption(string? Code, string Label)
    {
        // Which symbol the list shows left of the name: a globe or a flag.
        public bool IsAutomatic => Code is null;
        public bool IsFrench => Code == Localization.French;
        public bool IsEnglish => Code == Localization.English;
    }

    /// <summary>Automatic first; the language names stay in their own language.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(null, Strings.Settings_Language_Automatic),
        new(Localization.French, "Français"),
        new(Localization.English, "English")
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsRestart))]
    public partial LanguageOption SelectedLanguage { get; set; }

    /// <summary>The chosen language is not the running one: "Redémarrer maintenant".</summary>
    public bool NeedsRestart => Localization.Resolve(SelectedLanguage?.Code) != Localization.Current;

    // Also called by the constructor when it loads the setting: it writes
    // only its own setting, and only when it really changed.
    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value.Code == _saver.Settings.Language)
            return;
        _saver.Settings.Language = value.Code;
        _ = SaveLanguageAsync();
    }

    private async Task SaveLanguageAsync()
    {
        try
        {
            await _saver.SaveAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notices.Show(Localization.Format(Strings.Settings_Language_SaveError, ex.Message));
        }
    }

    [RelayCommand]
    private void Restart() => _restart();
}
