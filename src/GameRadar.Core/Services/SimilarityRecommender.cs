using GameRadar.Core.Models;

namespace GameRadar.Core.Services;

/// <summary>A recommended game and what it shares with the favorite it resembles.</summary>
/// <param name="ByTags">Compared on Steam tags (fine); false = on genres and modes (coarse fallback).</param>
/// <param name="SharedTags">Most telling tags both games have, strongest first (empty when not by tags).</param>
public sealed record SimilarGame(
    Recommendation Recommendation,
    double Similarity,
    IReadOnlyList<string> SharedGenres,
    PlayModes SharedModes,
    bool ByTags = false,
    IReadOnlyList<int>? SharedTags = null);

/// <summary>"Because you liked X": games close to one favorite.</summary>
public sealed record RecommendationSection(PlayedGame Favorite, IReadOnlyList<SimilarGame> Games);

/// <summary>A game leaving the Game Pass soon, and the favorite it resembles most.</summary>
public sealed record LeavingSoonPick(SimilarGame Game, PlayedGame ClosestFavorite);

/// <summary>
/// Games leaving soon that match the player, then sections per favorite,
/// then the best remaining games by genre taste.
/// </summary>
public sealed record SimilarityResult(
    IReadOnlyList<LeavingSoonPick> LeavingSoon,
    IReadOnlyList<RecommendationSection> Sections,
    IReadOnlyList<Recommendation> ByGenres);

/// <summary>
/// Recommends games that resemble the player's favorites, compared with the
/// cosine similarity of feature vectors. Two games that both have Steam tags
/// are compared on their TF-IDF tag vectors ("Looter Shooter", "Action RPG":
/// Warframe is close to The Division 2, not to Call of Duty); otherwise on
/// the coarse Store genres and play modes.
/// </summary>
public static class SimilarityRecommender
{
    /// <summary>Favorites: games played at least this far (≥ 50 %, finished or mastered).</summary>
    public const double FavoriteEngagement = 2;

    /// <summary>Below this similarity on genres and modes, a game is not "like" the favorite.</summary>
    public const double MinSimilarity = 0.8;

    /// <summary>
    /// Same, on TF-IDF tags, whose scores are lower: on real data, the
    /// looter-shooters are at 0.40-0.67 from Warframe, Diablo IV at 0.23,
    /// Call of Duty at 0.08. Validated by the user on their 52 favorites:
    /// the sections show the 5 closest (mostly ≥ 0.45), so this bar mainly
    /// decides the "leaving soon" picks and the favorites with few neighbors.
    /// </summary>
    public const double MinTagSimilarity = 0.35;

    /// <summary>
    /// A favorite this close to an already chosen one gets no section of its
    /// own: two near-identical sections would repeat the same games.
    /// </summary>
    public const double MaxSimilarityBetweenFavorites = 0.9;

    /// <summary>Same, on tags (Hellblade and Hellblade II).</summary>
    public const double MaxTagSimilarityBetweenFavorites = 0.7;

    /// <summary>Weight of each play mode, next to genres (main 1, secondary 0.5).</summary>
    public const double ModeWeight = 0.5;

    /// <summary>
    /// A game this close to one the player said "Pas intéressé" to, on tags,
    /// is the same kind of game (another Call of Duty): hidden too. Same bar
    /// as two favorites deemed near-duplicates.
    /// </summary>
    public const double RefusedNeighborTagSimilarity = MaxTagSimilarityBetweenFavorites;

    // Tags named in a card's explanation.
    private const int SharedTagCount = 3;

