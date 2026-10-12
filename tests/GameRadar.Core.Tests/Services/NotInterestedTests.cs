using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

/// <summary>
/// "Pas intéressé" on a card (choice validated by the user): the game is never
/// shown again, and the games that look like it more than like the favorite
/// they would be shown for are hidden too, on Steam tags only.
/// </summary>
public class NotInterestedTests
{
    // Steam tag ids.
    private const int Action = 19, Racing = 699, Fps = 1663, Shooter = 1774, ThirdPersonShooter = 3814,
        OnlineCoop = 3843, Military = 4168, ActionRpg = 4231, LooterShooter = 353880;

    private static readonly PlayedGameWithGenres Warframe = new(
        Played("w", 100, PlayStatus.Mastered, name: "Warframe"),
        ["Shooter", "Role playing"], PlayModes.CoopOnline,
        Tags(LooterShooter, 1, ActionRpg, 0.8, ThirdPersonShooter, 0.7, OnlineCoop, 0.5));

    // Refused: a military third-person shooter.
    private static readonly Recommendation MilitaryShooter = Candidate("military", "Ghost Squad",
        Tags(Fps, 1, Military, 0.8, ThirdPersonShooter, 0.6));

    // Close enough to Warframe for its section, but closer to the refused game.
    private static readonly Recommendation TacticalShooter = Candidate("tactical", "Tactical Ops",
        Tags(ThirdPersonShooter, 1, Military, 0.8, LooterShooter, 0.4));

    // A looter-shooter with nothing military: nothing to do with the refusal.
    private static readonly Recommendation Looter = Candidate("looter", "Loot Planet",
        Tags(LooterShooter, 1, ActionRpg, 0.6, OnlineCoop, 0.7));

    // Many racing games: "Action" and "Racing" weigh little, as on the real catalog.
    private static readonly IReadOnlyList<Recommendation> Others = Enumerable.Range(0, 20)
        .Select(i => Candidate($"race{i}", $"Race {i}", Tags(Action, 1, Racing, 0.8), score: 0.1))
        .ToList();

    private static IReadOnlyDictionary<int, double> Tags(params double[] pairs) =>
        Enumerable.Range(0, pairs.Length / 2).ToDictionary(i => (int)pairs[2 * i], i => pairs[2 * i + 1]);

    private static Recommendation Candidate(string id, string title, IReadOnlyDictionary<int, double>? tags, double score = 0.5) =>
        new(Catalog(id, title: title, genres: ["Shooter"], modes: PlayModes.CoopOnline) with { Tags = tags }, score, []);

    private static SimilarityResult Build(IReadOnlyList<Recommendation> candidates, params Recommendation[] refused) =>
        SimilarityRecommender.Build([Warframe], candidates, Now, notInterested: refused.Select(r => r.Game).ToList());

    private static IEnumerable<string> Shown(SimilarityResult result) =>
        result.Sections.SelectMany(s => s.Games).Select(g => g.Recommendation.Game.ProductId)
            .Concat(result.LeavingSoon.Select(p => p.Game.Recommendation.Game.ProductId))
            .Concat(result.ByGenres.Select(r => r.Game.ProductId));

    [Fact]
    public void A_refused_game_is_never_shown()
    {
        var result = Build([Looter, .. Others], Looter);

        Assert.DoesNotContain("looter", Shown(result));
    }

    [Fact]
    public void A_refused_game_is_recognized_by_its_title()
    {
        // Refused as a Game Pass game, back as a Steam game after a change of
        // plan: another product id, the same game.
        var onSteam = Candidate("steam:42", "Loot Planet", Looter.Game.Tags);

        var result = Build([onSteam, .. Others], Looter);

        Assert.DoesNotContain("steam:42", Shown(result));
    }

    [Fact]
    public void A_game_closer_to_the_refused_one_than_to_the_favorite_is_hidden()
    {
        var candidates = new[] { MilitaryShooter, TacticalShooter, Looter }.Concat(Others).ToList();
        // The case: in Warframe's section without the refusal...
        var before = Assert.Single(SimilarityRecommender.Build([Warframe], candidates, Now).Sections);
        Assert.Contains(before.Games, g => g.Recommendation == TacticalShooter);

        var result = Build(candidates, MilitaryShooter);

        // ...but more like the refused military shooter, below the near-duplicate bar.
        var vectorizer = new TagVectorizer(candidates.Select(c => c.Game.Tags));
        double toRefused = SimilarityRecommender.Cosine(vectorizer.Vector(TacticalShooter.Game.Tags)!, vectorizer.Vector(MilitaryShooter.Game.Tags)!);
        double toFavorite = SimilarityRecommender.Cosine(vectorizer.Vector(TacticalShooter.Game.Tags)!, vectorizer.Vector(Warframe.Tags)!);
        Assert.InRange(toRefused, toFavorite, SimilarityRecommender.RefusedNeighborTagSimilarity);

        var section = Assert.Single(result.Sections);
        Assert.DoesNotContain(section.Games, g => g.Recommendation == TacticalShooter);
        // The looter-shooter has nothing military: still there.
        Assert.Contains(section.Games, g => g.Recommendation == Looter);
    }

    [Fact]
    public void A_near_duplicate_of_a_refused_game_is_hidden_everywhere()
    {
        // No favorite it resembles: it would be in "D'après tes genres".
        var callOfDuty = Candidate("cod", "Call of Duty", Tags(Fps, 1, Shooter, 0.8, Military, 0.8));
        var sequel = Candidate("cod2", "Call of Duty 2", Tags(Fps, 1, Shooter, 0.8, Military, 0.7), score: 0.9);
        var candidates = new[] { callOfDuty, sequel }.Concat(Others).ToList();
        Assert.Contains("cod2", Shown(SimilarityRecommender.Build([Warframe], candidates, Now)));

        var result = Build(candidates, callOfDuty);

        Assert.DoesNotContain("cod2", Shown(result));
        Assert.Contains("race0", Shown(result));
    }

    [Fact]
    public void Games_without_tags_only_lose_the_refused_game_itself()
    {
        // The Store genres are too coarse: refusing a shooter must not hide
        // every "Shooter" not found on Steam.
        var callOfDuty = Candidate("cod", "Call of Duty", Tags(Fps, 1, Shooter, 0.8, Military, 0.8));
        var unknown = Candidate("unknown", "Unknown Shooter", tags: null, score: 0.9);

        var result = Build([callOfDuty, unknown, .. Others], callOfDuty);

        Assert.Contains("unknown", Shown(result));
    }

    [Fact]
    public void Without_refusals_nothing_changes()
    {
        var candidates = new[] { MilitaryShooter, TacticalShooter, Looter }.Concat(Others).ToList();

        var withNone = Build(candidates);
        var before = SimilarityRecommender.Build([Warframe], candidates, Now);

        Assert.Equal(Shown(before), Shown(withNone));
    }
}
