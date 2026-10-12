using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Manual;

/// <summary>
/// Games added by hand (choices validated by the user): five levels, weighed
/// like a synced game, "didn't like it" counting as a negative signal.
/// </summary>
public class ManualGamesTests
{
    [Theory]
    [InlineData(ManualLevel.Mastered, 3)]
    [InlineData(ManualLevel.Completed, 3)]
    [InlineData(ManualLevel.PlayedALot, 2)]
    [InlineData(ManualLevel.PlayedALittle, 0.5)]
    [InlineData(ManualLevel.DidNotLike, -0.5)]
    public void Each_level_weighs_like_a_synced_game(ManualLevel level, double engagement)
    {
        var game = ManualGames.ToPlayed(new ManualGame("Hades", ManualPlatform.Switch, level));

        Assert.Equal(engagement, TasteProfile.Engagement(game, Now));
    }

    [Fact]
    public void Didnt_like_it_counts_in_the_taste_by_default()
    {
        // An opinion, not a game launched for a minute (those are left out).
        var game = ManualGames.ToPlayed(new ManualGame("Hades", ManualPlatform.Switch, ManualLevel.DidNotLike));

        Assert.False(TasteProfile.IsBarelyPlayed(game));
        Assert.True(TasteSelection.CountsByDefault(game));
        Assert.True(TasteSelection.Counts(game, new HashSet<string>(), new HashSet<string>()));
    }

    [Fact]
    public void The_player_can_still_leave_it_out_of_the_taste()
    {
        var game = ManualGames.ToPlayed(new ManualGame("Hades", ManualPlatform.Switch, ManualLevel.Mastered));

        Assert.False(TasteSelection.Counts(game, new HashSet<string> { game.Key }, new HashSet<string>()));
    }

    [Fact]
    public void A_game_is_known_by_its_steam_app_id_else_by_its_title()
    {
        var onSteam = new ManualGame("Hades", ManualPlatform.Switch, ManualLevel.Completed, SteamAppId: "1145360");
        var typed = new ManualGame("Hades™", ManualPlatform.Switch, ManualLevel.Completed);

        Assert.Equal("1145360", onSteam.Id);
        Assert.Equal("Manual:1145360", ManualGames.ToPlayed(onSteam).Key);
        // Same game, however typed: added twice, it replaces the first one.
        Assert.Equal(new ManualGame("Hades", ManualPlatform.PlayStation, ManualLevel.PlayedALot).Id, typed.Id);
        Assert.Equal("1145360", ManualGames.ToPlayed(onSteam).SteamAppId);
        Assert.Null(ManualGames.ToPlayed(typed).SteamAppId);
    }

    [Fact]
    public void A_game_added_by_hand_is_never_recommended()
    {
        var played = ManualGames.ToPlayed(new ManualGame("Hollow Knight", ManualPlatform.Switch, ManualLevel.Completed));
        var candidates = new[] { Catalog("HK", title: "Hollow Knight"), Catalog("OTHER", title: "Other Game") };

        var recommended = RecommendationEngine.Recommend(TasteProfile.Build([], Now), candidates, [played], new CatalogMatcher(candidates));

        Assert.Equal(["OTHER"], recommended.Select(r => r.Game.ProductId));
    }
}