    /// <param name="played">
    /// The games that count, already chosen by <see cref="TasteSelection.Counts"/>:
    /// the favorites are taken among them.
    /// </param>
    /// <param name="tagVectorizer">
    /// Tag rarity; by default measured on <paramref name="candidates"/>. Pass
    /// one built on every candidate to keep the same weights when a filter
    /// narrows the candidates.
    /// </param>
    /// <param name="notInterested">
    /// Games the player said "Pas intéressé" to, with their tags: never shown
    /// (same title key, <see cref="GameTitle.Key"/>), and a negative signal on
    /// tags only (the Store genres are too coarse: refusing a Call of Duty
    /// would hide every shooter). A game is hidden when it is closer to a
    /// refused game than to the favorite it would be shown for, or when it is
    /// a near-duplicate of a refused game (<see cref="RefusedNeighborTagSimilarity"/>).
    /// </param>
    public static SimilarityResult Build(
        IEnumerable<PlayedGameWithGenres> played,
        IReadOnlyList<Recommendation> candidates,
        DateTimeOffset now,
        int maxSections = 6,
        int gamesPerSection = 5,
        int byGenresCount = 10,
        int leavingSoonCount = 5,
        TagVectorizer? tagVectorizer = null,
        IReadOnlyCollection<CatalogGame>? notInterested = null)
    {
        var vectorizer = tagVectorizer ?? new TagVectorizer(candidates.Select(c => c.Game.Tags));

        // The refused games themselves leave the candidates; their tag vectors
        // stay, to recognize the games that look like them.
        var refusedKeys = (notInterested ?? []).Select(g => GameTitle.Key(g.Title)).ToHashSet();
        var refusedVectors = (notInterested ?? [])
            .Select(g => new Features([], PlayModes.None, vectorizer.Vector(g.Tags)))
            .Where(f => f.TagVector is not null)
            .ToList();
        // Rebuilt at every click (filter, "Pas intéressé"): no title key to
        // compute when nothing was refused.
        if (refusedKeys.Count > 0)
            candidates = candidates.Where(c => !refusedKeys.Contains(GameTitle.Key(c.Game.Title))).ToList();

        // Strongest favorites first; among equals, the most recently played.
        var favorites = played
            .Where(p => TasteProfile.Engagement(p.Game, now) >= FavoriteEngagement)
            .OrderByDescending(p => TasteProfile.Engagement(p.Game, now))
            .ThenByDescending(p => p.Game.LastPlayed)
            .Select(p => (p.Game, Features: new Features(p.Genres, p.Modes, vectorizer.Vector(p.Tags))))
            .ToList();

        // Out: the near-duplicates of a refused game, whatever the section.
        var candidateFeatures = candidates
            .Select(c => (Recommendation: c, Features: new Features(c.Game.Genres, c.Game.Modes, vectorizer.Vector(c.Game.Tags))))
            .Select(c => (c.Recommendation, c.Features, Refused: RefusedSimilarity(c.Features, refusedVectors)))
            .Where(c => c.Refused < RefusedNeighborTagSimilarity)
            .ToList();
        var shownIds = candidateFeatures.Select(c => c.Recommendation.Game.ProductId).ToHashSet();

        // A game appears in one section only: the first one that takes it.
        var used = new HashSet<string>();

        // Leaving soon first: the only picks with a deadline. Same bar as the
        // sections (close enough to a favorite); among those, the v1 score
        // (genre taste + rating) decides.
        List<LeavingSoonPick> leavingSoon = favorites.Count == 0
            ? []
            : candidateFeatures
                .Where(c => c.Recommendation.Game.LeavingSoon)
                .Select(c =>
                {
                    var closest = favorites
                        .Select(f => (Favorite: f, Match: Compare(c.Features, f.Features)))
                        .MaxBy(x => (x.Match.IsSimilar, x.Match.ByTags, x.Match.Similarity));
                    return (c.Recommendation, c.Features, c.Refused, closest.Favorite, closest.Match);
                })
                .Where(x => x.Match.IsSimilar && !IsCloserToRefused(x.Refused, x.Match))
                .OrderByDescending(x => x.Recommendation.Score)
                .Take(leavingSoonCount)
                .Select(x => new LeavingSoonPick(
                    Describe(x.Recommendation, x.Features, x.Favorite.Features, x.Match),
                    x.Favorite.Game))
                .ToList();
        used.UnionWith(leavingSoon.Select(p => p.Game.Recommendation.Game.ProductId));

        var sections = new List<RecommendationSection>();
        var chosen = new List<Features>();

        foreach (var favorite in favorites)
        {
            if (sections.Count == maxSections)
                break;
            if (chosen.Any(c => IsNearDuplicate(c, favorite.Features)))
                continue;

            var games = candidateFeatures
                .Where(c => !used.Contains(c.Recommendation.Game.ProductId))
                .Select(c => (c.Recommendation, c.Features, Match: Compare(c.Features, favorite.Features), c.Refused))
                .Where(c => c.Match.IsSimilar && !IsCloserToRefused(c.Refused, c.Match))
                // Tag matches first (reliable), then the genre fallback.
                // Rounded: 0.999 and 1.0 count as equally similar, then the
                // genre taste and the rating (v1 score) decide.
                .OrderByDescending(c => c.Match.ByTags)
                .ThenByDescending(c => Math.Round(c.Match.Similarity, 2))
                .ThenByDescending(c => c.Recommendation.Score)
                .Take(gamesPerSection)
                .Select(c => Describe(c.Recommendation, c.Features, favorite.Features, c.Match))
                .ToList();

            if (games.Count == 0)
                continue;

            sections.Add(new RecommendationSection(favorite.Game, games));
            chosen.Add(favorite.Features);
            used.UnionWith(games.Select(g => g.Recommendation.Game.ProductId));
        }

        // Candidates are already ordered by the v1 score.
        var byGenres = candidates
            .Where(c => !used.Contains(c.Game.ProductId) && shownIds.Contains(c.Game.ProductId))
            .Take(byGenresCount)
            .ToList();

        return new SimilarityResult(leavingSoon, sections, byGenres);
    }

    /// <summary>
    /// What the similarity compares for one game. The norms are computed once:
    /// a game is compared with every favorite, at every click.
    /// </summary>
    private sealed class Features
    {
        public Features(IReadOnlyList<string> genres, PlayModes modes, Dictionary<int, double>? tagVector)
        {
            Genres = genres;
            Modes = modes;
            GenreVector = ToVector(genres, modes);
            GenreNorm = Norm(GenreVector);
            TagVector = tagVector;
            TagNorm = tagVector is null ? 0 : Norm(tagVector);
        }

