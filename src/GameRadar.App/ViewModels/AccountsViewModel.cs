using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.Core.Cache;
using GameRadar.Core.EA;
using GameRadar.Core.Epic;
using GameRadar.Core.GOG;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Steam;
using GameRadar.Core.Xbox;

namespace GameRadar.App.ViewModels;

// The accounts (Xbox, Steam, PlayStation, GOG profile, EA token, Epic) and the platforms they sync.
public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly HttpClient _http;
    private readonly XboxAccount _account;
    private readonly SteamAccount _steamAccount;
    private readonly PlayStationAccount _playStationAccount;
    private readonly EpicAccount _epicAccount;
    private readonly SettingsSaver _saver;
    private readonly GogHistorySource _gogSource;
    private readonly EaHistorySource _eaSource;
    private readonly EpicHistorySource _epicSource;
    // The token pasted from EA's page: in memory only, for the session (EA
    // makes it last 4 h); forgotten when EA refuses it. Never saved, never shown.
    private string? _eaToken;
    private readonly Action<Uri> _openBrowser;
    private readonly NoticeCenter _notices;
    private readonly Func<Task> _historyFetched;

    /// <param name="historyFetched">Called once new history is in: the window rebuilds its lists and the recommendations.</param>
    public AccountsViewModel(
        HttpClient http,
        CacheStore cache,
        XboxAccount account,
        SteamAccount steamAccount,
        PlayStationAccount playStationAccount,
        EpicAccount epicAccount,
        SettingsSaver saver,
        Action<Uri> openBrowser,
        NoticeCenter notices,
        Func<Task> historyFetched)
    {
        _http = http;
        _account = account;
        _steamAccount = steamAccount;
        _playStationAccount = playStationAccount;
        _epicAccount = epicAccount;
        _saver = saver;
        _gogSource = new GogHistorySource(cache);
        _eaSource = new EaHistorySource(cache);
        _epicSource = new EpicHistorySource(cache);
        _openBrowser = openBrowser;
        _notices = notices;
        _historyFetched = historyFetched;
        (XboxSync, SteamSync, PlayStationSync, GogSync, EaSync, EpicSync) = CreateSyncedPlatforms(cache);
        SyncedPlatforms = [XboxSync, SteamSync, PlayStationSync, GogSync, EaSync, EpicSync];
        // One listener for every platform, now and for the next one: no flag
        // and no OnXChanged hook to add here when a platform joins the list.
        foreach (var platform in SyncedPlatforms)
        {
            platform.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SyncedPlatform.IsConnected))
                    OnAccountChanged();
            };
        }
    }

    public SyncedPlatform XboxSync { get; }
    public SyncedPlatform SteamSync { get; }
    public SyncedPlatform PlayStationSync { get; }
    public SyncedPlatform GogSync { get; }
    public SyncedPlatform EaSync { get; }
    public SyncedPlatform EpicSync { get; }
    public IReadOnlyList<SyncedPlatform> SyncedPlatforms { get; }

    /// <summary>An account signed in or out: the platforms' sidebar lines follow (the window listens).</summary>
    public event Action? AccountChanged;

    // An account signed in or out, whichever: the sync button, the welcome
    // screen (HasAccount) and the platform's sidebar line follow. One place,
    // called when any platform's IsConnected changes (see the constructor).
    private void OnAccountChanged()
    {
        OnPropertyChanged(nameof(HasAccount));
        RefreshHistoryCommand.NotifyCanExecuteChanged();
        AccountChanged?.Invoke();
    }

    /// <summary>Reads the accounts' state (at startup, once the window is listening to <see cref="AccountChanged"/>).</summary>
    public void UpdateAll()
    {
        UpdateAccount();
        UpdateSteamAccount();
        UpdatePlayStationAccount();
        UpdateGogAccount();
        UpdateEaAccount();
        UpdateEpicAccount();
    }

    // Errors go to the detailed status line (Settings page), then to the
    // banner and the error log (NoticeCenter).
    private void ReportSyncError(SyncedPlatform platform, string message, Exception? ex = null)
    {
        platform.Status = message;
        _notices.Report(platform.LogContext, message, ex);
    }

    // ============ Xbox account ============

    [ObservableProperty]
    public partial string AccountLabel { get; set; } = string.Empty;

    private void UpdateAccount()
    {
        XboxSync.IsConnected = _account.IsSignedIn;
        AccountLabel = _account.Gamertag is { } gamertag
            ? Localization.Format(Strings.Account_SignedInAs, gamertag)
            : XboxSync.IsConnected ? Strings.Account_SignedIn : Strings.Account_NotSignedIn;
    }

    // IncludeCancelCommand generates SignInCancelCommand, which cancels the
    // CancellationToken below: the "Annuler" button while the browser is open.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SignInAsync(CancellationToken ct)
    {
        XboxSync.Status = Strings.Account_SigningIn;

        try
        {
            await _account.SignInAsync(_openBrowser, ct);
            UpdateAccount();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            XboxSync.Status = Strings.Account_SignInCancelled;
            return;
        }
        catch (Exception ex)
        {
            ReportSyncError(XboxSync, NoticeCenter.ServiceErrorMessage(
                ex, Strings.Error_MicrosoftUnreachable, Strings.Error_MicrosoftSlow, unexpected: Strings.Error_UnexpectedSignIn), ex);
            return;
        }

        // Signed in: the history is what the user came for.
        await RefreshConnectedAsync(XboxSync);
    }

    [RelayCommand]
    private void SignOut()
    {
        _account.SignOut();
        UpdateAccount();
        XboxSync.Status = Strings.Account_XboxSignedOut;
    }

    // ============ Steam account ============

    [ObservableProperty]
    public partial string SteamAccountLabel { get; set; } = string.Empty;

    /// <summary>Profile URL typed in Settings.</summary>
    [ObservableProperty]
    public partial string SteamProfileInput { get; set; } = string.Empty;

    /// <summary>Web API key typed in Settings; cleared once saved (encrypted).</summary>
    [ObservableProperty]
    public partial string SteamKeyInput { get; set; } = string.Empty;

    private void UpdateSteamAccount()
    {
        SteamSync.IsConnected = _steamAccount.IsConnected;
        SteamAccountLabel = _steamAccount.PersonaName is { } name
            ? Localization.Format(Strings.Account_SignedInAs, name)
            : SteamSync.IsConnected ? Strings.Account_SignedIn : Strings.Account_NotSignedIn;
    }

    [RelayCommand]
    private async Task ConnectSteamAsync()
    {
        SteamSync.Status = Strings.Account_SteamChecking;

        try
        {
            await _steamAccount.ConnectAsync(_http, SteamKeyInput, SteamProfileInput);
            // The key now lives encrypted on disk: no need to keep it on screen.
            SteamKeyInput = string.Empty;
            UpdateSteamAccount();
        }
        catch (Exception ex)
        {
            ReportSyncError(SteamSync, NoticeCenter.ServiceErrorMessage(ex, Strings.Error_SteamUnreachable, Strings.Error_SteamSlow), ex);
            return;
        }

        await RefreshConnectedAsync(SteamSync);
    }

    [RelayCommand]
    private void DisconnectSteam()
    {
        _steamAccount.Disconnect();
        UpdateSteamAccount();
        SteamSync.Status = Strings.Account_SteamSignedOut;
    }

    // ============ PlayStation account (experimental) ============

    // Where the user signs in, then where Sony shows the NPSSO once signed in.
    private static readonly Uri PlayStationSignInPage = new("https://www.playstation.com/");
    private static readonly Uri NpssoPage = new("https://ca.account.sony.com/api/v1/ssocookie");

    [ObservableProperty]
    public partial string PlayStationAccountLabel { get; set; } = string.Empty;

    /// <summary>NPSSO pasted in Settings; cleared once saved (encrypted).</summary>
    [ObservableProperty]
    public partial string NpssoInput { get; set; } = string.Empty;

    private void UpdatePlayStationAccount()
    {
        PlayStationSync.IsConnected = _playStationAccount.IsConnected;
        PlayStationAccountLabel = PlayStationSync.IsConnected ? Strings.Account_SignedIn : Strings.Account_NotSignedIn;
    }

    [RelayCommand]
    private void OpenPlayStationSignIn() => _openBrowser(PlayStationSignInPage);

    [RelayCommand]
    private void OpenNpssoPage() => _openBrowser(NpssoPage);

    [RelayCommand]
    private async Task ConnectPlayStationAsync()
    {
        PlayStationSync.Status = Strings.Account_PlayStationChecking;

        try
        {
            await _playStationAccount.ConnectAsync(NpssoInput);
        }
        catch (Exception ex)
        {
            ReportSyncError(PlayStationSync, NoticeCenter.ServiceErrorMessage(
                ex, Strings.Error_PlayStationUnreachable, Strings.Error_PlayStationSlow), ex);
            return;
        }
        finally
        {
            // A secret: never left on screen, whether Sony took it or not.
            NpssoInput = string.Empty;
        }
        UpdatePlayStationAccount();

        await RefreshConnectedAsync(PlayStationSync);
    }

    [RelayCommand]
    private void DisconnectPlayStation()
    {
        _playStationAccount.Disconnect();
        UpdatePlayStationAccount();
        PlayStationSync.Status = Strings.Account_PlayStationSignedOut;
    }

    // ============ Epic account (the Epic launcher's identity) ============

    // Where the user signs in, then Epic's page that shows the code once signed in.
    private static readonly Uri EpicSignInPage = new("https://www.epicgames.com/");

    [ObservableProperty]
    public partial string EpicAccountLabel { get; set; } = string.Empty;

    /// <summary>Epic's code page pasted in Settings; cleared at once (a secret, never left on screen).</summary>
    [ObservableProperty]
    public partial string EpicCodeInput { get; set; } = string.Empty;

    private void UpdateEpicAccount()
    {
        EpicSync.IsConnected = _epicAccount.IsConnected;
        EpicAccountLabel = EpicSync.IsConnected ? Strings.Account_SignedIn : Strings.Account_NotSignedIn;
    }

    [RelayCommand]
    private void OpenEpicSignIn() => _openBrowser(EpicSignInPage);

    [RelayCommand]
    private void OpenEpicCodePage() => _openBrowser(new Uri(EpicClient.CodePageUrl));

    [RelayCommand]
    private async Task ConnectEpicAsync()
    {
        EpicSync.Status = Strings.Account_EpicChecking;

        try
        {
            await _epicAccount.ConnectAsync(EpicCodeInput);
        }
        catch (Exception ex)
        {
            ReportSyncError(EpicSync, NoticeCenter.ServiceErrorMessage(
                ex, Strings.Error_EpicUnreachable, Strings.Error_EpicSlow), ex);
            return;
        }
        finally
        {
            // A secret: never left on screen, whether Epic took it or not.
            EpicCodeInput = string.Empty;
        }
        UpdateEpicAccount();

        await RefreshConnectedAsync(EpicSync);
    }

    [RelayCommand]
    private void DisconnectEpic()
    {
        _epicAccount.Disconnect();
        UpdateEpicAccount();
        EpicSync.Status = Strings.Account_EpicSignedOut;
    }

    // ============ GOG profile ============

    [ObservableProperty]
    public partial string GogAccountLabel { get; set; } = string.Empty;

    /// <summary>Profile name or address typed in Settings.</summary>
    [ObservableProperty]
    public partial string GogProfileInput { get; set; } = string.Empty;

    // The profile name being added, read by GogSync.Fetch until GOG answered.
    private string? _pendingGogUser;

    // A public profile, no sign-in: "connected" means a profile name is saved.
    private void UpdateGogAccount()
    {
        GogSync.IsConnected = _saver.Settings.GogUser is not null;
        GogAccountLabel = _saver.Settings.GogUser is { } user
            ? Localization.Format(Strings.Account_SignedInAs, user)
            : Strings.Account_NotSignedIn;
    }

    /// <summary>
    /// Reads the profile at once (1 request per 50 games): it proves the name
    /// and the privacy setting, and brings the history. The name is only kept
    /// once GOG answered.
    /// </summary>
    [RelayCommand]
    private async Task ConnectGogAsync()
    {
        string user;
        try
        {
            // Not a profile name or address: explained before any request.
            user = GogClient.RequireUser(GogProfileInput);
        }
        catch (GogException ex)
        {
            string message = GogSync.ErrorMessage(ex);
            GogSync.KeepError(message);
            _notices.Report(GogSync.LogContext, message, ex);
            return;
        }

        // Fetched like any sync (status, errors, one fetch at a time), under the name typed.
        _pendingGogUser = user;
        try
        {
            // A sync running under the former name: this one, once it ends.
            if (await FetchConnectedAsync(GogSync) != FetchResult.Done)
                return;
        }
        finally
        {
            _pendingGogUser = null;
        }

        _saver.Settings.GogUser = user;
        GogProfileInput = string.Empty;
        UpdateGogAccount();
        await SaveGogUserAsync();
        await _historyFetched();
    }

    [RelayCommand]
    private async Task DisconnectGogAsync()
    {
        _saver.Settings.GogUser = null;
        UpdateGogAccount();
        GogSync.Status = Strings.Account_GogSignedOut;
        await SaveGogUserAsync();
    }

    // The profile name is a setting: a disk that refuses it is told, the app goes on.
    private async Task SaveGogUserAsync()
    {
        try
        {
            await _saver.SaveAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notices.Show(Localization.Format(Strings.Error_SaveChoice, ex.Message));
        }
    }

    // ============ EA app (token pasted from EA's page) ============

    [ObservableProperty]
    public partial string EaAccountLabel { get; set; } = string.Empty;

    /// <summary>EA's page pasted in Settings; cleared at once (a secret, never left on screen).</summary>
    [ObservableProperty]
    public partial string EaTokenInput { get; set; } = string.Empty;

    // "Connected" while a token pasted this session is in memory: the general
    // sync takes EA then, and stops once EA refuses the token.
    private void UpdateEaAccount()
    {
        EaSync.IsConnected = _eaToken is not null;
        EaAccountLabel = EaSync.IsConnected ? Strings.Account_EaTokenInMemory : Strings.Account_NotSignedIn;
    }

    // Where the user signs in, then EA's page that shows the token once signed in.
    private static readonly Uri EaSignInPage = new("https://www.ea.com/");

    [RelayCommand]
    private void OpenEaSignIn() => _openBrowser(EaSignInPage);

    [RelayCommand]
    private void OpenEaPage() => _openBrowser(new Uri(EaClient.TokenPageUrl));

    [RelayCommand]
    private async Task UpdateEaAsync()
    {
        try
        {
            _eaToken = EaClient.RequireToken(EaTokenInput);
        }
        catch (EaException ex)
        {
            ReportSyncError(EaSync, ex.Message, ex);
            return;
        }
        finally
        {
            // A secret: never left on screen, whether it was a token or not.
            EaTokenInput = string.Empty;
        }
        UpdateEaAccount();

        await RefreshConnectedAsync(EaSync);
    }

    /// <summary>A sign-in, a connection or a sync is running: it may write files (erasing must wait).</summary>
    public bool IsBusy =>
        RefreshHistoryCommand.IsRunning || SignInCommand.IsRunning
        || ConnectSteamCommand.IsRunning || ConnectPlayStationCommand.IsRunning || ConnectGogCommand.IsRunning
        || UpdateEaCommand.IsRunning || ConnectEpicCommand.IsRunning;

    // ============ History sync ============

    /// <summary>Some account connected: the welcome screen offers to load its history.</summary>
    public bool HasAccount => SyncedPlatforms.Any(p => p.IsConnected);

    // Generates RefreshHistoryCommand. Being async, the command disables itself
    // while running: a double click cannot send the requests twice.
    /// <summary>
    /// Fetches every connected platform, all at once and each on its own: an
    /// error on one does not stop the others. Then the lists and the
    /// recommendations, once.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasAccount))]
    private async Task RefreshHistoryAsync()
    {
        var fetched = await Task.WhenAll(SyncedPlatforms.Where(p => p.IsConnected).Select(p => p.FetchAsync(_notices)));
        if (fetched.Contains(FetchResult.Done))
            await _historyFetched();
    }

    /// <summary>The account just connected (or its new token): its history, then the lists and the recommendations.</summary>
    private async Task RefreshConnectedAsync(SyncedPlatform platform)
    {
        if (await FetchConnectedAsync(platform) == FetchResult.Done)
            await _historyFetched();
    }

    /// <summary>
    /// Fetches the account just connected. A sync still running under the former one
    /// (signed out meanwhile, a token replaced) is waited for, then this one runs:
    /// skipped, the new account's history would never come.
    /// </summary>
    private async Task<FetchResult> FetchConnectedAsync(SyncedPlatform platform)
    {
        var result = await platform.FetchAsync(_notices);
        if (result != FetchResult.AlreadyRunning)
            return result;
        await platform.WhenFetchedAsync();
        return await platform.FetchAsync(_notices);
    }

    /// <summary>Reads the cached histories, all at once (separate files).</summary>
    public Task LoadCachedAsync() =>
        Task.WhenAll(SyncedPlatforms.Select(async platform => platform.History = await Task.Run(platform.LoadCached)));

    // ============ The synced platforms ============

    /// <summary>
    /// The synced platforms: what each one reads, fetches and shows. A new
    /// platform is one more entry here, then in SyncedPlatforms.
    /// </summary>
    private (SyncedPlatform Xbox, SyncedPlatform Steam, SyncedPlatform PlayStation, SyncedPlatform Gog, SyncedPlatform Ea, SyncedPlatform Epic) CreateSyncedPlatforms(CacheStore cache)
    {
        var xbox = new XboxHistorySource(cache);
        var steam = new SteamHistorySource(cache);
        var playStation = new PlayStationHistorySource(cache);

        return (
            new SyncedPlatform
            {
                Platform = XboxLiveClient.PlatformName,
                Title = Strings.Common_XboxHistory,
                LogContext = "Xbox history",
                AlwaysShown = true,
                FetchingStatus = Strings.Status_FetchingXbox,
                NoHistoryStatus = Strings.Status_NoXboxHistory,
                LoadCached = xbox.LoadCached,
                Fetch = _ => xbox.RefreshAsync(new XboxLiveClient(_http, _account)),
                ErrorMessage = ex => NoticeCenter.ServiceErrorMessage(
                    ex, Strings.Error_XboxUnreachable, Strings.Error_XboxSlow, Strings.Error_XboxFormat),
                AfterFetch = UpdateAccount,
                Describe = h => Localization.Format(Strings.Status_XboxHistory, h.Games.Count,
                    h.Games.Count(g => g.Status == PlayStatus.Mastered), h.FetchedAt.ToLocalTime())
            },
            new SyncedPlatform
            {
                Platform = SteamClient.PlatformName,
                Title = Strings.Common_SteamHistory,
                LogContext = "Steam library",
                FetchingStatus = Strings.Status_FetchingSteam,
                NoHistoryStatus = Strings.Status_NoSteamHistory,
                LoadCached = steam.LoadCached,
                // The achievements are fetched one game at a time: their count on the status line.
                Fetch = platform => steam.RefreshAsync(_steamAccount.CreateClient(_http), _steamAccount.SteamId!,
                    new Progress<(int Done, int Total)>(p =>
                        platform.Status = Localization.Format(Strings.Status_FetchingSteamAchievements, p.Done, p.Total))),
                ErrorMessage = ex => NoticeCenter.ServiceErrorMessage(
                    ex, Strings.Error_SteamUnreachable, Strings.Error_SteamSlow, Strings.Error_SteamFormat),
                Describe = h => Localization.Format(Strings.Status_SteamHistory, h.Games.Count,
                    h.Games.Count(g => !g.NeverPlayed), h.Games.Count(g => g.Status == PlayStatus.Mastered),
                    h.FetchedAt.ToLocalTime())
            },
            new SyncedPlatform
            {
                Platform = PlayStationClient.PlatformName,
                Title = Strings.Common_PlayStationHistory,
                LogContext = "PlayStation history",
                FetchingStatus = Strings.Status_FetchingPlayStation,
                NoHistoryStatus = Strings.Status_NoPlayStationHistory,
                LoadCached = playStation.LoadCached,
                Fetch = _ => playStation.RefreshAsync(new PlayStationClient(_http, _playStationAccount)),
                ErrorMessage = ex => NoticeCenter.ServiceErrorMessage(
                    ex, Strings.Error_PlayStationUnreachable, Strings.Error_PlayStationSlow, Strings.Error_PlayStationFormat),
                // The saved NPSSO may have been dropped: the field to paste a new one.
                AfterFetch = UpdatePlayStationAccount,
                Describe = h => Localization.Format(Strings.Status_PlayStationHistory, h.Games.Count,
                    h.Games.Count(g => g.Status == PlayStatus.Mastered), h.FetchedAt.ToLocalTime())
            },
            new SyncedPlatform
            {
                Platform = GogClient.PlatformName,
                Title = Strings.Common_GogHistory,
                LogContext = "GOG history",
                FetchingStatus = Strings.Status_FetchingGog,
                NoHistoryStatus = Strings.Status_NoGogHistory,
                LoadCached = _gogSource.LoadCached,
                // A public profile: its name is all it takes (the one being added, or the saved one).
                Fetch = _ => _gogSource.RefreshAsync(new GogClient(_http), _pendingGogUser ?? _saver.Settings.GogUser!),
                ErrorMessage = ex => NoticeCenter.ServiceErrorMessage(
                    ex, Strings.Error_GogUnreachable, Strings.Error_GogSlow),
                // Only the games GOG Galaxy followed have a play time.
                Describe = h => Localization.Format(Strings.Status_GogHistory, h.Games.Count,
                    h.Games.Count(g => g.PlaytimeMinutes > 0), h.Games.Count(g => g.Status == PlayStatus.Mastered),
                    h.FetchedAt.ToLocalTime())
            },
            new SyncedPlatform
            {
                Platform = EaClient.PlatformName,
                Title = Strings.Common_EaHistory,
                LogContext = "EA history",
                FetchingStatus = Strings.Status_FetchingEa,
                NoHistoryStatus = Strings.Status_NoEaHistory,
                LoadCached = _eaSource.LoadCached,
                // ~150 requests (5 games each): their count on the status line.
                Fetch = async platform =>
                {
                    string token = _eaToken!;
                    try
                    {
                        return await _eaSource.RefreshAsync(new EaClient(_http, token),
                            new Progress<(int Done, int Total)>(p =>
                                platform.Status = Localization.Format(Strings.Status_FetchingEaGames, p.Done, p.Total)));
                    }
                    catch (EaException ex) when (ex.RequiresSignIn)
                    {
                        // Expired (4 h): forgotten, the card asks for EA's page again. Only
                        // this one: a token pasted meanwhile is the next sync's.
                        if (_eaToken == token)
                            _eaToken = null;
                        throw;
                    }
                },
                ErrorMessage = ex => NoticeCenter.ServiceErrorMessage(
                    ex, Strings.Error_EaUnreachable, Strings.Error_EaSlow),
                AfterFetch = UpdateEaAccount,
                Describe = h => Localization.Format(Strings.Status_EaHistory, h.Games.Count,
                    h.Games.Count(g => g.Status == PlayStatus.Mastered), h.FetchedAt.ToLocalTime())
            },
            new SyncedPlatform
            {
                Platform = EpicClient.PlatformName,
                Title = Strings.Common_EpicHistory,
                LogContext = "Epic history",
                FetchingStatus = Strings.Status_FetchingEpic,
                NoHistoryStatus = Strings.Status_NoEpicHistory,
                LoadCached = _epicSource.LoadCached,
                Fetch = _ => _epicSource.RefreshAsync(new EpicClient(_http, _epicAccount)),
                ErrorMessage = ex => NoticeCenter.ServiceErrorMessage(
                    ex, Strings.Error_EpicUnreachable, Strings.Error_EpicSlow),
                // A session Epic refused is dropped: the card asks for a new code.
                AfterFetch = UpdateEpicAccount,
                Describe = h => Localization.Format(Strings.Status_EpicHistory, h.Games.Count,
                    h.Games.Count(g => g.Status == PlayStatus.Mastered), h.FetchedAt.ToLocalTime())
            });
    }
}
