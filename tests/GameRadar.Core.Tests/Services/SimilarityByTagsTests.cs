using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

/// <summary>
/// The user's case: loving Warframe (looter-shooter, action RPG) does not
/// mean loving Call of Duty (military FPS), though both are "Shooter" for
/// the Microsoft Store.
/// </summary>
public class SimilarityByTagsTests
{
    // Steam tag ids.
    private const int Action = 19, Racing = 699, Fps = 1663, Shooter = 1774, ThirdPersonShooter = 3814,
        OnlineCoop = 3843, Military = 4168, ActionRpg = 4231, LooterShooter = 353880;

    private static readonly PlayedGameWithGenres Warframe = new(
        Played("w", 100, PlayStatus.Mastered, name: "Warframe"),
        ["Shooter", "Role playing", "Action & adventure"], PlayModes.CoopOnline,
        Tags(LooterShooter, 1, ActionRpg, 0.8, ThirdPersonShooter, 0.7, Action, 0.6, OnlineCoop, 0.5));

    private static readonly Recommendation Division = Candidate("division", ["Shooter", "Role playing"],
        Tags(LooterShooter, 1, ThirdPersonShooter, 0.9, OnlineCoop, 0.7, Action, 0.6), score: 0.3);

    private static readonly Recommendation CallOfDuty = Candidate("cod", ["Shooter", "Action & adventure"],
        Tags(Fps, 1, Action, 0.9, Shooter, 0.8, Military, 0.8), score: 0.9);

    // Many racing games: "Action" is on most candidates, so it weighs little.
    private static readonly IReadOnlyList<Recommendation> Others = Enumerable.Range(0, 20)
        .Select(i => Candidate($"race{i}", ["Racing & flying"], Tags(Action, 1, Racing, 0.8)))
        .ToList();

    private static IReadOnlyDictionary<int, double> Tags(params double[] pairs) =>
        Enumerable.Range(0, pairs.Length / 2).ToDictionary(i => (int)pairs[2 * i], i => pairs[2 * i + 1]);

    private static Recommendation Candidate(string id, string[] genres, IReadOnlyDictionary<int, double>? tags, double score = 0.5) =>
        new(Catalog(id, genres: genres, modes: PlayModes.CoopOnline) with { Tags = tags }, score, []);

    private static IReadOnlyList<SimilarGame> SectionOf(params Recommendation[] candidates)
    {
        var result = SimilarityRecommender.Build([Warframe], [.. candidates, .. Others], Now);
        return Assert.Single(result.Sections).Games;
    }

    [Fact]
    public void Build_ComparesOnTagsWhenBothGamesHaveSome()
    {
        var games = SectionOf(Division, CallOfDuty);

        // Same "Shooter" genre, but only the looter-shooter is alike.
        var match = Assert.Single(games);
        Assert.Same(Division, match.Recommendation);
        Assert.True(match.ByTags);
        Assert.True(match.Similarity >= SimilarityRecommender.MinTagSimilarity);
    }

    [Fact]
    public void Build_FallsBackOnGenresWhenTagsAreUnknown()
    {
        // Not found on Steam: judged on the Store genres, as before.
        var unknown = Candidate("unknown", ["Shooter", "Role playing", "Action & adventure"], tags: null);

        var match = Assert.Single(SectionOf(unknown));

        Assert.Same(unknown, match.Recommendation);
        Assert.False(match.ByTags);
    }

    [Fact]
    public void Build_PutsTagMatchesBeforeGenreMatches()
    {
        // Better rated, but only judged on the coarse genres.
        var unknown = Candidate("unknown", ["Shooter", "Role playing", "Action & adventure"], tags: null, score: 0.9);

        var games = SectionOf(unknown, Division);

        Assert.Equal(["division", "unknown"], games.Select(g => g.Recommendation.Game.ProductId));
    }

    [Fact]
    public void Build_ExplainsWithTheMostTellingSharedTags()
    {
        var match = Assert.Single(SectionOf(Division));

        // "Looter Shooter" first: rare and heavily voted on both.
        Assert.Equal(LooterShooter, match.SharedTags![0]);
        Assert.DoesNotContain(Action, match.SharedTags.Take(2));
    }

    [Fact]
    public void Build_SkipsAFavoriteNearlyIdenticalToAChosenOne()
    {
        // A sequel with the same tags: its section would repeat the first one.
        var sequel = Warframe with { Game = Played("w2", 100, PlayStatus.Mastered, name: "Warframe 2") };
        var result = SimilarityRecommender.Build([Warframe, sequel], [Division, .. Others], Now);

        Assert.Equal("Warframe", Assert.Single(result.Sections).Favorite.Name);
    }

    [Fact]
    public void TagVectorizer_WeighsRareTagsMoreThanCommonOnes()
    {
        var vectorizer = new TagVectorizer([.. Others.Select(o => o.Game.Tags), Division.Game.Tags, CallOfDuty.Game.Tags]);

        Assert.True(vectorizer.IdfOf(LooterShooter) > vectorizer.IdfOf(Action));
        // On every game or nearly: says nothing, never negative.
        Assert.Equal(0, vectorizer.IdfOf(Action));
        Assert.Null(vectorizer.Vector(null));
    }
}
