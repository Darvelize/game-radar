using System.Text.Json;
using GameRadar.App.Resources;
using GameRadar.App.ViewModels;
using GameRadar.Core.Logging;

namespace GameRadar.App.Tests;

public sealed class NoticeCenterTests : AppTest
{
    private string LogsFolder => Path.Combine(TempFolder, "logs");

    private NoticeCenter Center() => new(new ErrorLog(LogsFolder, "test"));

    private string LogText() => File.ReadAllText(Assert.Single(Directory.GetFiles(LogsFolder, "*.log")));

    [Fact]
    public void A_reported_error_is_shown_and_logged_with_its_exception()
    {
        var center = Center();

        center.Report("Steam store", "The store is down", new InvalidOperationException("boom"));

        Assert.Equal("The store is down", center.Message);
        Assert.True(center.HasMessage);
        string log = LogText();
        Assert.Contains("Steam store", log);
        Assert.Contains("The store is down", log);
        Assert.Contains("boom", log);
    }

    [Fact]
    public void An_unexpected_error_points_to_the_log()
    {
        var center = Center();

        center.ReportUnexpected("UI", new InvalidOperationException("nobody expected this"));

        Assert.Equal(Strings.Error_UnexpectedLogged, center.Message);
        Assert.Contains("nobody expected this", LogText());
    }

    [Fact]
    public void Dismissing_clears_the_message_and_tells_the_banner()
    {
        var center = Center();
        center.Report("Test", "Something went wrong");
        var changed = new List<string?>();
        center.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        center.Dismiss();

        Assert.Equal(string.Empty, center.Message);
        Assert.False(center.HasMessage);
        Assert.Contains(nameof(NoticeCenter.HasMessage), changed);
    }

    [Fact]
    public void The_service_message_goes_from_the_most_specific_to_the_most_generic()
    {
        string Message(Exception ex, string? badAnswer = null) =>
            NoticeCenter.ServiceErrorMessage<InvalidOperationException>(ex, "unreachable", "slow", badAnswer);

        Assert.Equal("own message", Message(new InvalidOperationException("own message")));
        Assert.Equal("unreachable", Message(new HttpRequestException()));
        Assert.Equal("slow", Message(new TaskCanceledException()));
        Assert.Equal("bad answer", Message(new JsonException(), badAnswer: "bad answer"));
        // Without a sentence for a bad answer, it is an unexpected error.
        Assert.Contains("unexpected", Message(new JsonException("unexpected")));
    }
}
