using GameRadar.Core.Models;
using GameRadar.Core.Xbox;

namespace GameRadar.Core.Services;

/// <summary>A game suggested to the player (Game Pass or Steam store), with why.</summary>
/// <param name="ReasonGenres">Genres the player went far in that this game belongs to, strongest first (scored on genres).</param>
/// <param name="ReasonTags">Steam tags the player went far in that weigh most on this game (scored on tags).</param>
public sealed record Recommendation(
    CatalogGame Game,
    double Score,
    IReadOnlyList<string> ReasonGenres,
    IReadOnlyList<int>? ReasonTags = null);

/// <summary>
/// Ranks the candidate games (Game Pass, Steam store) the player has not played yet:
/// taste for their Steam tags (or, without tags, their genres) first,
/// community rating as a small bonus.
/// </summary>
public static class RecommendationEngine
{
    // Community rating, smoothed like the genres: every game starts with
    // PriorRatingCount virtual ratings of PriorRating, so 5/5 from 3 people
    // does not beat 4.6/5 from 4,000.
    private const double PriorRating = 3.5;
    private const double PriorRatingCount = 50;

    // Max bonus of a perfectly rated game. Kept small compared to the gaps
    // between genre affinities: it mostly breaks ties within a genre.
    private const double QualityWeight = 0.1;

    // Tags named in a card's explanation.
    private const int ReasonTagCount = 3;

    // Bonus of a game on the player's Steam wishlist: the one signal the
    // player gave on purpose. Above the quality bonus (0.1, which only breaks
    // ties), so a wished game goes ahead of an equivalent one; far under the
    // gaps between affinities, so it never brings up a genre the player drops.
    // Confirmed by the user (Oct 2026).
    internal const double WishlistBonus = 0.15;

    /// <param name="tagVectorizer">
    /// Tag rarity; by default measured on <paramref name="catalog"/>. Pass the
    /// one of the similarity search to weigh the tags the same way.
    /// </param>
    /// <param name="wishlist">Steam app ids on the player's wishlist: a bonus for those games (null = none).</param>
    public static IReadOnlyList<Recommendation> Recommend(
        TasteProfile profile,
        IEnumerable<CatalogGame> catalog,
        IEnumerable<PlayedGame> history,
        CatalogMatcher matcher,
        int maxCount = 50,
        TagVectorizer? tagVectorizer = null,
        IReadOnlySet<string>? wishlist = null)
    {
        var exclusions = new PlayedExclusions(history, matcher);
        var games = catalog.ToList();
        var vectorizer = tagVectorizer ?? new TagVectorizer(games.Select(g => g.Tags));

        double Wished(CatalogGame game) =>
            wishlist is not null && game.SteamAppId is { } appId && wishlist.Contains(appId) ? WishlistBonus : 0;

        var scored = games
            .Where(g => (g.Genres.Count > 0 || g.Tags is { Count: > 0 }) && !exclusions.Contains(g))
            // Several Store products for one game (editions, cross-gen packs):
            // keep a single one, the most rated.
            .GroupBy(g => GameTitle.Key(g.Title))
            .Select(group => group.MaxBy(g => g.RatingCount)!)
            .Select(g => (
                Game: g,
                // On Steam tags when both sides have some (the user's choice:
                // far finer than the Store genres), else on genres.
                OnTags: profile.HasTags && vectorizer.Vector(g.Tags) is { } tags ? ScoreOnTags(profile, tags) : default((double Taste, IReadOnlyList<int> Reasons)?),
                OnGenres: ScoreOnGenres(profile, g.Genres)))
            .ToList();

        // Both scores are means of affinities, yet not on the same scale: on
        // real data, the genre score's median was 0.99 and the tag score's
        // 0.54, so the few games without tags (old Xbox 360 games) took the
        // whole top. A game scored on genres gets the tag score found at the
        // same rank among the games that have both (quantile mapping). Both
        // truly: a game with tags but no genre has no genre score (not a 0).
        var toTagScale = new QuantileMap(scored
            .Where(s => s.OnTags is not null && s.Game.Genres.Count > 0)
            .Select(s => (s.OnGenres.Taste, s.OnTags!.Value.Taste)));

        return scored
            .Select(s => s.OnTags is { } onTags
                ? new Recommendation(s.Game, onTags.Taste + Quality(s.Game) + Wished(s.Game), [], onTags.Reasons)
                : new Recommendation(s.Game, toTagScale.Map(s.OnGenres.Taste) + Quality(s.Game) + Wished(s.Game), s.OnGenres.Reasons))
            .OrderByDescending(r => r.Score)
            .Take(maxCount)
            .ToList();
    }

