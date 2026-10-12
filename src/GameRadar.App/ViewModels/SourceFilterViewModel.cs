using CommunityToolkit.Mvvm.ComponentModel;
using GameRadar.Core.Models;
using GameRadar.Core.Settings;

namespace GameRadar.App.ViewModels;

// Where the recommendations come from: the "Game Pass", "PS Plus" and "Steam"
// boxes of the "Pour toi" page, and the player's plans chosen in Settings.
// All of them are remembered between sessions.
public sealed partial class SourceFilterViewModel : ObservableObject
{
    private readonly SettingsSaver _saver;

    /// <summary>A box was ticked: the window writes the settings (and says so when it fails).</summary>
    public event Action? SaveRequested;

    /// <summary>A box, or the plan: the cards are rebuilt from the last computation.</summary>
    public event Action? Changed;

    /// <summary>Another plan, other candidates: the history badges and the recommendations are rebuilt.</summary>
    public event Action? PlanChanged;

    /// <summary>Another PS Plus plan: its catalog may have to be downloaded before the recommendations follow.</summary>
    public event Action? PsPlusPlanChanged;

    // Events, not constructor callbacks: the window listens once everything exists, and the
    // boxes set from the settings below (the hooks run then) are heard by nobody.
    public SourceFilterViewModel(SettingsSaver saver)
    {
        _saver = saver;
        var settings = saver.Settings;
        SelectedGamePassPlan = GamePassPlans.FirstOrDefault(o => o.Plan == settings.GamePassPlan)
            ?? GamePassPlans.First(o => o.Plan == GamePassTier.Ultimate);
        SelectedPsPlusPlan = PsPlusPlans.FirstOrDefault(o => o.Plan == settings.PsPlusPlan) ?? PsPlusPlans[0];
        ShowGamePass = settings.ShowGamePass;
        ShowPsPlus = settings.ShowPsPlus;
        ShowSteam = settings.ShowSteam;
        ShowGog = settings.ShowGog;
        ShowEpic = settings.ShowEpic;
        // The rule comes once the history is known (ApplyPlayStationRule).
        ShowPlayStationStore = settings.ShowPlayStationStore ?? false;
    }

    /// <summary>The "PlayStation Store" box was ticked (by the player or the rule): its catalog may have to be downloaded.</summary>
    public event Action? PlayStationStoreTicked;

    // Set while the box follows the rule: only a click is the player's choice, and saved.
    private bool _applyingRule;

    /// <summary>"Game Pass" box of the filter: the games of the player's plan. Remembered between sessions.</summary>
    [ObservableProperty]
    public partial bool ShowGamePass { get; set; }

    /// <summary>"PS Plus" box of the filter: the games of the player's PS Plus plan. Remembered between sessions.</summary>
    [ObservableProperty]
    public partial bool ShowPsPlus { get; set; }

    /// <summary>"Steam" box of the filter: the games sold on Steam. Remembered between sessions.</summary>
    [ObservableProperty]
    public partial bool ShowSteam { get; set; }

    /// <summary>"GOG" box of the filter: the games sold on GOG. Remembered between sessions.</summary>
    [ObservableProperty]
    public partial bool ShowGog { get; set; }

    /// <summary>"Epic" box of the filter: the games sold on Epic. Remembered between sessions.</summary>
    [ObservableProperty]
    public partial bool ShowEpic { get; set; }

    /// <summary>
    /// "PlayStation Store" box of the filter: the games sold on the PlayStation Store.
    /// Ticked for a player with a PlayStation history until they choose; the choice is remembered.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowPlayStationStore { get; set; }

    /// <summary>The box follows the history (PlayStation games or not), unless the player chose.</summary>
    public void ApplyPlayStationRule(bool hasPlayStationHistory)
    {
        if (_saver.Settings.ShowPlayStationStore is not null)
            return;
        _applyingRule = true;
        ShowPlayStationStore = hasPlayStationHistory;
        _applyingRule = false;
    }

    /// <summary>
    /// The sources the cards come from: box ticked, and usable (a subscription box
    /// needs a plan), in the order of the merge. A new source is one more line here.
    /// </summary>
    public IReadOnlyList<CatalogSource> Ticked => Enum.GetValues<CatalogSource>().Where(source => source switch
    {
        CatalogSource.GamePass => ShowGamePass && HasGamePassPlan,
        CatalogSource.PsPlus => ShowPsPlus && HasPsPlusPlan,
        CatalogSource.Steam => ShowSteam,
        CatalogSource.Gog => ShowGog,
        CatalogSource.Epic => ShowEpic,
        CatalogSource.PlayStationStore => ShowPlayStationStore,
        _ => false
    }).ToList();

