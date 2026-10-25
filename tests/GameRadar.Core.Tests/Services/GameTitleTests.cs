using GameRadar.Core.Services;

namespace GameRadar.Core.Tests.Services;

public class GameTitleTests
{
    [Theory]
    [InlineData("Avowed Standard Edition", "Avowed")]
    [InlineData("Gears 5 édition Jeu de l'année", "Gears 5")]
    [InlineData("Dead Island 2 (Windows)", "Dead Island 2")]
    [InlineData("EA SPORTS FC™ 25 pour Xbox Series X|S", "EA SPORTS FC 25")]
    [InlineData("Pillars of Eternity II: Deadfire - Ultimate Edition (PC)", "Pillars of Eternity II: Deadfire")]
    [InlineData("Heroes of Might and Magic: Olden Era (Aperçu)", "Heroes of Might and Magic: Olden Era")]
    [InlineData("Assassin's Creed Chronicles : China", "Assassin's Creed Chronicles: China")]
    [InlineData("Half-Life 2", "Half-Life 2")]
    // The platform after a dash: the dash goes with it.
    [InlineData("Grounded - PC", "Grounded")]
    [InlineData("Halo Infinite - Xbox One", "Halo Infinite")]
    [InlineData("Dead Cells - PS4 & PS5", "Dead Cells")]
    [InlineData("", "")]
    [InlineData("-", "-")]
    // "PlayStation" elsewhere than a final console version stays.
    [InlineData("PlayStation VR Worlds", "PlayStation VR Worlds")]
    public void Clean_KeepsOnlyTheGameItself(string storeTitle, string expected)
    {
        Assert.Equal(expected, GameTitle.Clean(storeTitle));
    }

    // Pairs found by the calibration on a real history: one game each time.
    [Theory]
    [InlineData("Control", "Control Ultimate Edition - Xbox Series X|S")]
    [InlineData("Sleeping Dogs™", "Sleeping Dogs: Definitive Edition")]
    [InlineData("DOOM Eternal", "DOOM Eternal Standard Edition")]
    [InlineData("Fallout 3", "Fallout 3: Game of the Year Edition")]
    [InlineData("The Elder Scrolls V: Skyrim Special Edition (PC)", "The Elder Scrolls V: Skyrim Special Edition")]
    [InlineData("Call of Duty: Vanguard - Édition Standard", "Call of Duty: Vanguard - Pack Cross-gen")]
    [InlineData("Mass Effect™: Andromeda", "Édition Deluxe de Mass Effect™: Andromeda")]
    [InlineData("Halo Wars", "Édition finale de Halo Wars")]
    // A platform's edition (found with PlayStation: FF XV platinum on PS4,
    // its Windows edition still recommended on Steam).
    [InlineData("FINAL FANTASY XV", "FINAL FANTASY XV WINDOWS EDITION")]
    [InlineData("Genesis Noir", "Genesis Noir: Windows Edition")]
    [InlineData("Goat Simulator 3", "Goat Simulator 3: Xbox One Edition")]
    [InlineData("Cities: Skylines II", "Cities: Skylines II - PC Edition")]
    // A bare platform at the end, or the console edition (found by measuring the
    // games without Steam tags, oct. 2026).
    [InlineData("Dead by Daylight", "Dead by Daylight Windows")]
    [InlineData("Enlisted", "Enlisted Xbox One")]
    [InlineData("Anno 1800", "Anno 1800™ Console Edition")]
    [InlineData("FINAL FANTASY XII THE ZODIAC AGE", "FINAL FANTASY Ⅻ THE ZODIAC AGE")]
    // PlayStation titles carry their console versions (PS Plus catalog, played games, oct. 2026).
    [InlineData("Dead Cells", "Dead Cells (PS4® & PS5®)")]
    [InlineData("Lake", "Lake PS4 & PS5")]
    [InlineData("Dying Light 2 Stay Human", "Dying Light 2 Stay Human PS4&PS5")]
    [InlineData("Sniper Elite 5", "Sniper Elite 5 PS4™ & PS5™")]
    [InlineData("Insurgency: Sandstorm", "Insurgency: Sandstorm [PS4 & PS5]")]
    [InlineData("The Elder Scrolls Online", "The Elder Scrolls Online (PS5)")]
    [InlineData("Hogwarts Legacy", "Hogwarts Legacy PS4 Version")]
    [InlineData("Green Hell", "Green Hell PS4 + PS5 Free Upgrade")]
    // The console written in full (PlayStation Store best sellers, oct. 2026).
    [InlineData("Wreckfest", "Wreckfest PlayStation®5 Version")]
    [InlineData("Hogwarts Legacy", "Hogwarts Legacy PlayStation 4 Version")]
    // After "for" / "pour", like "for Xbox": the whole console goes, not just its name.
    [InlineData("Lake", "Lake for PlayStation 4")]
    [InlineData("Lake", "Lake pour PS5")]
    [InlineData("Ghost of Tsushima DIRECTOR’S CUT", "Ghost of Tsushima DIRECTOR’S CUT (PlayStation Plus)")]
    // An em dash separates an edition too (a Steam store title).
    [InlineData("The Witcher 3: Wild Hunt", "The Witcher 3: Wild Hunt — Remastered")]
    // The edition still sold of a Steam game no longer sold (oct. 2026).
    [InlineData("Titan Quest", "Titan Quest Anniversary Edition")]
    public void Key_IsTheSameForEditionsOfOneGame(string a, string b)
    {
        Assert.Equal(GameTitle.Key(a), GameTitle.Key(b));
    }

    [Theory]
    [InlineData("Hades", "Hades II")]
    [InlineData("Ridge Racer", "R4 RIDGE RACER TYPE 4® PS4 & PS5")]
    [InlineData("Dead Space (2008)", "Dead Space")]
    [InlineData("Assassin's Creed Rogue", "Assassin's Creed Rogue Remastered")]
    // "Anniversary" without "Edition" names a remake: another game.
    [InlineData("Halo: Combat Evolved", "Halo: Combat Evolved Anniversary")]
    public void Key_KeepsDifferentGamesApart(string a, string b)
    {
        Assert.NotEqual(GameTitle.Key(a), GameTitle.Key(b));
    }
}
