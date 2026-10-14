using CommunityToolkit.Mvvm.ComponentModel;

namespace GameRadar.App.ViewModels;

/// <summary>How a download asked to a <see cref="FetchLine"/> ended.</summary>
public enum FetchResult
{
    Done,
    Failed,
    /// <summary>Already being downloaded: nothing was started.</summary>
    AlreadyRunning
}

/// <summary>
/// Something downloaded on demand with its own status line in Settings › Data:
/// a platform's history (<see cref="SyncedPlatform"/>) or a candidate catalog
/// (<see cref="CatalogLine"/>). Never downloaded twice at once; "fetching" and
/// the last error stay on the line until the download ends or the next one starts.
/// </summary>
public abstract partial class FetchLine : ObservableObject
{
    /// <summary>Context of its errors in the log, in fixed English.</summary>
    public required string LogContext { get; init; }

    public required string FetchingStatus { get; init; }

    /// <summary>The message of a failed download, from the most specific error to the most generic.</summary>
    public required Func<Exception, string> ErrorMessage { get; init; }

    /// <summary>Its state, or the current operation.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Why the last download failed; kept on the line until the next one.</summary>
    public string? Error { get; private set; }

    // The download running, if any.
    private Task<FetchResult>? _running;

    public bool IsFetching => _running is not null;

    /// <summary>
    /// Writes its status line, unless a download is running (it says so until
    /// the end) or the last one failed (the error stays readable): a history
    /// rebuild or a search must not write the old state over either.
    /// </summary>
    public void ShowStatus(string status)
    {
        if (_running is null)
            Status = Error ?? status;
    }

    /// <summary>An error met before any download (a profile name refused): kept on the line like a failed one.</summary>
    public void KeepError(string message)
    {
        Error = message;
        Status = message;
    }

    /// <summary>The download running, or a finished task.</summary>
    public Task WhenFetchedAsync() => _running ?? Task.CompletedTask;

    /// <summary>
    /// Runs <paramref name="download"/>, unless one is already running. A failure
    /// is on its status line and in the log, and in the banner unless
    /// <paramref name="banner"/> is false (the page in use says it itself).
    /// </summary>
    protected Task<FetchResult> RunFetchAsync(Func<Task> download, NoticeCenter notices, bool banner = true)
    {
        if (_running is not null)
            return Task.FromResult(FetchResult.AlreadyRunning);

        var task = RunAsync();
        // Ended at once: its finally already ran, nothing to keep.
        if (!task.IsCompleted)
            _running = task;
        return task;

        // No ConfigureAwait(false): back on the UI thread to update the line.
        async Task<FetchResult> RunAsync()
        {
            Error = null;
            Status = FetchingStatus;
            try
            {
                await download();
                // An error kept meanwhile (KeepError) is older than this success.
                Error = null;
                return FetchResult.Done;
            }
            catch (Exception ex)
            {
                Error = ErrorMessage(ex);
                Status = Error;
                if (banner)
                    notices.Report(LogContext, Error, ex);
                else
                    notices.Log(LogContext, Error, ex);
                return FetchResult.Failed;
            }
            finally
            {
                _running = null;
            }
        }
    }
}
