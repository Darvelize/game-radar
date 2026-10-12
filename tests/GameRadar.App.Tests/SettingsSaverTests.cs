using GameRadar.App.ViewModels;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

public sealed class SettingsSaverTests : AppTest
{
    [Fact]
    public async Task Quick_saves_queue_up_and_the_last_one_writes_the_latest_choices()
    {
        var settings = new AppSettings();
        var saver = new SettingsSaver(new SettingsStore(TempFolder), settings);

        // Fire and forget, as the view models do: nobody awaits them one by one.
        settings.MarkedCompleted.Add("Xbox:1");
        var first = saver.SaveAsync();
        settings.MarkedCompleted.Add("Xbox:2");
        var second = saver.SaveAsync();
        settings.MarkedCompleted.Add("Xbox:3");
        var third = saver.SaveAsync();

        await saver.WhenSavedAsync();
        await Task.WhenAll(first, second, third);

        var saved = new SettingsStore(TempFolder).Load();
        Assert.Equal(["Xbox:1", "Xbox:2", "Xbox:3"], saved.MarkedCompleted.Order());
    }

    [Fact]
    public async Task A_save_is_pending_from_its_start_until_it_is_written()
    {
        var saver = new SettingsSaver(new SettingsStore(TempFolder), new AppSettings());
        Assert.False(saver.IsSaving);

        var save = saver.SaveAsync();
        Assert.True(saver.IsSaving);

        await save;
        Assert.False(saver.IsSaving);
    }

    [Fact]
    public async Task A_failed_save_does_not_keep_the_lock()
    {
        // The settings folder is a file: creating it fails.
        Directory.CreateDirectory(TempFolder);
        string blocker = Path.Combine(TempFolder, "not-a-folder");
        await File.WriteAllTextAsync(blocker, "", TestContext.Current.CancellationToken);
        var saver = new SettingsSaver(new SettingsStore(blocker), new AppSettings());

        await Assert.ThrowsAnyAsync<IOException>(() => saver.SaveAsync());

        Assert.False(saver.IsSaving);
        // Would never complete if the failed save had kept the lock.
        await saver.WhenSavedAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
