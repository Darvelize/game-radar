using GameRadar.Core.Epic;
using GameRadar.Core.Logging;
using GameRadar.Core.Resources;

namespace GameRadar.Core.Tests.Epic;

public class EpicClientTests
{
    // Shaped like Epic's authorization codes (32 hex), made up.
    private const string Code = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void RequireCode_ReadsTheWholePageOrTheCodeAlone()
    {
        string page = $$"""{"warning":"Do not share this code with any 3rd party service.","redirectUrl":"https://localhost/launcher/authorized?code={{Code}}","authorizationCode":"{{Code}}","exchangeCode":null,"sid":null}""";

        Assert.Equal(Code, EpicClient.RequireCode(page));
        Assert.Equal(Code, EpicClient.RequireCode($"  {Code}\n"));
    }

    [Fact]
    public void RequireCode_APageWithoutCode_SaysToSignInFirst()
    {
        // A browser signed out of epicgames.com: no code on the page.
        string page = """{"redirectUrl":"https://www.epicgames.com/id/login","authorizationCode":null,"sid":null}""";

        var ex = Assert.Throws<EpicException>(() => EpicClient.RequireCode(page));

        Assert.Equal(CoreStrings.Epic_SignInFirst, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("my password")]
    [InlineData("{ not json")]
    public void RequireCode_SomethingElse_SaysWhatToPaste(string pasted)
    {
        var ex = Assert.Throws<EpicException>(() => EpicClient.RequireCode(pasted));

        Assert.Equal(CoreStrings.Epic_PasteCode, ex.Message);
    }

    [Fact]
    public void ParseTokens_ReadsTheAccessAndRefreshTokens()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        string json = """{"access_token":"eg1~access","expires_in":129600,"refresh_token":"eg1~refresh","refresh_expires":31536000,"account_id":"abc123","token_type":"bearer"}""";

        var tokens = EpicClient.ParseTokens(json, now);

        Assert.Equal("eg1~access", tokens.AccessToken);
        Assert.Equal(now.AddHours(36), tokens.ExpiresAt);
        Assert.Equal("eg1~refresh", tokens.RefreshToken);
        Assert.Equal("abc123", tokens.AccountId);
    }

    [Theory]
    [InlineData("""{"access_token":"x","expires_in":60}""")]
    [InlineData("[]")]
    [InlineData("<html>")]
    public void ParseTokens_AnIncompleteAnswer_IsAFormatError(string json)
    {
        var ex = Assert.Throws<EpicException>(() => EpicClient.ParseTokens(json, DateTimeOffset.UtcNow));

        Assert.Equal(CoreStrings.Epic_Format, ex.Message);
    }

    [Fact]
    public void ParseLibrary_ReadsTheEntriesAndTheNextCursor()
    {
        string json = """
            { "responseMetadata": { "nextCursor": "page-2", "stateToken": "s" },
              "records": [
                { "namespace": "ns-borderlands", "catalogItemId": "item-bl3", "appName": "Catnip", "sandboxName": "Borderlands 3", "recordType": "APPLICATION" },
                { "namespace": "ns-x", "catalogItemId": null, "appName": "NoItem" } ] }
            """;

        var page = EpicClient.ParseLibrary(json);

        Assert.Equal("page-2", page.NextCursor);
        var entry = Assert.Single(page.Entries);
        Assert.Equal(("Catnip", "ns-borderlands", "item-bl3"), (entry.AppName, entry.Namespace, entry.CatalogItemId));
    }

    [Fact]
    public void ParseLibrary_TheLastPageHasNoCursor()
    {
        Assert.Null(EpicClient.ParseLibrary("""{ "responseMetadata": {}, "records": [] }""").NextCursor);
    }

