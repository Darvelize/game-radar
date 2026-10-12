using GameRadar.Core.Logging;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Logging;

public class ErrorLogTests : IDisposable
{
    private readonly TempFolder _root = new();
    private DateTime _now = new(2026, 9, 29, 16, 2, 41);

    public void Dispose() => _root.Dispose();

    private string Folder => Path.Combine(_root.Path, "logs");

    private ErrorLog Log() => new(Folder, "1.0.0", () => _now);

    private string Today => File.ReadAllText(Path.Combine(Folder, "2026-09-29.log"));

    [Fact]
    public void Write_AppendsADatedLineWithVersionContextAndMessage()
    {
        var log = Log();

        log.Write("Historique Xbox", "Impossible de joindre Xbox Live.");
        log.Write("Boutique Steam", "Réponse inattendue.");

        Assert.Equal(
            "2026-09-29 16:02:41 · 1.0.0 · Historique Xbox · Impossible de joindre Xbox Live." + Environment.NewLine
            + "2026-09-29 16:02:41 · 1.0.0 · Boutique Steam · Réponse inattendue." + Environment.NewLine,
            Today);
    }

    [Fact]
    public void Write_KeepsTheExceptionAndItsCause()
    {
        var error = new InvalidOperationException("outer", new TimeoutException("inner cause"));

        Log().Write("Recommandations", "Erreur inattendue", error);

        Assert.Contains("System.InvalidOperationException: outer", Today);
        Assert.Contains("System.TimeoutException: inner cause", Today);
    }

    [Theory]
    [InlineData("GET https://api.steampowered.com/IPlayerService/GetOwnedGames/v1?key=ABCDEF0123456789ABCDEF0123456789&steamid=7656", "ABCDEF0123456789")]
    [InlineData("clé 0123456789abcdef0123456789abcdef refusée", "0123456789abcdef")]
    [InlineData("grant_type=refresh_token&refresh_token=M.C5_BAY.secret-value&client_id=x", "secret-value")]
    [InlineData("""{ "RpsTicket": "d=EwBIA+secret", "SiteName": "user.auth.xboxlive.com" }""", "EwBIA")]
    [InlineData("""{"Token":"eyJhbGciOiJSUzI1NiJ9.xsts-secret"}""", "xsts-secret")]
    [InlineData("Authorization: XBL3.0 x=1234567890;eyJxsts.secret.part", "1234567890")]
    [InlineData("Authorization: Bearer abc.def-secret", "def-secret")]
    [InlineData("jeton eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl dans le message", "c2lnbmF0dXJl")]
    [InlineData("Cookie: npsso=AbCdEfSonySession42; other=1", "AbCdEfSonySession42")]
    [InlineData("""{ "npsso": "AbCdEfSonySession42" }""", "AbCdEfSonySession42")]
    // The secrets as the app itself names them: a record shown by ToString,
    // the JSON of the saved secrets, JSON escaped inside a string.
    [InlineData("SavedXboxAccount { RefreshToken = M.C5_BAY.record-secret, Gamertag = Radar }", "record-secret")]
    [InlineData("MicrosoftTokens { AccessToken = EwBIA-access-secret, RefreshToken = M.C5 }", "access-secret")]
    [InlineData("""{"RefreshToken":"M.C5_BAY.json-secret","Gamertag":"Radar"}""", "json-secret")]
    [InlineData("""{"ApiKey":"steam-api-secret","SteamId":"76561198000000000"}""", "steam-api-secret")]
    [InlineData("""body: {\"access_token\":\"EwB-escaped-secret\"}""", "escaped-secret")]
    // A bare NPSSO: 64 letters and digits (not only hex, unlike a SHA-256).
    [InlineData("jeton refusé : Kq7ZpRt2wXy9MnB4vC6dF8gH1jL3sA5eQ0uI2oP4rT6yU8iO0pA2sD4fG6hJ8kLz", "Kq7ZpRt2wXy9MnB4")]
    // An EA token with its base64 padding: masked up to the last "=".
    [InlineData("EA token QVQxOjIuMDozLjA6NjA6bWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFk== refused", "==")]
    public void Redact_MasksSecrets(string text, string secret)
    {
        string redacted = ErrorLog.Redact(text);

        Assert.DoesNotContain(secret, redacted);
        Assert.Contains("***", redacted);
    }

    [Fact]
    public void Redact_KeepsWhatHelpsTheDiagnosis()
    {
        // Game names, statuses, 64-hex picture file names (SHA-256) stay readable.
        const string text = "Genres introuvables pour Hades II (404) : images\\"
            + "0A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F9.img";

        Assert.Equal(text, ErrorLog.Redact(text));
    }

    [Fact]
    public void Write_MasksSecretsInTheFile()
    {
        Log().Write("Bibliothèque Steam", "Échec", new HttpRequestException("GET ...?key=ABCDEF0123456789ABCDEF0123456789 → 403"));

        Assert.DoesNotContain("ABCDEF0123456789", Today);
    }