    private static double Quality(CatalogGame game)
    {
        double smoothedRating = ((game.AverageRating ?? PriorRating) * game.RatingCount + PriorRating * PriorRatingCount)
            / (game.RatingCount + PriorRatingCount);
        // Maps the rating to [-1, 1] around the prior (2 -> -1, 5 -> 1).
        return QualityWeight * (smoothedRating - PriorRating) / (5 - PriorRating);
    }

    /// <summary>
    /// Turns a score of one scale into the score of the same rank on another,
    /// learned on pairs measured both ways. Without pairs (no tags at all),
    /// scores pass through unchanged.
    /// </summary>
    private sealed class QuantileMap
    {
        private readonly double[] _from;
        private readonly double[] _to;

        public QuantileMap(IEnumerable<(double From, double To)> pairs)
        {
            var list = pairs.ToList();
            _from = list.Select(p => p.From).Order().ToArray();
            _to = list.Select(p => p.To).Order().ToArray();
        }

        public double Map(double value)
        {
            if (_from.Length == 0)
                return value;
            // Rank of the value among the "from" scores, ties counting half
            // (a whole tie group lands in its middle), then the "to" score there.
            double below = _from.Count(f => f < value) + _from.Count(f => f == value) / 2.0;
            double position = below / _from.Length * (_to.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = Math.Min(lower + 1, _to.Length - 1);
            return _to[lower] + (position - lower) * (_to[upper] - _to[lower]);
        }
    }

    /// <summary>
    /// The affinities of the game's tags, averaged with their TF-IDF weights:
    /// "Souls-like" or "Story Rich" say more about a game than "Action",
    /// which is on most of them. A tag the player never met counts 0 (no
    /// opinion), as a genre never played does.
    /// </summary>
    private static (double Taste, IReadOnlyList<int> Reasons) ScoreOnTags(TasteProfile profile, Dictionary<int, double> tags)
    {
        double weighted = tags.Sum(t => t.Value * profile.AffinityOfTag(t.Key));
        double taste = weighted / tags.Values.Sum();

        // The tags that weigh most for this game among those the player went far in.
        var reasons = tags
            .Where(t => profile.StrongGamesWithTag(t.Key) > 0 && profile.AffinityOfTag(t.Key) > 0)
            .OrderByDescending(t => t.Value * profile.AffinityOfTag(t.Key))
            .Take(ReasonTagCount)
            .Select(t => t.Key)
            .ToList();
        return (taste, reasons);
    }

    private static (double Taste, IReadOnlyList<string> Reasons) ScoreOnGenres(TasteProfile profile, IReadOnlyList<string> genres)
    {
        double weighted = 0, weightTotal = 0;
        for (int i = 0; i < genres.Count; i++)
        {
            double weight = TasteProfile.GenreWeight(i);
            weighted += weight * profile.AffinityOf(genres[i]);
            weightTotal += weight;
        }

        var reasons = genres
            .Where(g => profile.StrongGamesIn(g) > 0 && profile.AffinityOf(g) > 0)
            .OrderByDescending(profile.AffinityOf)
            .ToList();
        return (weightTotal == 0 ? 0 : weighted / weightTotal, reasons);
    }

    /// <summary>
    /// Everything that identifies a game the player already has, so that
    /// another edition of it is not recommended either.
    /// </summary>
    private sealed class PlayedExclusions
    {
        private readonly HashSet<string> _productIds = new();
        private readonly HashSet<string> _titleIds = new();
        private readonly HashSet<string> _titleKeys = new();
        private readonly HashSet<string> _steamAppIds = new();

        public PlayedExclusions(IEnumerable<PlayedGame> history, CatalogMatcher matcher)
        {
            foreach (var played in history)
            {
                // Owned on Steam but never launched: still worth suggesting
                // (the user's choice), it may be a forgotten game.
                if (played.NeverPlayed)
                    continue;

                // Only Xbox ids are title ids: a Steam appid could collide with one.
                if (played.Platform == XboxLiveClient.PlatformName)
                    _titleIds.Add(played.PlatformId);
                // Steam app ids (games added by hand too): catches a Steam
                // candidate whose title differs a bit.
                if (played.SteamAppId is { } appId)
                    _steamAppIds.Add(appId);
                _titleKeys.Add(GameTitle.Key(played.Name));
                if (matcher.Find(played) is { } match)
                {
                    _productIds.Add(match.ProductId);
                    _titleKeys.Add(GameTitle.Key(match.Title));
                }
            }
        }

        public bool Contains(CatalogGame game) =>
            _productIds.Contains(game.ProductId)
            || (game.XboxTitleId is { } id && _titleIds.Contains(id))
            || (game.SteamAppId is { } appId && _steamAppIds.Contains(appId))
            || _titleKeys.Contains(GameTitle.Key(game.Title));
    }
}
