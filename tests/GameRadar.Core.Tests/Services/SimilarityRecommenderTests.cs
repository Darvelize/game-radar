using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

public class SimilarityRecommenderTests
{
    private const PlayModes CoopShooterModes = PlayModes.CoopOnline | PlayModes.MultiOnline;

    // A looter-shooter favorite and a solo RPG favorite (both mastered).
    private static readonly PlayedGameWithGenres Destiny =
        new(Played("d", 100, PlayStatus.Mastered, name: "Destiny 2"), ["Action & adventure", "Shooter"], CoopShooterModes);
    private static readonly PlayedGameWithGenres FinalFantasy =
        new(Played("f", 100, PlayStatus.Mastered, name: "FINAL FANTASY"), ["Role playing"], PlayModes.Solo);

    private static Recommendation Candidate(string id, string[] genres, PlayModes modes, double score = 0.5, bool leavingSoon = false) =>
        new(Catalog(id, genres: genres, modes: modes) with { LeavingSoon = leavingSoon }, score, []);

    [Fact]
    public void Build_PutsMatchingLeavingSoonGamesFirstWithTheirClosestFavorite()
    {
        var leavingRpg = Candidate("leaving", ["Role playing"], PlayModes.Solo, leavingSoon: true);
        var stayingRpg = Candidate("staying", ["Role playing"], PlayModes.Solo);

        var result = SimilarityRecommender.Build([Destiny, FinalFantasy], [leavingRpg, stayingRpg], Now);

        var pick = Assert.Single(result.LeavingSoon);
        Assert.Same(leavingRpg, pick.Game.Recommendation);
        Assert.Equal("FINAL FANTASY", pick.ClosestFavorite.Name);
        // Not repeated in the favorite's section.
        var section = Assert.Single(result.Sections);
        Assert.Same(stayingRpg, Assert.Single(section.Games).Recommendation);
    }

    [Fact]
    public void Build_LeavesOutLeavingSoonGamesThatDoNotMatch()
    {
        var leavingStrategy = Candidate("leaving", ["Strategy"], PlayModes.Solo, leavingSoon: true);

        var result = SimilarityRecommender.Build([FinalFantasy], [leavingStrategy], Now);

        Assert.Empty(result.LeavingSoon);
    }

    [Fact]
    public void Build_OrdersLeavingSoonGamesByV1Score()
    {
        var lower = Candidate("low", ["Role playing"], PlayModes.Solo, score: 0.1, leavingSoon: true);
        var higher = Candidate("high", ["Role playing"], PlayModes.Solo, score: 0.7, leavingSoon: true);

        var result = SimilarityRecommender.Build([FinalFantasy], [lower, higher], Now);

        Assert.Equal(["high", "low"], result.LeavingSoon.Select(p => p.Game.Recommendation.Game.ProductId));
    }

    [Fact]
    public void Cosine_IsOneForSameFeaturesAndZeroForNothingInCommon()
    {
        var shooter = SimilarityRecommender.ToVector(["Shooter"], PlayModes.Solo);
        var rpg = SimilarityRecommender.ToVector(["Role playing"], PlayModes.None);

        Assert.Equal(1, SimilarityRecommender.Cosine(shooter, shooter), precision: 6);
        Assert.Equal(0, SimilarityRecommender.Cosine(shooter, rpg));
    }

    [Fact]
    public void ToVector_WeightsMainGenreSecondaryGenresAndModes()
    {
        var vector = SimilarityRecommender.ToVector(["Shooter", "Role playing"], PlayModes.Solo | PlayModes.CoopOnline);

        Assert.Equal(1, vector["genre:Shooter"]);
        Assert.Equal(0.5, vector["genre:Role playing"]);
        Assert.Equal(SimilarityRecommender.ModeWeight, vector["mode:Solo"]);
        Assert.Equal(SimilarityRecommender.ModeWeight, vector["mode:CoopOnline"]);
        Assert.Equal(4, vector.Count);
    }