    [Fact]
    public void ParsePlaytime_GivesMinutes()
    {
        string json = """[ { "accountId": "abc123", "artifactId": "Catnip", "totalTime": 228780 }, { "artifactId": "Fresh", "totalTime": 30 }, { "totalTime": 60 } ]""";

        var played = EpicClient.ParsePlaytime(json);

        Assert.Equal([("Catnip", 3813), ("Fresh", 0)], played.Select(p => (p.AppName, p.Minutes)));
    }

    [Theory]
    [InlineData("""[{"path":"games"},{"path":"applications"}]""", true)]
    [InlineData("""[{"path":"addons"}]""", false)]                   // a DLC
    [InlineData("""[{"path":"engines/ue5"},{"path":"engines"}]""", false)] // Unreal Engine
    [InlineData("""[{"path":"games"},{"path":"addons"}]""", false)]
    public void ParseCatalogItem_KeepsTheTitleAndWhetherItIsAGame(string categories, bool isGame)
    {
        string json = $$"""{ "item-1": { "id": "item-1", "title": "Marvel Rivals", "categories": {{categories}} } }""";

        var item = EpicClient.ParseCatalogItem(json, "item-1")!;

        Assert.Equal("Marvel Rivals", item.Title);
        Assert.Equal(isGame, item.IsGame);
    }

    [Fact]
    public void ParseCatalogItem_AnUnknownItemIsNull()
    {
        Assert.Null(EpicClient.ParseCatalogItem("{}", "item-1"));
    }

    [Theory]
    [InlineData("""{"data":{"Achievement":{"productAchievementsRecordBySandbox":{"totalAchievements":40}}}}""", 40)]
    [InlineData("""{"data":{"Achievement":{"productAchievementsRecordBySandbox":{"totalAchievements":null}}}}""", null)]
    [InlineData("""{"data":{"Achievement":{"productAchievementsRecordBySandbox":null}}}""", null)]
    public void ParseAchievementsTotal_NullWhenTheGameHasNone(string json, int? total)
    {
        Assert.Equal(total, EpicClient.ParseAchievementsTotal(json));
    }

    [Theory]
    [InlineData("""{"data":{"PlayerAchievement":{"playerAchievementGameRecordsBySandbox":{"records":[{"totalUnlocked":5}]}}}}""", 5)]
    [InlineData("""{"data":{"PlayerAchievement":{"playerAchievementGameRecordsBySandbox":{"records":null}}}}""", 0)]
    public void ParseAchievementsUnlocked_ZeroWithoutRecord(string json, int unlocked)
    {
        Assert.Equal(unlocked, EpicClient.ParseAchievementsUnlocked(json));
    }

    [Theory]
    [InlineData("""{"errors":[{"message":"Cannot query field"}],"data":null}""")]
    // Partial data with an error: a resolver failing would read as "no achievements".
    [InlineData("""{"errors":[{"message":"Timeout"}],"data":{"Achievement":{"productAchievementsRecordBySandbox":null},"PlayerAchievement":{"playerAchievementGameRecordsBySandbox":null}}}""")]
    public void AGraphQlErrorInA200_IsAFormatError(string json)
    {

        Assert.Equal(CoreStrings.Epic_Format, Assert.Throws<EpicException>(() => EpicClient.ParseAchievementsTotal(json)).Message);
        Assert.Equal(CoreStrings.Epic_Format, Assert.Throws<EpicException>(() => EpicClient.ParseAchievementsUnlocked(json)).Message);
    }

    [Fact]
    public void Redact_MasksTheCodeAndTheTokens()
    {
        string redacted = ErrorLog.Redact(
            $$"""page {"authorizationCode":"{{Code}}"} then Authorization: bearer eg1~eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl and refresh_token=eg1~eyJraWQiOiJ4In0.eyJhIjoxfQ.c2ln""");

        Assert.DoesNotContain(Code, redacted);
        Assert.DoesNotContain("c2lnbmF0dXJl", redacted);
        Assert.DoesNotContain("eyJraWQiOiJ4In0", redacted);
    }
}
