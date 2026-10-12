using CommunityToolkit.Mvvm.ComponentModel;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.App.ViewModels;

/// <summary>
/// One game on one platform, with every label already formatted: the detail
/// lines of a <see cref="GameGroupRow"/>, whose header reuses the best one.
/// The XAML only displays these strings: no logic in the view.
/// </summary>
public sealed partial class GameRow : ObservableObject
{
    /// <param name="game">The game as detected from achievements (without manual marks).</param>
    /// <param name="gamePassMatch">The same game in the Game Pass catalog, if it is there.</param>
    /// <param name="isCompleted">True when the user marked the story as finished.</param>
    /// <param name="manual">For a game added by hand: what the line edits (platform, how far).</param>
    public GameRow(PlayedGame game, CatalogGame? gamePassMatch, bool isCompleted, ManualGameEditor? manual = null)
    {
        Key = game.Key;
        Name = game.Name;
        PlatformName = game.Platform;
        IsMastered = game.Status == PlayStatus.Mastered;
        NeverPlayed = game.NeverPlayed;
        string achievements = (game.HasAchievements, game.TotalAchievements) switch
        {
            (false, _) => Strings.Row_NoAchievements,
            (true, int total) => Localization.Format(Strings.Row_Achievements, game.UnlockedAchievements, total),
            // No count at all, only a percentage (GOG): "0 succès" would be wrong.
            (true, null) when game.UnlockedAchievements == 0 && game.ProgressPercentage > 0 =>
                Localization.Format(Strings.Row_AchievementsPercentOnly, game.ProgressPercentage),
            // Total unknown: the percentage still tells how far the player went.
            (true, null) => Localization.Format(Strings.Row_AchievementsPercent, game.UnlockedAchievements, game.ProgressPercentage)
        };
        // Steam knows the play time: shown next to the achievements.
        AchievementsLabel = game.PlaytimeMinutes is > 0 and int minutes
            ? $"{achievements} · {FormatPlaytime(minutes)}"
            : achievements;
        LastPlayedLabel = game.LastPlayed is { } date
            ? Localization.Format(Strings.Row_PlayedOn, date.ToLocalTime())
            : game.NeverPlayed ? Strings.Row_NeverLaunched : Strings.Row_UnknownDate;

        GamePassMatch = gamePassMatch;

        // Only games with achievements or a play time feed the taste profile.
        CanCountInTaste = game.HasEngagementSignal;
        _barelyPlayed = TasteProfile.IsBarelyPlayed(game);
        // Finished according to the platform itself (a PlayStation platinum
        // without the DLC trophies): "game" comes without the manual marks.
        CompletedOnPlatform = game.Status == PlayStatus.Completed && manual is null;
        // A mastered game is already at the top: "finished" would add nothing.
        // Added by hand: "finished" is one of its levels, not a mark; same
        // when the platform says it.
        CanMarkCompleted = game.HasEngagementSignal && !IsMastered && manual is null && !CompletedOnPlatform;
        HasProgress = game.HasAchievements;
        Progress = game.ProgressPercentage;
        IsCompleted = isCompleted || CompletedOnPlatform;

        // Added by hand: its platform and how far the player said they went.
        Manual = manual;
        if (manual is not null)
        {
            PlatformName = manual.SelectedPlatform.Label;
            AchievementsLabel = manual.SelectedLevel.Label;
            LastPlayedLabel = Strings.Row_AddedByHand;
            IsCompleted = game.Status == PlayStatus.Completed;
        }
    }

    /// <summary>For a game added by hand, what its line edits; null otherwise.</summary>
    public ManualGameEditor? Manual { get; }

    public bool IsManual => Manual is not null;

    /// <summary>Finished according to the platform (a PlayStation platinum), not marked by the player.</summary>
    public bool CompletedOnPlatform { get; }

    public string Key { get; }
    public string Name { get; }
    /// <summary>"Xbox", "Steam", or where a game added by hand was played ("PlayStation").</summary>
    public string PlatformName { get; }
    public bool IsMastered { get; }
    /// <summary>Owned but never launched (Steam library).</summary>
    public bool NeverPlayed { get; }
    /// <summary>True when the small achievement bar means something.</summary>
    public bool HasProgress { get; }
    public string AchievementsLabel { get; }
    public string LastPlayedLabel { get; }
    public CatalogGame? GamePassMatch { get; }
    public bool CanCountInTaste { get; }

    // Launched but barely played (as detected, before the manual mark).
    private readonly bool _barelyPlayed;

    /// <summary>
    /// What the automatic rule says (<see cref="TasteSelection"/>): barely
    /// played games are left out, unless marked finished. Recomputed when
    /// the mark changes.
    /// </summary>
    public bool CountsByDefault => CanCountInTaste && (IsCompleted || !_barelyPlayed);
    public bool CanMarkCompleted { get; }

    /// <summary>Achievement completion, 0 to 100, for the small progress bar.</summary>
    public double Progress { get; }

    /// <summary>Computed from the detected status and the manual mark.</summary>
    public string StatusLabel => IsMastered ? Strings.Status_Mastered
        : IsCompleted ? Strings.Status_Completed
        : NeverPlayed ? Strings.Status_NotLaunched
        : Strings.Status_Played;

    /// <summary>"45 min" under an hour, then whole hours: "12 h".</summary>
    public static string FormatPlaytime(int minutes) => minutes < 60
        ? Localization.Format(Strings.Playtime_Minutes, minutes)
        : Localization.Format(Strings.Playtime_Hours, minutes / 60);

    /// <summary>Finished or mastered: the status badge uses the accent color.</summary>
    public bool IsStatusHighlighted => IsMastered || IsCompleted;

    /// <summary>
    /// The manual "finished" mark, as the group header last set it. Only
    /// platforms where the mark means something take it (see CanMarkCompleted).
    /// </summary>
    [ObservableProperty]
    // Both labels depend on IsCompleted: tell the view to re-read them too.
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(IsStatusHighlighted))]
    public partial bool IsCompleted { get; set; }
}
