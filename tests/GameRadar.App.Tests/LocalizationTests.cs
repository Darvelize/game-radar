using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

public sealed class LocalizationTests : AppTest
{
    // ---------- The translations ----------

    // Each pair: the French (neutral) file and its English translation.
    public static TheoryData<string> ResourceFiles => new()
    {
        @"src\GameRadar.App\Resources\Strings",
        @"src\GameRadar.Core\Resources\CoreStrings",
    };

    [Theory]
    [MemberData(nameof(ResourceFiles))]
    public void Every_text_has_its_English_translation(string file)
    {
        var french = ReadResx(file + ".resx");
        var english = ReadResx(file + ".en.resx");

        Assert.Empty(french.Keys.Except(english.Keys));   // missing in English
        Assert.Empty(english.Keys.Except(french.Keys));   // left over in English
        Assert.All(french.Concat(english), text => Assert.False(string.IsNullOrWhiteSpace(text.Value), text.Key));
    }

    [Theory]
    [MemberData(nameof(ResourceFiles))]
    public void Translations_keep_the_same_placeholders(string file)
    {
        // "{0} jeux" translated "games" would throw a FormatException... or
        // silently drop the number.
        var french = ReadResx(file + ".resx");
        var english = ReadResx(file + ".en.resx");

        foreach (var (key, text) in french)
            Assert.True(Placeholders(text).SetEquals(Placeholders(english[key])), $"{key}: « {text} » / « {english[key]} »");
    }

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).ToHashSet();

    private static Dictionary<string, string> ReadResx(string relativePath) =>
        XDocument.Load(Path.Combine(SampleData.RepositoryRoot(), relativePath))
            .Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    // ---------- Texts prepared by the view models, in English ----------

    [Fact]
    public void Relative_times_read_naturally_in_English()
    {
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        TestCulture.In("en-GB", () =>
        {
            Assert.Equal("just now", ViewModels.RelativeTime.Format(now.AddSeconds(-20), now));
            Assert.Equal("5 min ago", ViewModels.RelativeTime.Format(now.AddMinutes(-5), now));
            Assert.Equal("yesterday", ViewModels.RelativeTime.Format(now.AddHours(-25), now));
            Assert.Equal("2 days ago", ViewModels.RelativeTime.Format(now.AddHours(-49), now));
        });
    }

    [Fact]
    public void Numbers_follow_the_language()
    {
        // 88 % / 88%, 187 500 / 187,500: the format and the number both translate.
        var game = new Core.Models.CatalogGame("steam:397540", "Borderlands 3", null, null, ["Shooter"],
            OnPc: false, OnConsole: false, LeavingSoon: false, AverageRating: 4.4, RatingCount: 187_500, SteamAppId: "397540");
        ViewModels.RecommendationRow Row() => new(new Core.Services.SimilarGame(
            new Core.Services.Recommendation(game, 0.5, []), 0.9, ["Shooter"], Core.Models.PlayModes.Solo));

        TestCulture.In("en-GB", () =>
        {
            var row = Row();
            Assert.Equal("88%", row.RatingValueLabel);
            Assert.Equal("187,500 Steam reviews", row.RatingCountLabel);
            Assert.Equal("Same style: Shooter · single-player", row.ReasonLabel);
        });
        TestCulture.In("fr-FR", () =>
        {
            var row = Row();
            Assert.Equal("88 %", row.RatingValueLabel);
            // French groups thousands with a narrow no-break space.
            Assert.Equal("187 500 avis Steam", row.RatingCountLabel);
            Assert.Equal("Même style : Tir · solo", row.ReasonLabel);
        });
    }

    [Theory]
    [InlineData("fr-FR", "https://www.xbox.com/fr-FR/games/store/-/9ABC", "https://store.steampowered.com/app/397540/?l=french")]
    [InlineData("en-GB", "https://www.xbox.com/en-GB/games/store/-/9ABC", "https://store.steampowered.com/app/397540/?l=english")]
    public void Store_pages_open_in_the_language_of_the_app(string culture, string xboxPage, string steamPage)
    {
        static ViewModels.RecommendationRow Row(Core.Models.CatalogGame game) => new(new Core.Services.SimilarGame(
            new Core.Services.Recommendation(game, 0.5, []), 0.9, ["Shooter"], Core.Models.PlayModes.None));
        var inGamePass = new Core.Models.CatalogGame("9ABC", "Space Marine 2", null, null, ["Shooter"],
            OnPc: true, OnConsole: true, LeavingSoon: false);
        var onSteam = new Core.Models.CatalogGame("steam:397540", "Borderlands 3", null, null, ["Shooter"],
            OnPc: false, OnConsole: false, LeavingSoon: false, SteamAppId: "397540");

        TestCulture.In(culture, () =>
        {
            Assert.Equal(new Uri(xboxPage), Row(inGamePass).XboxStorePage);
            Assert.Equal(new Uri(steamPage), Row(onSteam).SteamPage);
        });
    }

    // ---------- Choosing the language ----------

    [Theory]
    [InlineData(null, "fr-FR", "fr")]
    [InlineData(null, "fr-BE", "fr")]
    [InlineData(null, "en-US", "en")]
    // Any other Windows language: English, more widely read than French.
    [InlineData(null, "de-DE", "en")]
    // A choice beats Windows.
    [InlineData("en", "fr-FR", "en")]
    [InlineData("fr", "de-DE", "fr")]
    // An unknown value in settings.json (edited by hand): automatic.
    [InlineData("klingon", "fr-FR", "fr")]
    public void The_language_is_the_choice_or_else_the_Windows_one(string? setting, string windows, string expected)
    {
        Assert.Equal(expected, Localization.Resolve(setting, CultureInfo.GetCultureInfo(windows)));
    }

    [AvaloniaFact]
    public async Task Choosing_a_language_saves_it_and_asks_for_a_restart()
    {
        var vm = SampleData.Empty(TempFolder);
        Assert.False(vm.Language.NeedsRestart);

        string other = Localization.Current == Localization.French ? Localization.English : Localization.French;
        vm.Language.SelectedLanguage = vm.Language.LanguageOptions.Single(o => o.Code == other);

        Assert.True(vm.Language.NeedsRestart);
        // The hook saves without waiting (like every setting): wait for the file.
        var store = new SettingsStore(TempFolder);
        for (int i = 0; i < 100 && store.Load().Language != other; i++)
            await Task.Delay(20);
        Assert.Equal(other, store.Load().Language);
    }

    [AvaloniaFact]
    public void Loading_the_settings_does_not_rewrite_them()
    {
        // The hook also runs when the constructor loads the setting.
        var vm = SampleData.Empty(TempFolder);

        Assert.Null(vm.Language.SelectedLanguage.Code);
        Assert.False(File.Exists(Path.Combine(TempFolder, "settings.json")));
    }
}