    [Fact]
    public void Build_GivesEachFavoriteFamilyItsOwnSection()
    {
        var coopShooter = Candidate("coop", ["Action & adventure", "Shooter"], CoopShooterModes);
        var soloRpg = Candidate("rpg", ["Role playing"], PlayModes.Solo);

        var result = SimilarityRecommender.Build([Destiny, FinalFantasy], [coopShooter, soloRpg], Now);

        Assert.Equal(2, result.Sections.Count);
        var destinySection = result.Sections.Single(s => s.Favorite.Name == "Destiny 2");
        var shared = Assert.Single(destinySection.Games);
        Assert.Same(coopShooter, shared.Recommendation);
        Assert.Equal(["Action & adventure", "Shooter"], shared.SharedGenres);
        Assert.Equal(CoopShooterModes, shared.SharedModes);
    }

    [Fact]
    public void Build_LeavesOutGamesBelowMinimumSimilarity()
    {
        // A solo strategy game shares nothing with Destiny's features.
        var soloTactical = Candidate("tac", ["Strategy"], PlayModes.Solo);

        var result = SimilarityRecommender.Build([Destiny], [soloTactical], Now);

        Assert.Empty(result.Sections);
        Assert.Same(soloTactical, Assert.Single(result.ByGenres));
    }

    [Fact]
    public void Build_SkipsFavoriteTooCloseToAnAlreadyChosenOne()
    {
        var destinyLike = Destiny with { Game = Played("w", 100, PlayStatus.Mastered, name: "Warframe") };
        var candidates = Enumerable.Range(0, 10)
            .Select(i => Candidate($"c{i}", ["Action & adventure", "Shooter"], CoopShooterModes))
            .ToList();

        var result = SimilarityRecommender.Build([Destiny, destinyLike], candidates, Now);

        // Same features: a second section would only repeat the first.
        Assert.Single(result.Sections);
    }

    [Fact]
    public void Build_ShowsEachGameInOneSectionOnly()
    {
        // Main genre swapped: 0.86 similar to Destiny, under the 0.9 limit,
        // so it would get its own section, and the candidate (0.86 similar
        // to it too) would qualify for both.
        var shooterFirst = Destiny with
        {
            Game = Played("o", 100, PlayStatus.Mastered, name: "DOOM"),
            Genres = ["Shooter", "Action & adventure"]
        };
        var candidates = new[] { Candidate("c1", ["Action & adventure", "Shooter"], CoopShooterModes) };
        Assert.True(SimilarityRecommender.Cosine(
            SimilarityRecommender.ToVector(Destiny.Genres, Destiny.Modes),
            SimilarityRecommender.ToVector(shooterFirst.Genres, shooterFirst.Modes)) < SimilarityRecommender.MaxSimilarityBetweenFavorites);

        var result = SimilarityRecommender.Build([Destiny, shooterFirst], candidates, Now);

        // Taken by the first section; the second, left empty, is dropped.
        var section = Assert.Single(result.Sections);
        Assert.Equal("Destiny 2", section.Favorite.Name);
        Assert.Single(section.Games);
    }

    [Fact]
    public void Build_IgnoresWeakFavorites()
    {
        var littlePlayed = new PlayedGameWithGenres(Played("l", 10), ["Role playing"], PlayModes.Solo);
        var candidates = new[] { Candidate("rpg", ["Role playing"], PlayModes.Solo) };

        var weak = SimilarityRecommender.Build([littlePlayed], candidates, Now);

        Assert.Empty(weak.Sections);
    }

    [Fact]
    public void Build_BreaksSimilarityTiesWithV1Score()
    {
        var weaker = Candidate("low", ["Role playing"], PlayModes.Solo, score: 0.2);
        var stronger = Candidate("high", ["Role playing"], PlayModes.Solo, score: 0.6);

        var section = Assert.Single(SimilarityRecommender.Build([FinalFantasy], [weaker, stronger], Now).Sections);

        Assert.Equal(["high", "low"], section.Games.Select(g => g.Recommendation.Game.ProductId));
    }

    [Fact]
    public void Build_ByGenresKeepsV1OrderWithoutGamesAlreadyInSections()
    {
        var inSection = Candidate("rpg", ["Role playing"], PlayModes.Solo, score: 0.9);
        var first = Candidate("a", ["Racing & flying"], PlayModes.None, score: 0.8);
        var second = Candidate("b", ["Sports"], PlayModes.None, score: 0.1);

        var result = SimilarityRecommender.Build([FinalFantasy], [inSection, first, second], Now);

        Assert.Equal(["a", "b"], result.ByGenres.Select(r => r.Game.ProductId));
    }
}
