using System.Text;
using System.Text.RegularExpressions;
using GameRadar.Core.Settings;

namespace GameRadar.Core.Logging;

/// <summary>
/// Errors written to a local text file, one per day (logs\2026-09-29.log),
/// for a user to send when something goes wrong: nothing leaves the
/// computer on its own. Secrets are masked before writing (<see cref="Redact"/>).
/// Never throws: a log must never break what it observes.
/// </summary>
public sealed class ErrorLog
{
    /// <summary>Days of logs kept; older files are deleted at startup (<see cref="DeleteOldLogs"/>), then once a day.</summary>
    public const int DaysKept = 7;

    /// <summary>Per day: an error repeated in a loop must not fill the disk.</summary>
    public const long MaxBytesPerDay = 1_000_000;

    private readonly string _appVersion;
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();
    // The day the old logs were last deleted: again on the first write of
    // another day (an app left open for weeks).
    private DateTime? _cleanedUpOn;
    // "Effacer toutes mes données" done: nothing is written any more (a
    // background task's error would bring the folder back).
    private readonly EraseGuard _guard;

    /// <param name="folder">Folder of the log files, created at the first write.</param>
    /// <param name="appVersion">Written on each line: which version the error comes from.</param>
    /// <param name="now">Clock, for tests. Local time: the user reads these files.</param>
    /// <param name="guard">Shared "erased" state of the app's stores. Null = this log's own.</param>
    public ErrorLog(string folder, string appVersion, Func<DateTime>? now = null, EraseGuard? guard = null)
    {
        Folder = folder;
        _appVersion = appVersion;
        _now = now ?? (() => DateTime.Now);
        _guard = guard ?? new EraseGuard();
    }

    public string Folder { get; }

    /// <summary>
    /// Appends one entry: where it happened ("Historique Xbox"), what the user
    /// was told, and the exception with its stack trace when there is one.
    /// </summary>
    public void Write(string context, string message, Exception? exception = null)
    {
        // Everything in the try, and every exception caught: the log is what
        // the app's safety nets call, it must never break them, not even for
        // an exception that throws while displayed. Losing a log line (full
        // disk, read-only folder...) is better than crashing the app.
        try
        {
            var now = _now();
            string path = PathFor(now);
            // Built and masked outside the lock: the regexes over a stack
            // trace are the slow part, and need nothing shared.
            byte[] entry = Encoding.UTF8.GetBytes(Redact(Format(now, context, message, exception)));

            lock (_lock)
            {
                if (_guard.IsErased)
                    return;
                Directory.CreateDirectory(Folder);
                if (_cleanedUpOn != now.Date)
                {
                    _cleanedUpOn = now.Date;
                    DeleteOldFiles(now);
                }

                // FileShare.ReadWrite: another GameRadar may be writing to the
                // same file (the app restarting after a language change).
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                // A new file starts with the UTF-8 mark, counted too.
                byte[] start = file.Length == 0 ? [.. Encoding.UTF8.Preamble] : [];
                long budget = MaxBytesPerDay - file.Length - start.Length;
                if (budget <= 0)
                    return;
                // One huge entry (an error quoting a whole API answer) must not
                // pass the limit of the day either: cut, and said so.
                if (entry.Length > budget)
                    entry = [.. entry.Take((int)Math.Max(0, budget - CutMark.Length)), .. CutMark];
                file.Write(start);
                file.Write(entry);
            }
        }
        catch (Exception)
        {
        }
    }

    private static readonly byte[] CutMark = Encoding.UTF8.GetBytes(Environment.NewLine + "    [...] cut: the log of the day is full" + Environment.NewLine);