        public IReadOnlyList<string> Genres { get; }
        public PlayModes Modes { get; }
        public Dictionary<string, double> GenreVector { get; }
        public double GenreNorm { get; }
        public Dictionary<int, double>? TagVector { get; }
        public double TagNorm { get; }
    }

    private readonly record struct Match(double Similarity, bool ByTags, bool IsSimilar);

    /// <summary>On tags when both games have some, else on genres and modes.</summary>
    private static Match Compare(Features a, Features b)
    {
        if (a.TagVector is { } tagsA && b.TagVector is { } tagsB)
        {
            double similarity = Cosine(tagsA, a.TagNorm, tagsB, b.TagNorm);
            return new Match(similarity, ByTags: true, IsSimilar: similarity >= MinTagSimilarity);
        }

        double genreSimilarity = Cosine(a.GenreVector, a.GenreNorm, b.GenreVector, b.GenreNorm);
        return new Match(genreSimilarity, ByTags: false, IsSimilar: genreSimilarity >= MinSimilarity);
    }

    /// <summary>Tag similarity to the closest refused game; 0 without tags or refusals.</summary>
    private static double RefusedSimilarity(Features game, IReadOnlyList<Features> refused) =>
        game.TagVector is { } tags && refused.Count > 0
            ? refused.Max(r => Cosine(tags, game.TagNorm, r.TagVector!, r.TagNorm))
            : 0;

    /// <summary>
    /// Shown for a favorite it resembles, but more like a refused game: the
    /// player said no to that kind of game. Only when both are measured on
    /// tags (a genre match and a tag match are not on the same scale).
    /// </summary>
    private static bool IsCloserToRefused(double refusedSimilarity, Match favoriteMatch) =>
        favoriteMatch.ByTags && refusedSimilarity > favoriteMatch.Similarity;

    private static bool IsNearDuplicate(Features chosen, Features favorite)
    {
        var match = Compare(chosen, favorite);
        return match.Similarity >= (match.ByTags ? MaxTagSimilarityBetweenFavorites : MaxSimilarityBetweenFavorites);
    }

    private static SimilarGame Describe(Recommendation recommendation, Features game, Features favorite, Match match)
    {
        IReadOnlyList<int> sharedTags = match.ByTags
            // The tags that weigh most in both: what makes them alike.
            ? game.TagVector!
                .Where(t => favorite.TagVector!.ContainsKey(t.Key))
                .OrderByDescending(t => t.Value * favorite.TagVector![t.Key])
                .Take(SharedTagCount)
                .Select(t => t.Key)
                .ToList()
            : [];

        return new SimilarGame(
            recommendation,
            match.Similarity,
            game.Genres.Intersect(favorite.Genres).ToList(),
            game.Modes & favorite.Modes,
            match.ByTags,
            sharedTags);
    }

    /// <summary>Genres (main 1, secondary 0.5) and play modes (0.5 each) as a sparse vector.</summary>
    public static Dictionary<string, double> ToVector(IReadOnlyList<string> genres, PlayModes modes)
    {
        var vector = new Dictionary<string, double>();
        for (int i = 0; i < genres.Count; i++)
            vector.TryAdd("genre:" + genres[i], TasteProfile.GenreWeight(i));

        foreach (var mode in Enum.GetValues<PlayModes>())
        {
            // HasFlag tests one bit of the combined value.
            if (mode != PlayModes.None && modes.HasFlag(mode))
                vector["mode:" + mode] = ModeWeight;
        }
        return vector;
    }

    /// <summary>
    /// Cosine similarity: 1 for vectors pointing the same way (same features
    /// in the same proportions), 0 for nothing in common. Unlike a raw count
    /// of shared features, it is not inflated by games with many genres.
    /// Generic: genre vectors have text keys, tag vectors tag ids.
    /// </summary>
    public static double Cosine<TKey>(Dictionary<TKey, double> a, Dictionary<TKey, double> b)
        where TKey : notnull =>
        Cosine(a, Norm(a), b, Norm(b));

    /// <summary>Same, with the norms already known (<see cref="Features"/>).</summary>
    private static double Cosine<TKey>(Dictionary<TKey, double> a, double normA, Dictionary<TKey, double> b, double normB)
        where TKey : notnull
    {
        if (normA == 0 || normB == 0)
            return 0;
        // The smaller vector walked, the other looked up. A foreach on a
        // Dictionary (not an interface) uses its struct enumerator: no allocation.
        if (a.Count > b.Count)
            (a, b) = (b, a);
        double dot = 0;
        foreach (var (key, value) in a)
        {
            if (b.TryGetValue(key, out double other))
                dot += value * other;
        }
        return dot / (normA * normB);
    }

    private static double Norm<TKey>(Dictionary<TKey, double> vector) where TKey : notnull
    {
        double sum = 0;
        foreach (double value in vector.Values)
            sum += value * value;
        return Math.Sqrt(sum);
    }
}
