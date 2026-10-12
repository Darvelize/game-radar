using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.Core.EA;
using GameRadar.Core.Epic;
using GameRadar.Core.GOG;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;
using GameRadar.Core.Xbox;

namespace GameRadar.App.ViewModels;

/// <summary>
/// One game of the history page, whatever the number of platforms it was
/// played on. The header shows the platform where the player went furthest;
/// a game on several platforms (Warframe on Xbox and Steam) unfolds into one
/// detail line per platform.
/// </summary>
public sealed partial class GameGroupRow : ObservableObject
{
    private readonly GameRow _best;
    private readonly Action<GameGroupRow>? _onUserChoiceChanged;
    // The player's explicit choices (live settings sets, read only here).
    private readonly IReadOnlySet<string> _excludedFromTaste;
    private readonly IReadOnlySet<string> _includedInTaste;
    // True while the heart follows the rule after a "finished" mark: not a click.
    private bool _followingRule;

    /// <param name="games">The same game on each platform.</param>
    /// <param name="excludedFromTaste">Keys the user unticked from their taste profile.</param>
    /// <param name="includedInTaste">Keys the user ticked although barely played.</param>
    /// <param name="markedCompleted">Keys the user marked as finished.</param>
    /// <param name="now">To judge how far the player went (a recent start is not a drop).</param>
    /// <param name="onUserChoiceChanged">Called when the user ticks or unticks one of the boxes.</param>
    /// <param name="manualEditorFor">The editor of a game added by hand, null for a synced one.</param>
    /// <param name="tagEditor">The tags the player added to the game; none when null.</param>
    public GameGroupRow(
        IReadOnlyList<(PlayedGame Game, CatalogGame? Match)> games,
        IReadOnlySet<string> excludedFromTaste,
        IReadOnlySet<string> includedInTaste,
        IReadOnlySet<string> markedCompleted,
        DateTimeOffset now,
        Action<GameGroupRow> onUserChoiceChanged,
        Func<PlayedGame, ManualGameEditor?>? manualEditorFor = null,
        GameTagEditor? tagEditor = null)
    {
        TagEditor = tagEditor ?? new GameTagEditor([], _ => { });
        _excludedFromTaste = excludedFromTaste;
        _includedInTaste = includedInTaste;
        // Most recently played first: the header shows that date.
        games = games.OrderByDescending(g => g.Game.LastPlayed).ToList();
        _games = games.Select(g => g.Game).ToList();
        _platforms = games.Select(g => g.Game.Platform).ToHashSet();
        Details = games
            .Select(g => new GameRow(g.Game, g.Match, markedCompleted.Contains(g.Game.Key), manualEditorFor?.Invoke(g.Game)))
            .ToList();
        // Same rule as the taste profile (HistoryMerger.KeepBestPerGame):
        // the line where the player went furthest speaks for the game.
        // MaxBy keeps the first of equals: the most recently played.
        int bestIndex = games
            .Select((g, i) => (Engagement: TasteProfile.Engagement(g.Game, now), Index: i))
            .MaxBy(x => x.Engagement).Index;
        _best = Details[bestIndex];

        var first = Details[0];
        Name = first.Name;
        LastPlayedLabel = first.LastPlayedLabel;
        SearchKey = CatalogMatcher.NormalizeName(Name);
        IsMastered = Details.Any(d => d.IsMastered);
        // Added by hand: its platform as a badge, and a line to edit it.
        var manual = Details.FirstOrDefault(d => d.IsManual);
        ManualPlatformLabel = manual?.PlatformName;
        _manualPlatform = manual?.Manual?.SelectedPlatform.Platform;
        // Finished by its own account: a level "Terminé" added by hand, or a
        // PlayStation platinum (the platform says so: no flag to tick).
        _declaredCompleted = Details.Any(d => (d.IsManual && d.IsCompleted) || d.CompletedOnPlatform);

        // Matches arrive as the player's plan sees them: a game of a higher
        // plan is in the catalog but not in "their" Game Pass.
        var match = Details.Select(d => d.GamePassMatch).FirstOrDefault(m => m is { InGamePass: true });
        IsInGamePass = match is not null;

        CanCountInTaste = Details.Any(d => d.CanCountInTaste);
        // Finished according to a platform (a PlayStation platinum without the
        // DLC trophies): the flag shows ticked, and the player cannot untick it.
        // Mastered somewhere: already at the top, no flag at all.
        IsCompletedOnPlatform = !IsMastered && Details.Any(d => d.CompletedOnPlatform);
        CanMarkCompleted = !IsMastered && !IsCompletedOnPlatform && Details.Any(d => d.CanMarkCompleted);

        // Initial values set BEFORE storing the callback: building the row
        // must not look like a user click.
        IsCompleted = Details.Any(d => d.CanMarkCompleted && d.IsCompleted);
        CountsInTaste = EffectiveCountsInTaste();
        _onUserChoiceChanged = onUserChoiceChanged;
    }

