using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GameRadar.App.ViewModels;

// "Pas intéressé" on a recommendation card: the game is never suggested
// again, nor the games that look too much like it (SimilarityRecommender).
// Undone from the banner of "Pour toi" right after, or later from the list
// of Settings › Preferences.
/// <param name="saver">Holds the refused games (<c>AppSettings.NotInterested</c>).</param>
/// <param name="changed">The refusals changed: the window saves the choice and rebuilds the cards from the last computation.</param>
public sealed partial class NotInterestedViewModel : ObservableObject
{
    private readonly SettingsSaver _saver;
    private readonly Action _changed;

    public NotInterestedViewModel(SettingsSaver saver, Action changed)
    {
        _saver = saver;
        _changed = changed;
        Reload();
    }

    /// <summary>The refused games, sorted by title, for the list of the preferences.</summary>
    public ObservableCollection<NotInterestedRow> Games { get; } = [];

    public bool HasGames => Games.Count > 0;

    /// <summary>The last refusal, for "Annuler" in the banner; null once undone or closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastRefusal), nameof(LastRefusalLabel))]
    public partial NotInterestedRow? LastRefusal { get; set; }

    public bool HasLastRefusal => LastRefusal is not null;

    public string LastRefusalLabel => LastRefusal is { } refusal
        ? Localization.Format(Strings.NotInterested_Done, refusal.Title)
        : string.Empty;

    /// <summary>The button of a card: the card goes away at once.</summary>
    [RelayCommand]
    private void Refuse(RecommendationRow row)
    {
        _saver.Settings.NotInterested[row.TitleKey] = row.Title;
        LastRefusal = new NotInterestedRow(row.TitleKey, row.Title);
        OnChanged();
    }

    /// <summary>"Annuler" in the banner: a click by mistake.</summary>
    [RelayCommand]
    private void Undo()
    {
        if (LastRefusal is { } refusal)
            Restore(refusal);
    }

    [RelayCommand]
    private void Dismiss() => LastRefusal = null;

    /// <summary>"Remettre" in the list of the preferences.</summary>
    [RelayCommand]
    private void Restore(NotInterestedRow row)
    {
        if (LastRefusal?.Key == row.Key)
            LastRefusal = null;
        if (_saver.Settings.NotInterested.Remove(row.Key))
            OnChanged();
    }

    private void OnChanged()
    {
        Reload();
        _changed();
    }

    /// <summary>Rebuilds the list from the settings (at startup, and after the titles moved to English).</summary>
    public void Reload()
    {
        Games.Clear();
        foreach (var (key, title) in _saver.Settings.NotInterested.OrderBy(g => g.Value, StringComparer.CurrentCultureIgnoreCase))
            Games.Add(new NotInterestedRow(key, title));
        OnPropertyChanged(nameof(HasGames));
    }
}

/// <summary>A game the player is not interested in: its title key (the saved choice) and its title.</summary>
public sealed record NotInterestedRow(string Key, string Title);
