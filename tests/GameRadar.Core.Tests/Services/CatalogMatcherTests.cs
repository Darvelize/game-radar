using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

public class CatalogMatcherTests
{
    [Theory]
    [InlineData("WATCH_DOGS™", "watchdogs")]
    [InlineData("Watch Dogs", "watchdogs")]
    [InlineData("Overwatch®", "overwatch")]
    [InlineData("L'Héritage de Poudlard", "lheritagedepoudlard")]
    [InlineData("Halo : La collection le Major", "halolacollectionlemajor")]
    // A compatibility character: one Unicode letter "Ⅻ" for XII (FF XII on PlayStation).
    [InlineData("FINAL FANTASY Ⅻ THE ZODIAC AGE", "finalfantasyxiithezodiacage")]
    // The trademark sign decomposes to the letters "TM" with that: dropped first.
    [InlineData("Hitman℠ Sniper™", "hitmansniper")]
    public void NormalizeName_KeepsOnlyLowercaseLettersAndDigitsWithoutAccents(string name, string expected)
    {
        Assert.Equal(expected, CatalogMatcher.NormalizeName(name));
    }

    [Fact]
    public void Find_MatchesXboxGameByTitleIdFirst()
    {
        var byId = Catalog("P1", title: "Other name", xboxTitleId: "111");
        var byName = Catalog("P2", title: "Played 111");
        var matcher = new CatalogMatcher([byId, byName]);

        Assert.Same(byId, matcher.Find(Played("111")));
    }

    [Fact]
    public void Find_DoesNotUseTitleIdOfAnotherPlatform()
    {
        // A Steam app id may collide with an Xbox title id: not the same game.
        var matcher = new CatalogMatcher([Catalog("P1", xboxTitleId: "111")]);

        Assert.Null(matcher.Find(Played("111", platform: "Steam")));
    }

    [Fact]
    public void Find_MatchesByPackageFamilyNameIgnoringCase()
    {
        var game = Catalog("P1", packageFamilyName: "Publisher.Game_abc");
        var matcher = new CatalogMatcher([game]);

        Assert.Same(game, matcher.Find(Played("999", packageFamilyName: "publisher.game_ABC")));
    }

    [Fact]
    public void Find_FallsBackToNormalizedName()
    {
        var game = Catalog("P1", title: "STAR WARS Jedi: Fallen Order™");
        var matcher = new CatalogMatcher([game]);

        Assert.Same(game, matcher.Find(Played("999", name: "Star Wars Jedi: Fallen Order")));
    }

    [Fact]
    public void Find_ThenFallsBackToTheGamesOwnTitle()
    {
        // The Store sells the edition, the history names the game (12 more
        // matches on a real history, all right: Avowed, Diablo IV, Gears 5...).
        var edition = Catalog("P1", title: "Avowed Standard Edition");
        var matcher = new CatalogMatcher([edition]);

        Assert.Same(edition, matcher.Find(Played("999", name: "Avowed")));
    }

    [Fact]
    public void Find_PrefersTheExactNameToAnotherEdition()
    {
        var edition = Catalog("P1", title: "Diablo® IV - Standard Edition");
        var game = Catalog("P2", title: "Diablo® IV");
        var matcher = new CatalogMatcher([edition, game]);

        Assert.Same(game, matcher.Find(Played("999", name: "Diablo IV")));
    }

    [Fact]
    public void Find_ReturnsNullWhenNothingMatches()
    {
        var matcher = new CatalogMatcher([Catalog("P1", title: "Something else", xboxTitleId: "1")]);

        Assert.Null(matcher.Find(Played("999", name: "Unknown game")));
    }
}
