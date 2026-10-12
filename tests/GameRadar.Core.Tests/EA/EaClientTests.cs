using GameRadar.Core.EA;

namespace GameRadar.Core.Tests.EA;

public class EaClientTests
{
    // Shaped like EA's tokens (base64 of "AT1:…"), made up.
    private const string Token = "QVQxOjIuMDozLjA6NjA6bWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFk";

    [Fact]
    public void RequireToken_ReadsTheWholePageOrTheTokenAlone()
    {
        string page = $$"""{"access_token":"{{Token}}","token_type":"Bearer","expires_in":14399}""";

        Assert.Equal(Token, EaClient.RequireToken(page));
        Assert.Equal(Token, EaClient.RequireToken($"  {Token}\n"));
    }

    [Fact]
    public void RequireToken_AcceptsTheBase64Padding()
    {
        string padded = Token[..^4] + "WQ==";

        Assert.Equal(padded, EaClient.RequireToken(padded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("my password")]
    [InlineData("""{"token_type":"Bearer"}""")]
    public void RequireToken_SomethingElse_SaysWhatToPaste(string pasted)
    {
        var ex = Assert.Throws<EaException>(() => EaClient.RequireToken(pasted));

        Assert.Equal(GameRadar.Core.Resources.CoreStrings.Ea_PasteToken, ex.Message);
    }

    [Fact]
    public void RequireToken_ThePageOfASignedOutBrowser_SaysToSignIn()
    {
        string page = """{"error_code":"login_required","error":"login_required","error_number":"102100"}""";

        var ex = Assert.Throws<EaException>(() => EaClient.RequireToken(page));

        Assert.Equal(GameRadar.Core.Resources.CoreStrings.Ea_SignInFirst, ex.Message);
    }

    [Fact]
    public void ParseIdentity_ReadsThePlayerId()
    {
        Assert.Equal("1000123", EaClient.ParseIdentity("""{"data":{"me":{"player":{"psd":"1000123"}}}}"""));
    }

    [Fact]
    public void ParseGameSearch_ReadsTheSlugsOnce()
    {
        string json = """{"data":{"gameSearch":{"items":[{"slug":"anthem"},{"slug":"titanfall-2"},{"slug":"anthem"},{"slug":null}]}}}""";

        Assert.Equal(["anthem", "titanfall-2"], EaClient.ParseGameSearch(json));
    }

    [Fact]
    public void ParseRecentGames_ReadsPlayTimeAndLastSession()
    {
        string json = """
            {"data":{"me":{"recentGames":{"items":[
              {"gameSlug":"star-wars-jedi-fallen-order","lastSessionEndDate":"2023-05-07T15:26:58.000Z","totalPlayTimeSeconds":231120},
              {"gameSlug":"battlefield-4","lastSessionEndDate":"1970-01-01T00:00:00.000Z","totalPlayTimeSeconds":125280}]}}}}
            """;

        var games = EaClient.ParseRecentGames(json);

        var jedi = games.Single(g => g.Slug == "star-wars-jedi-fallen-order");
        Assert.Equal(3852, jedi.PlaytimeMinutes);
        Assert.Equal(new DateTimeOffset(2023, 5, 7, 15, 26, 58, TimeSpan.Zero), jedi.LastPlayed);
        // 1970: EA does not know the date.
        Assert.Null(games.Single(g => g.Slug == "battlefield-4").LastPlayed);
    }

    [Fact]
    public void ParseRecentGames_NoGamePlayedAmongTheseSlugs_IsEmpty()
    {
        Assert.Empty(EaClient.ParseRecentGames("""{"data":{"me":{"recentGames":{"items":[]}}}}"""));
    }

    [Fact]
    public void ParseGames_TakesTheBaseGameOfEachSlug()
    {
        string json = """
            {"data":{"games":{"items":[{"slug":"star-wars-jedi-fallen-order","products":{"items":[
              {"name":"Soundtrack","originOfferId":"Origin.OFR.50.9","baseItem":{"gameType":"EXTRA_CONTENT"}},
              {"name":"STAR WARS Jedi: Fallen Order™ Deluxe Edition","originOfferId":"Origin.OFR.50.0003532","baseItem":{"gameType":"BASE_GAME"}},
              {"name":"STAR WARS Jedi: Fallen Order™","originOfferId":"Origin.OFR.50.0004031","baseItem":{"gameType":"BASE_GAME"}}]}}]}}}
            """;

        var product = Assert.Single(EaClient.ParseGames(json));

        Assert.Equal("star-wars-jedi-fallen-order", product.Slug);
        Assert.Equal("STAR WARS Jedi: Fallen Order™ Deluxe Edition", product.Name);
        Assert.Equal("Origin.OFR.50.0003532", product.OfferId);
    }

    [Fact]
    public void ParseAchievements_CountsTheAwardedOnes()
    {
        string json = """
            {"data":{"achievements":[{"id":"75158_196485_50844","achievements":[
              {"id":"1","awardCount":1,"date":"2023-01-02T00:00:00Z"},{"id":"2","awardCount":0,"date":null},{"id":"3","awardCount":2,"date":"2023-01-03T00:00:00Z"}]}]}}
            """;

        Assert.Equal(new EaAchievements(2, 3), EaClient.ParseAchievements(json));
    }

    [Fact]
    public void ParseAchievements_AGameWithoutAchievements_IsNull()
    {
        Assert.Null(EaClient.ParseAchievements("""{"data":{"achievements":[]}}"""));
    }

    [Theory]
    [InlineData("""{"errors":[{"message":"Cannot query field"}],"data":null}""")]
    [InlineData("<html>Maintenance</html>")]
    [InlineData("""{"data":null}""")]
    public void Parse_AnUnexpectedAnswer_ThrowsEaException(string json)
    {
        Assert.Throws<EaException>(() => EaClient.ParseIdentity(json));
        Assert.Throws<EaException>(() => EaClient.ParseRecentGames(json));
    }
}
