using GameRadar.Core.Models;

namespace GameRadar.Core.Services;

/// <summary>
/// The player's affinity for each Steam tag and each genre, learned from how
/// far they went in the games they played. Affinity is roughly between -0.5
/// (games of this kind get dropped) and 3 (always played to the end).
/// </summary>
public sealed class TasteProfile
{
    /// <summary>
    /// Bayesian smoothing: every genre starts as if it had this many neutral
    /// (0) games. A genre seen twice can then not outrank one confirmed
    /// over thirty games. Tuned on real data: with 2 or 5, a genre backed by
    /// only 2 finished games outranked genres confirmed on dozens of games
    /// (whose mean is lowered by the many quick tries typical of Game Pass).
    /// </summary>
    public const double Smoothing = 10;

    /// <summary>A game dropped under 5% counts against its genres only after this delay.</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromDays(30);

    /// <summary>A game this far counts as "went far in" its genres and tags, to explain a recommendation.</summary>
    private const double StrongEngagement = 1.5;

    private readonly Dictionary<string, double> _affinities;
    private readonly Dictionary<string, int> _strongGames;
    private readonly Dictionary<int, double> _tagAffinities;
    private readonly Dictionary<int, int> _strongTagGames;

    private TasteProfile(
        Dictionary<string, double> affinities, Dictionary<string, int> strongGames,
        Dictionary<int, double> tagAffinities, Dictionary<int, int> strongTagGames)
    {
        _affinities = affinities;
        _strongGames = strongGames;
        _tagAffinities = tagAffinities;
        _strongTagGames = strongTagGames;
    }

    /// <summary>0 for a genre never played: no opinion.</summary>
    public double AffinityOf(string genre) => _affinities.GetValueOrDefault(genre);

    /// <summary>Games of this genre the player went far in (engagement ≥ 1.5), to explain a recommendation.</summary>
    public int StrongGamesIn(string genre) => _strongGames.GetValueOrDefault(genre);

    /// <summary>True once some played games have Steam tags: candidates can be scored on tags.</summary>
    public bool HasTags => _tagAffinities.Count > 0;

    /// <summary>0 for a tag never met: no opinion.</summary>
    public double AffinityOfTag(int tag) => _tagAffinities.GetValueOrDefault(tag);

    /// <summary>Games with this Steam tag the player went far in (engagement ≥ 1.5).</summary>
    public int StrongGamesWithTag(int tag) => _strongTagGames.GetValueOrDefault(tag);

    /// <summary>
    /// How much a game tells about the player's taste. Progress is the
    /// signal: on Game Pass, launching a game costs nothing. Progress is
    /// read from the achievements and, when the platform reports it reliably
    /// (Steam), from the play time: the better of the two wins (the user's
    /// choice), so a player who ignores achievements but plays a game for
    /// hours still loves it, and many achievements in few hours count too.
    /// </summary>
    public static double Engagement(PlayedGame game, DateTimeOffset now)
    {
        if (game.Status >= PlayStatus.Completed)
            return 3;

        double? level = Max(ProgressLevel(game.ProgressPercentage), PlaytimeLevel(game.PlaytimeMinutes));
        return level ?? Dropped(game, now);
    }

    /// <summary>
    /// Launched but barely played: under 5 % of the achievements and, when
    /// the play time is known, under 2 h, and not marked finished. The same
    /// bars as <see cref="Engagement"/>'s drops. Such games are left out of
    /// the taste unless the player ticks them (<see cref="TasteSelection"/>):
    /// on Game Pass, launching a game says nothing.
    /// </summary>
    public static bool IsBarelyPlayed(PlayedGame game)
    {
        // "Pas accroché" on a game added by hand is an opinion, not a game
        // launched for a minute: it counts (as -0.5, see Engagement).
        if (game.Status >= PlayStatus.Completed || game.DeclaredByPlayer)
            return false;
        return ProgressLevel(game.ProgressPercentage) is null && PlaytimeLevel(game.PlaytimeMinutes) is null;
    }

    // Share of the achievements; null under 5 % (barely played).
    private static double? ProgressLevel(double percentage) => percentage switch
    {
        >= 50 => 2,
        >= 25 => 1.5,
        >= 5 => 0.5,
        _ => null
    };

    // Hours played, thresholds chosen by the user (on their Steam library,
    // ~20 h matches roughly 50 % of the achievements); null under 2 h or
    // when the platform does not report it (Xbox). Never 3: only a manual
    // "Histoire finie" or a platinum says a game was finished.
    private static double? PlaytimeLevel(int? minutes) => (minutes / 60.0) switch
    {
        >= 20 => 2,
        >= 10 => 1.5,
        >= 2 => 0.5,
        _ => null
    };

    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    // Barely played: dropped, unless recently played (maybe just started).
    // Only reached for a barely played game the player chose to count.
    private static double Dropped(PlayedGame game, DateTimeOffset now) =>
        game.LastPlayed is { } last && now - last <= AbandonedAfter ? 0 : -0.5;

    /// <summary>Main genre counts fully, secondary ones half.</summary>
    public static double GenreWeight(int position) => position == 0 ? 1 : 0.5;

    /// <summary>
    /// Learns the affinities twice, the same way: per Steam tag (fine: "Open
    /// World", "Souls-like", "Story Rich"), the user's choice for every game
    /// that has tags, whatever its platform; and per Store genre (coarse),
    /// kept for the candidates without Steam tags. A tag weighs its relative
    /// votes on the game (the most voted 1), like a genre its position.
    /// </summary>
    /// <param name="games">
    /// The games that count, already chosen by <see cref="TasteSelection.Counts"/>
    /// (the player's choices, then the automatic rule): all of them are learned.
    /// </param>
    public static TasteProfile Build(
        IEnumerable<PlayedGameWithGenres> games,
        DateTimeOffset now)
    {
        var genres = new AffinityCounter<string>();
        var tags = new AffinityCounter<int>();

        // "_" discards the play modes: they only serve the similarity search.
        foreach (var (game, gameGenres, _, gameTags) in games)
        {
            double engagement = Engagement(game, now);
            for (int i = 0; i < gameGenres.Count; i++)
                genres.Add(gameGenres[i], GenreWeight(i), engagement);
            foreach (var (tag, weight) in gameTags ?? new Dictionary<int, double>())
                tags.Add(tag, weight, engagement);
        }

        return new TasteProfile(genres.Means(), genres.StrongGames, tags.Means(), tags.StrongGames);
    }

    /// <summary>Weighted engagement sums per genre or tag, then their smoothed means.</summary>
    private sealed class AffinityCounter<TKey> where TKey : notnull
    {
        private readonly Dictionary<TKey, double> _weightedSum = new();
        private readonly Dictionary<TKey, double> _weightTotal = new();

        public Dictionary<TKey, int> StrongGames { get; } = new();

        public void Add(TKey key, double weight, double engagement)
        {
            _weightedSum[key] = _weightedSum.GetValueOrDefault(key) + engagement * weight;
            _weightTotal[key] = _weightTotal.GetValueOrDefault(key) + weight;
            if (engagement >= StrongEngagement)
                StrongGames[key] = StrongGames.GetValueOrDefault(key) + 1;
        }

        // Weighted mean pulled towards 0 by the smoothing: a mean rather than
        // a sum, so a genre is not favored just because the catalog has many.
        public Dictionary<TKey, double> Means() => _weightedSum.ToDictionary(
            kv => kv.Key,
            kv => kv.Value / (_weightTotal[kv.Key] + Smoothing));
    }
}
