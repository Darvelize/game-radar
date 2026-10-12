using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;

namespace GameRadar.App.ViewModels;

/// <summary>A tag under a game's name ("#Soulslike"); one the player added has a × to remove it.</summary>
public sealed record TagBadge(string Label, bool IsAdded, IRelayCommand? Remove);

/// <summary>
/// The tags the player gave a game, and the search of the "+" panel that adds
/// one from Steam's list.
/// </summary>
public sealed partial class GameTagEditor : ObservableObject
{
    private readonly List<int> _added;
    private readonly Action<IReadOnlyList<int>> _changed;
    private SteamTagChoices? _choices;

    /// <param name="added">The tags already added, in their order.</param>
    /// <param name="changed">Called with the new list after each addition or removal.</param>
    public GameTagEditor(IEnumerable<int> added, Action<IReadOnlyList<int>> changed)
    {
        _added = [.. added];
        _changed = changed;
    }

    /// <summary>The tags added, in their order.</summary>
    public IReadOnlyList<int> AddedIds => _added;

    /// <summary>
    /// A tag added or removed here since the row was built: the row stays when
    /// the list is refiltered (no row vanishes under the mouse, even after its last ×).
    /// </summary>
    public bool WasEdited { get; private set; }

    /// <summary>The names offered by the box, but those already added; empty while unknown.</summary>
    public IReadOnlyList<string> Suggestions =>
        _suggestions ??= _choices is null ? [] : _choices.Offered.Where(t => !_added.Contains(t.Id)).Select(t => t.Name).ToList();

    // Built when first read, again after a change: read by each realized row's panel.
    private IReadOnlyList<string>? _suggestions;

    /// <summary>False before the tag names are known (first launch offline): no "+".</summary>
    public bool CanAdd => _choices is not null;

    /// <summary>Suggestions containing the typed text, case, accents and spaces ignored.</summary>
    public static AutoCompleteFilterPredicate<object?> Filter { get; } = (search, item) =>
        CatalogMatcher.NormalizeName(item as string ?? "").Contains(CatalogMatcher.NormalizeName(search ?? ""));

    /// <summary>
    /// Two-way bound to the text of the box, which a picked suggestion fills.
    /// Nothing is added before Enter or "Ajouter": the box also picks an item
    /// by itself when the text matches one ("Action" while typing "Action RPG").
    /// </summary>
    [ObservableProperty]
    public partial string? Text { get; set; } = string.Empty;

    /// <summary>Enter in the box, or "Ajouter": the tag whose name is in the box.</summary>
    [RelayCommand]
    private void Add()
    {
        if (_choices?.IdOf(Text) is int id && !_added.Contains(id))
        {
            _added.Add(id);
            Changed();
        }
        Text = string.Empty;
    }

    /// <summary>The × of an added tag's badge.</summary>
    public void Remove(int id)
    {
        if (_added.Remove(id))
            Changed();
    }

    /// <summary>The names are known (or changed): the "+" and its suggestions.</summary>
    public void SetChoices(SteamTagChoices choices)
    {
        if (ReferenceEquals(choices, _choices))
            return;
        _choices = choices;
        _suggestions = null;
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(Suggestions));
    }

    private void Changed()
    {
        WasEdited = true;
        _suggestions = null;
        OnPropertyChanged(nameof(Suggestions));
        _changed(_added.ToList());
    }
}