    /// <summary>One line per platform, most recently played first.</summary>
    public IReadOnlyList<GameRow> Details { get; }

    /// <summary>Played on several platforms: the header unfolds into the details.</summary>
    public bool IsMerged => Details.Count > 1;

    /// <summary>
    /// Unfolded, it shows a line per platform, or the line that edits a game
    /// added by hand (every row unfolds, for its tags).
    /// </summary>
    public bool HasDetails => IsMerged || IsManual;

    /// <summary>The tags the player added to the game, edited when the row is unfolded.</summary>
    public GameTagEditor TagEditor { get; }

    /// <summary>Where a game added by hand was played ("PlayStation"), for its badge; null otherwise.</summary>
    public string? ManualPlatformLabel { get; }

    public bool IsManual => ManualPlatformLabel is not null;

    // The badge of a game added by hand: the color of its platform, like a
    // synced game (never twice: when a synced game of the same platform is
    // merged in, its own badge already says it). The other platforms stay neutral.
    private readonly ManualPlatform? _manualPlatform;

    public bool ShowManualPlayStationBadge => _manualPlatform == ManualPlatform.PlayStation && !IsPlayStation;

    public bool ShowManualXboxBadge => _manualPlatform == ManualPlatform.OldXbox && !IsXbox;

    public bool ShowManualNeutralBadge => IsManual && _manualPlatform is not (ManualPlatform.PlayStation or ManualPlatform.OldXbox);

    private readonly HashSet<string> _platforms;

    /// <summary>True when the game was played on this platform (a <see cref="PlayedGame.Platform"/>).</summary>
    public bool HasPlatform(string platform) => _platforms.Contains(platform);

    // "Terminé" chosen for a game added by hand: a level, not the "finished" mark.
    private readonly bool _declaredCompleted;

    public string Name { get; }
    /// <summary>The most recent session, on any platform.</summary>
    public string LastPlayedLabel { get; }
    // Its badges: one fact, the platforms it was played on.
    public bool IsXbox => HasPlatform(XboxLiveClient.PlatformName);
    public bool IsSteam => HasPlatform(SteamClient.PlatformName);
    public bool IsPlayStation => HasPlatform(PlayStationClient.PlatformName);
    public bool IsGog => HasPlatform(GogClient.PlatformName);
    public bool IsEa => HasPlatform(EaClient.PlatformName);
    public bool IsEpic => HasPlatform(EpicClient.PlatformName);
    public bool IsMastered { get; }
    /// <summary>In the player's Game Pass plan: counted in the catalog status, no badge (the history only tells the platform).</summary>
    public bool IsInGamePass { get; }
    public bool CanCountInTaste { get; }
    public bool CanMarkCompleted { get; }
    /// <summary>Finished according to a platform: a ticked flag the player cannot change.</summary>
    public bool IsCompletedOnPlatform { get; }

    /// <summary>Name without case, accents or symbols, for the search box.</summary>
    public string SearchKey { get; }

    // Progress of the platform where the player went furthest.
    public string AchievementsLabel => _best.AchievementsLabel;
    public bool HasProgress => _best.HasProgress;
    public double Progress => _best.Progress;

    public string StatusLabel => IsMastered ? Strings.Status_Mastered
        : IsCompleted || _declaredCompleted ? Strings.Status_Completed
        : Details.All(d => d.NeverPlayed) ? Strings.Status_NotLaunched
        : Strings.Status_Played;

    /// <summary>Finished or mastered: the status badge uses the accent color.</summary>
    public bool IsStatusHighlighted => IsMastered || IsCompleted || _declaredCompleted;

