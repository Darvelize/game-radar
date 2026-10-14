using System.Net;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GameRadar.App.ViewModels;
using GameRadar.App.Views;
using GameRadar.Core.Cache;
using GameRadar.Core.GamePass;
using GameRadar.Core.Logging;
using GameRadar.Core.Epic;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;
using GameRadar.Core.Xbox;

namespace GameRadar.App;

public partial class App : Application
{
    // One HttpClient for the whole app lifetime, shared by every connector.
    // Creating one per call can exhaust network sockets.
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        // Asks servers for gzip/brotli: the Game Pass catalog drops
        // from ~47 MB to ~11 MB.
        AutomaticDecompression = DecompressionMethods.All,
        // Recycles connections periodically so DNS changes are picked up
        // (the classic pitfall of a long-lived HttpClient).
        PooledConnectionLifetime = TimeSpan.FromMinutes(15)
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Every file of GameRadar lives under one folder: the usual one, or
            // the "Data" folder the user put next to the exe (portable mode).
            // ProcessPath, not AppContext.BaseDirectory: a single-file exe can
            // report the folder where it extracted its libraries.
            string? exeFolder = Path.GetDirectoryName(Environment.ProcessPath);
            var (data, portableRefused) = exeFolder is null
                ? (DataFolder.Default, false)
                : DataFolder.Resolve(exeFolder, DataFolder.Default.Root);
            // Every store shares data.Guard: one "Effacer toutes mes données" stops all their writes.
            var log = new ErrorLog(data.Logs, AppInfo.Version.ToString(3), guard: data.Guard);
            // At every start, not only at the next error: an old log never lingers.
            log.DeleteOldLogs();

            // Before anything is read: 1.0.0 and 1.1.0 kept settings and
            // caches in %APPDATA% (Roaming). A file that cannot move stays
            // there and is tried again at the next launch. Not in portable
            // mode: the user chose to start from a folder of their own.
            if (!data.IsPortable)
            {
                foreach (var (file, error) in DataFolderMigration.MoveLegacyData(DataFolderMigration.LegacyRoot, data))
                    log.Write("Data", $"Could not move {file} to the new data folder", error);
            }

            var secrets = new SecretStore(data.Root, data.Guard);
            var settings = new SettingsStore(data.Root, data.Guard);

            // Before any view is built: every text is read once, in this language.
            Localization.Apply(settings.Load().Language);

            var cache = new CacheStore(data.Cache, data.Guard);
            // Caches of a former format (a version suffix that went up) are
            // useless: removed now, before any source reads its own.
            CacheCleanup.RemoveObsolete(cache);
            var images = new ImageFileCache(Http, data.Images, guard: data.Guard);
            // Pictures of games no longer shown: the folder would only grow.
            images.RemoveUnused();
            var viewModel = new MainWindowViewModel(
                Http,
                settings,
                cache,
                images,
                new XboxAccount(Http, secrets),
                new SteamAccount(secrets),
                new PlayStationAccount(secrets),
                new EpicAccount(Http, secrets),
                // The window's launcher: web page, folder or file (LinkLauncher).
                uri =>
                {
                    if (desktop.MainWindow is { } window)
                        _ = LinkLauncher.OpenAsync(window.Launcher, uri);
                },
                new LocalDataEraser(settings, cache, secrets, images, log, data),
                () => desktop.Shutdown(),
                log,
                // New language: start a new GameRadar, then close this one.
                () =>
                {
                    System.Diagnostics.Process.Start(Environment.ProcessPath!);
                    desktop.Shutdown();
                },
                data);

            CatchUnexpectedErrors(log, viewModel);

            // A "Data" folder that cannot be written to: said once, in the banner and the log.
            if (portableRefused)
                viewModel.Notices.Report("Data", Localization.Format(Strings.Data_PortableNotWritable));

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Fire-and-forget on purpose: the window opens right away and the
            // lists fill in when the caches are read. InitializeAsync catches
            // its own errors, so nothing is lost.
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The last safety net: errors no try/catch expected are written to the
    /// log instead of vanishing (until now, the window just disappeared).
    /// </summary>
    private static void CatchUnexpectedErrors(ErrorLog log, MainWindowViewModel viewModel)
    {
        // On the UI thread (a click, a binding...): logged, then marked as
        // handled so the app keeps running, with a banner pointing to the log.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            viewModel.ReportUnexpectedError("UI", e.Exception);
            e.Handled = true;
        };

        // A background task that failed and that nobody awaited: logged
        // when the garbage collector finds it, without stopping the app.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Write("Background task", "Unexpected error", e.Exception);
            e.SetObserved();
        };

        // Any other thread: the process is going down, at least the cause
        // is on disk (Write is synchronous).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Write("Crash", "Fatal error", e.ExceptionObject as Exception);
    }
}
