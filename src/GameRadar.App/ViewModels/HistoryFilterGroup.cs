using CommunityToolkit.Mvvm.ComponentModel;

namespace GameRadar.App.ViewModels;

/// <summary>A box of a history filter: ticked, its games are shown.</summary>
public sealed partial class HistoryFilterChoice(string label, Func<GameGroupRow, bool> matches) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>True when the row is one of this box's games.</summary>
    public bool Matches(GameGroupRow row) => matches(row);

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = true;

    /// <summary>Only for what the history holds: a platform without games gets no box.</summary>
    [ObservableProperty]
    public partial bool IsOffered { get; set; } = true;
}

/// <summary>
/// A picklist of the history page (platform, status, taste). Within a group
/// the ticked boxes add up (Xbox or Steam); the groups narrow each other
/// (Steam and mastered). Every box ticked: every game.
/// </summary>
public sealed partial class HistoryFilterGroup : ObservableObject
{
    private readonly string _all;
    private readonly string _none;
    private readonly Action _ticked;

    // Every box ticked or unticked at once: the list is filtered once, at the end.
    private bool _bulk;

    /// <param name="all">The closed picklist when every box is ticked ("Toutes", "Tous").</param>
    /// <param name="none">When none is ("Aucune", "Aucun").</param>
    /// <param name="ticked">Called when a box is ticked or unticked: the list is filtered again.</param>
    public HistoryFilterGroup(string title, string all, string none, Action ticked,
        params (string Label, Func<GameGroupRow, bool> Matches)[] choices)
    {
        Title = title;
        _all = all;
        _none = none;
        _ticked = ticked;
        Choices = choices.Select(c => new HistoryFilterChoice(c.Label, c.Matches)).ToList();

        foreach (var choice in Choices)
        {
            choice.PropertyChanged += (_, e) =>
            {
                // A box offered or not changes the summary and the first box only; a tick refilters.
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(AllChecked));
                if (e.PropertyName == nameof(HistoryFilterChoice.IsChecked) && !_bulk)
                    ticked();
            };
        }
    }

    /// <summary>"Plateforme :", before the picklist.</summary>
    public string Title { get; }

    /// <summary>The first box of the picklist, labelled like <see cref="Summary"/> when all are ticked.</summary>
    public string AllLabel => _all;

    /// <summary>
    /// The first box: ticked when every box offered is. Unticking it unticks
    /// the boxes offered (a hidden one stays ticked: its games show once
    /// synced); ticking it (every box ticked, or only some) ticks them all.
    /// </summary>
    public bool AllChecked
    {
        get => Choices.Where(c => c.IsOffered).All(c => c.IsChecked);
        set
        {
            _bulk = true;
            try
            {
                foreach (var choice in Choices.Where(c => value || c.IsOffered))
                    choice.IsChecked = value;
            }
            finally
            {
                _bulk = false;
            }
            _ticked();
        }
    }

    public IReadOnlyList<HistoryFilterChoice> Choices { get; }

    /// <summary>The closed picklist: among the boxes offered, all ticked, none, or the ticked ones.</summary>
    public string Summary
    {
        get
        {
            var offered = Choices.Where(c => c.IsOffered).ToList();
            var ticked = offered.Where(c => c.IsChecked).Select(c => c.Label).ToList();
            return ticked.Count == offered.Count ? _all
                : ticked.Count == 0 ? _none
                : string.Join(", ", ticked);
        }
    }

    /// <summary>Some box unticked (even one not offered any more).</summary>
    public bool IsFiltered => Choices.Any(c => !c.IsChecked);

    /// <summary>True when the row is the game of a ticked box.</summary>
    public bool Passes(GameGroupRow row) => Choices.Any(c => c.IsChecked && c.Matches(row));
}