    /// <summary>Two-way bound to the header: unfolds the tags, and the details when it has some.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    // A row without details does not unfold, whoever asks (a screen reader may toggle the name).
    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !HasDetails)
            IsExpanded = false;
    }

    // The game on each platform: its Steam tags are looked up by any of them.
    private readonly List<PlayedGame> _games;

    /// <summary>
    /// "#Metroidvania #Soulslike #Action" under the name: the tags the player
    /// added (with their ×), then Steam's, 3 at least; empty while unknown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddTagLabel))]
    [NotifyPropertyChangedFor(nameof(HasTagLine))]
    public partial IReadOnlyList<TagBadge> TagBadges { get; set; } = [];

    /// <summary>The "+" after the badges, "+ Tag" alone for a game without any.</summary>
    public string AddTagLabel => TagBadges.Count > 0 ? Strings.History_AddTag_More : Strings.History_AddTag_First;

    /// <summary>Badges or a "+" to show: no empty line (first launch offline).</summary>
    public bool HasTagLine => TagBadges.Count > 0 || TagEditor.CanAdd;

    /// <summary>Known to have no tag at all, neither from Steam nor added; false while the tags are unknown.</summary>
    public bool LacksTags { get; private set; }

    /// <summary>Weighs in the recommendations without any tag: what the "Sans tags" box shows.</summary>
    public bool CountsWithoutTags => CountsInTaste && LacksTags;

    /// <summary>
    /// Its badges: the tags the player added, in their order, then its most
    /// voted Steam tags (from the first of its platforms Steam knows) up to 3.
    /// </summary>
    public void ShowTags(SteamTagIndex index, SteamTagChoices choices)
    {
        var tags = index.ForGame(_games);
        LacksTags = tags is null;
        var added = TagEditor.AddedIds;
        // A tag Steam no longer names still counts: shown by its number, so it can be removed.
        string Label(int id) => choices.Names.GetValueOrDefault(id) is { } name
            ? Localization.Format(Strings.Tag_Badge, name)
            : Localization.Format(Strings.Tag_Badge_Unknown, id);
        var steamOnes = SteamTagIndex.TopTags(tags, choices.Names, count: 3 + added.Count)
            .Where(t => !added.Contains(t.Id))
            .Take(Math.Max(0, 3 - added.Count));
        var badges = added
            .Select(id => new TagBadge(Label(id), IsAdded: true, new RelayCommand(() => TagEditor.Remove(id))))
            .Concat(steamOnes.Select(t => new TagBadge(Label(t.Id), IsAdded: false, Remove: null)))
            .ToList();

        bool couldAdd = TagEditor.CanAdd;
        TagEditor.SetChoices(choices);
        if (TagEditor.CanAdd != couldAdd)
            OnPropertyChanged(nameof(HasTagLine));
        // Replaced only when they change: every row is relabelled on each click
        // of a "+" or a ×, and new badges would lose the keyboard focus of a row.
        if (!badges.Select(b => (b.Label, b.IsAdded)).SequenceEqual(TagBadges.Select(b => (b.Label, b.IsAdded))))
            TagBadges = badges;
    }

    /// <summary>
    /// Two-way bound to the "Compte dans mes goûts" toggle, for every platform.
    /// Ticked when one of its platforms counts: the player's choice, else the
    /// rule (barely played games are left out, see TasteSelection).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TasteTip))]
    public partial bool CountsInTaste { get; set; }

    /// <summary>Left out by the rule on every platform: launched but barely played.</summary>
    public bool IsBarelyPlayed => Details.Where(d => d.CanCountInTaste).All(d => !d.CountsByDefault);

    /// <summary>Tooltip of the heart: why the game counts or not.</summary>
    public string TasteTip => CountsInTaste ? Strings.Taste_Counts_Tip
        : IsBarelyPlayed ? Strings.Taste_BarelyPlayed_Tip
        : Strings.Taste_Ignored_Tip;

    // The player's choice where there is one, else the rule, platform by platform.
    private bool EffectiveCountsInTaste() =>
        Details.Where(d => d.CanCountInTaste).Any(d =>
            _includedInTaste.Contains(d.Key) || (!_excludedFromTaste.Contains(d.Key) && d.CountsByDefault));

    /// <summary>Two-way bound to the "Histoire finie" toggle, for every platform.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(IsStatusHighlighted))]
    public partial bool IsCompleted { get; set; }

    // Hooks generated by the MVVM Toolkit, called after each change.
    partial void OnCountsInTasteChanged(bool value)
    {
        if (!_followingRule)
            _onUserChoiceChanged?.Invoke(this);
    }

    partial void OnIsCompletedChanged(bool value)
    {
        // The detail lines follow, where the mark means something.
        foreach (var detail in Details.Where(d => d.CanMarkCompleted))
            detail.IsCompleted = value;

        // A barely played game marked finished counts by the rule: the heart
        // follows (it is not a choice of the player, so nothing is saved for it).
        _followingRule = true;
        CountsInTaste = EffectiveCountsInTaste();
        _followingRule = false;
        OnPropertyChanged(nameof(TasteTip));

        _onUserChoiceChanged?.Invoke(this);
    }
}