    /// <summary>Its name in "Jeux proposés : …": "Game Pass Ultimate", "catalogue Steam"…</summary>
    public string Label(CatalogSource source) => source switch
    {
        CatalogSource.GamePass => GamePassLabel,
        CatalogSource.PsPlus => PsPlusLabel,
        CatalogSource.Steam => Strings.Reco_SourceSteam,
        CatalogSource.Gog => Strings.Reco_SourceGog,
        CatalogSource.Epic => Strings.Reco_SourceEpic,
        CatalogSource.PlayStationStore => Strings.Reco_SourcePsStore,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };

    // Hooks generated by the MVVM Toolkit. Each one saves ITS setting only:
    // the constructor sets the boxes one after the other, and copying both
    // at the first one would save the other one before it is loaded.
    partial void OnShowGamePassChanged(bool value) => Remember(value, _saver.Settings.ShowGamePass, s => s.ShowGamePass = value);
    partial void OnShowPsPlusChanged(bool value) => Remember(value, _saver.Settings.ShowPsPlus, s => s.ShowPsPlus = value);
    partial void OnShowSteamChanged(bool value) => Remember(value, _saver.Settings.ShowSteam, s => s.ShowSteam = value);
    partial void OnShowGogChanged(bool value) => Remember(value, _saver.Settings.ShowGog, s => s.ShowGog = value);
    partial void OnShowEpicChanged(bool value) => Remember(value, _saver.Settings.ShowEpic, s => s.ShowEpic = value);

    // A box set to what is saved (the constructor) saves nothing; the cards follow either way.
    private void Remember(bool value, bool saved, Action<AppSettings> save)
    {
        if (saved != value)
        {
            save(_saver.Settings);
            SaveRequested?.Invoke();
        }
        Changed?.Invoke();
    }

    partial void OnShowPlayStationStoreChanged(bool value)
    {
        if (!_applyingRule && _saver.Settings.ShowPlayStationStore != value)
        {
            _saver.Settings.ShowPlayStationStore = value;
            SaveRequested?.Invoke();
        }
        Changed?.Invoke();
        if (value)
            PlayStationStoreTicked?.Invoke();
    }

    /// <summary>A choice of the "Mon Game Pass" list.</summary>
    public sealed record GamePassPlanOption(GamePassTier Plan, string Label, string ShortLabel);

    public IReadOnlyList<GamePassPlanOption> GamePassPlans { get; } =
    [
        new(GamePassTier.None, Strings.Plan_None, string.Empty),
        new(GamePassTier.Essential, "Game Pass Essential", "Essential"),
        new(GamePassTier.Premium, "Game Pass Premium", "Premium"),
        new(GamePassTier.Ultimate, "Game Pass Ultimate", "Ultimate"),
        new(GamePassTier.PcGamePass, "PC Game Pass", "PC")
    ];

    /// <summary>Two-way bound to the "Mon Game Pass" list of the settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGamePassPlan), nameof(GamePassLabel))]
    public partial GamePassPlanOption SelectedGamePassPlan { get; set; }

    public GamePassTier GamePassPlan => SelectedGamePassPlan.Plan;

    /// <summary>No subscription: no "Game Pass" box.</summary>
    public bool HasGamePassPlan => GamePassPlan != GamePassTier.None;

    /// <summary>Label of the "Game Pass" box, with the plan: "Game Pass Premium".</summary>
    public string GamePassLabel => SelectedGamePassPlan.Label;

    partial void OnSelectedGamePassPlanChanged(GamePassPlanOption value)
    {
        // Also called by the constructor with the saved plan: nothing to do then.
        if (_saver.Settings.GamePassPlan == value.Plan)
            return;

        _saver.Settings.GamePassPlan = value.Plan;
        PlanChanged?.Invoke();
    }

    /// <summary>A choice of the "Mon PS Plus" list.</summary>
    public sealed record PsPlusPlanOption(PsPlusTier Plan, string Label);

    public IReadOnlyList<PsPlusPlanOption> PsPlusPlans { get; } =
    [
        new(PsPlusTier.None, Strings.Plan_None),
        new(PsPlusTier.Extra, "PlayStation Plus Extra"),
        new(PsPlusTier.Premium, "PlayStation Plus Premium")
    ];

    /// <summary>Two-way bound to the "Mon PS Plus" list of the settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPsPlusPlan), nameof(PsPlusLabel))]
    public partial PsPlusPlanOption SelectedPsPlusPlan { get; set; }

    public PsPlusTier PsPlusPlan => SelectedPsPlusPlan.Plan;

    /// <summary>No subscription: no "PS Plus" box.</summary>
    public bool HasPsPlusPlan => PsPlusPlan != PsPlusTier.None;

    /// <summary>Label of the "PS Plus" box, with the plan: "PlayStation Plus Extra".</summary>
    public string PsPlusLabel => SelectedPsPlusPlan.Label;

    partial void OnSelectedPsPlusPlanChanged(PsPlusPlanOption value)
    {
        // Also called by the constructor with the saved plan: nothing to do then.
        if (_saver.Settings.PsPlusPlan == value.Plan)
            return;

        _saver.Settings.PsPlusPlan = value.Plan;
        PsPlusPlanChanged?.Invoke();
    }
}