    private string Format(DateTime now, string context, string message, Exception? exception)
    {
        var entry = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))
            .Append(" · ").Append(_appVersion)
            .Append(" · ").Append(context)
            .Append(" · ").AppendLine(message);
        if (exception is not null)
        {
            // ToString(): type, message, inner exceptions and stack trace.
            foreach (string line in exception.ToString().Split('\n'))
                entry.Append("    ").AppendLine(line.TrimEnd('\r'));
        }
        return entry.ToString();
    }

    /// <summary>
    /// Deletes the logs older than <see cref="DaysKept"/> days. Called at
    /// startup: without any new error, an old one would otherwise stay on
    /// the computer for good. Never throws.
    /// </summary>
    public void DeleteOldLogs()
    {
        try
        {
            lock (_lock)
            {
                var now = _now();
                _cleanedUpOn = now.Date;
                if (Directory.Exists(Folder))
                    DeleteOldFiles(now);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Deletes the log files, then the folder if empty ("Effacer toutes mes
    /// données"); nothing is written afterwards. Only its own files (named
    /// after a date), like every store: another file keeps its folder.
    /// </summary>
    public void EraseAll()
    {
        lock (_lock)
        {
            _guard.Trip();
            foreach (var (path, _) in OwnLogs())
                File.Delete(path);
            LocalFiles.DeleteFolderIfEmpty(Folder);
        }
    }

    private string PathFor(DateTime day) =>
        Path.Combine(Folder, day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".log");

    // The log files this class wrote: named after a date.
    private IEnumerable<(string Path, DateTime Day)> OwnLogs()
    {
        if (!Directory.Exists(Folder))
            yield break;
        foreach (string path in Directory.EnumerateFiles(Folder, "*.log"))
        {
            if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day))
                yield return (path, day);
        }
    }

    // Older than DaysKept days. Each one tried on its own: a file that cannot
    // go (read-only, opened) stays, and never stops the log.
    private void DeleteOldFiles(DateTime now)
    {
        foreach (var (path, _) in OwnLogs().Where(l => l.Day < now.Date.AddDays(-DaysKept)).ToList())
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // ---------- Secrets ----------

    private static readonly (Regex Pattern, string Replacement)[] SecretPatterns =
    [
        // Steam Web API key in a URL (&key=...), and any bare 32-hex key.
        (new Regex(@"([?&]key=)[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1***"),
        (new Regex(@"\b[0-9a-f]{32}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "***"),
        // OAuth values in a query or a form: code, access / refresh / id tokens...
        (new Regex(@"\b(code|access_token|refresh_token|id_token|client_secret|code_verifier)=[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1=***"),
        // The same in JSON, plus Xbox Live's RpsTicket and Token fields, and
        // the app's own names (the saved secrets: RefreshToken, ApiKey,
        // Npsso...) and Epic's code page (authorizationCode); also JSON escaped
        // inside a string (\"access_token\").
        (new Regex(@"\\?""(access_token|refresh_token|id_token|Token|RpsTicket|AccessToken|RefreshToken|IdToken|ApiKey|Npsso|authorizationCode)\\?""\s*:\s*\\?""[^""\\]*\\?""", RegexOptions.IgnoreCase | RegexOptions.Compiled), @"""$1"":""***"""),
        // A record shown by ToString: "SavedXboxAccount { RefreshToken = ..., Gamertag = ... }".
        (new Regex(@"\b(AccessToken|RefreshToken|IdToken|Token|ApiKey|Npsso)\s*=\s*[^,}\s]+", RegexOptions.Compiled), "$1 = ***"),
        // Authorization headers: XBL3.0 x=<uhs>;<XSTS>, Bearer <token>.
        (new Regex(@"XBL3\.0 x=[^\s""']+", RegexOptions.Compiled), "XBL3.0 x=***"),
        (new Regex(@"\bBearer\s+[^\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Bearer ***"),
        // Sony's session cookie: npsso=... in a header ("npsso":"..." in JSON
        // is above), and bare: 64 letters and digits, with at least one
        // letter past "f" (a SHA-256 picture name, 64 hex, stays readable).
        (new Regex(@"\bnpsso=[^;&\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "npsso=***"),
        (new Regex(@"\b(?=[A-Za-z0-9]{64}\b)(?=[A-Za-z0-9]*[g-zG-Z])[A-Za-z0-9]{64}\b", RegexOptions.Compiled), "***"),
        // EA access tokens: ~84 letters and digits, base64 of "AT1:..." (measured), maybe "=" padded.
        (new Regex(@"\bQVQ[A-Za-z0-9]{40,}={0,2}", RegexOptions.Compiled), "***"),
        // JSON Web Tokens (eyJ...), wherever they appear.
        (new Regex(@"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*", RegexOptions.Compiled), "***"),
    ];

    /// <summary>
    /// Masks the secrets an error could carry: Steam key (URL or bare),
    /// Microsoft, Xbox, PlayStation and EA tokens, Sony's NPSSO, authorization headers. Applied to the whole
    /// entry, exception included, so no code path can leak one.
    /// </summary>
    public static string Redact(string text)
    {
        foreach (var (pattern, replacement) in SecretPatterns)
            text = pattern.Replace(text, replacement);
        return text;
    }
}