    [Fact]
    public void Write_DeletesLogsOlderThanAWeekButNothingElse()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "2026-09-21.log"), "too old");
        File.WriteAllText(Path.Combine(Folder, "2026-09-22.log"), "7 days: kept");
        File.WriteAllText(Path.Combine(Folder, "notes.log"), "not ours");

        Log().Write("Test", "message");

        Assert.False(File.Exists(Path.Combine(Folder, "2026-09-21.log")));
        Assert.True(File.Exists(Path.Combine(Folder, "2026-09-22.log")));
        Assert.True(File.Exists(Path.Combine(Folder, "notes.log")));
    }

    [Fact]
    public void Write_StopsForTheDayOnceTheFileIsFull()
    {
        var log = Log();
        string line = new('x', 10_000);
        for (int i = 0; i < 150; i++)
            log.Write("Boucle", line);

        long size = new FileInfo(Path.Combine(Folder, "2026-09-29.log")).Length;
        Assert.InRange(size, ErrorLog.MaxBytesPerDay, ErrorLog.MaxBytesPerDay + 20_000);

        // A new day gets a new file.
        _now = _now.AddDays(1);
        log.Write("Boucle", "encore");
        Assert.True(File.Exists(Path.Combine(Folder, "2026-09-30.log")));
    }

    [Fact]
    public void Write_NeverThrowsWhenTheFolderCannotBeCreated()
    {
        // A file where the folder should be: Directory.CreateDirectory fails.
        Directory.CreateDirectory(_root.Path);
        File.WriteAllText(Folder, "a file, not a folder");

        var error = Record.Exception(() => Log().Write("Test", "message"));

        Assert.Null(error);
    }

    [Fact]
    public void Write_NeverThrowsEvenForAnExceptionThatThrowsWhenDisplayed()
    {
        var error = Record.Exception(() => Log().Write("UI", "message", new ThrowingException()));

        Assert.Null(error);
    }

    [Fact]
    public void Write_GoesOnWhenAnOldLogCannotBeDeleted()
    {
        Directory.CreateDirectory(Folder);
        string old = Path.Combine(Folder, "2026-09-01.log");
        File.WriteAllText(old, "too old, read-only");
        File.SetAttributes(old, FileAttributes.ReadOnly);
        try
        {
            var log = Log();
            log.Write("Test", "first");
            log.Write("Test", "second");

            // The cleanup failed, the log did not: both entries are there.
            Assert.Contains("first", Today);
            Assert.Contains("second", Today);
        }
        finally
        {
            File.SetAttributes(old, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Write_CutsAHugeEntryToTheBudgetOfTheDay()
    {
        Log().Write("Test", new string('x', 3 * (int)ErrorLog.MaxBytesPerDay));

        Assert.InRange(new FileInfo(Path.Combine(Folder, "2026-09-29.log")).Length, 1, ErrorLog.MaxBytesPerDay);
    }

    [Fact]
    public void Write_SharesTheFileWithAnotherGameRadarWritingToIt()
    {
        // The app restarting after a language change: the old process may
        // still write while the new one does.
        Directory.CreateDirectory(Folder);
        using (new FileStream(Path.Combine(Folder, "2026-09-29.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            Log().Write("Test", "written meanwhile");

        Assert.Contains("written meanwhile", Today);
    }

    private sealed class ThrowingException : Exception
    {
        public override string ToString() => throw new InvalidOperationException("broken ToString");
    }

    [Fact]
    public void Redact_KeepsTheNamesAroundTheMaskedSecrets()
    {
        string redacted = ErrorLog.Redact("SavedXboxAccount { RefreshToken = M.C5_BAY.secret, Gamertag = Radar }");

        // What the secret was, and the rest, stay readable for the diagnosis.
        Assert.Contains("RefreshToken", redacted);
        Assert.Contains("Gamertag = Radar", redacted);
    }

    [Fact]
    public void DeleteOldLogs_RemovesThemWithoutAnyNewError()
    {
        // An error in January, none since: the log must not linger for months.
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "2026-01-15.log"), "an old error");

        Log().DeleteOldLogs();

        Assert.False(File.Exists(Path.Combine(Folder, "2026-01-15.log")));
    }

    [Fact]
    public void Write_WritesNothingOnceErased()
    {
        // An error logged by a background task right after "Effacer toutes mes données".
        var log = Log();
        log.Write("Test", "before");
        log.EraseAll();

        log.Write("Test", "after");

        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void EraseAll_KeepsTheFilesItDidNotWrite()
    {
        var log = Log();
        log.Write("Test", "message");
        File.WriteAllText(Path.Combine(Folder, "notes.log"), "not ours");

        log.EraseAll();

        Assert.False(File.Exists(Path.Combine(Folder, "2026-09-29.log")));
        Assert.True(File.Exists(Path.Combine(Folder, "notes.log")));
    }

    [Fact]
    public void EraseAll_RemovesTheLogsAndTheFolder()
    {
        var log = Log();
        log.Write("Test", "message");

        log.EraseAll();

        Assert.False(Directory.Exists(Folder));
    }
}
