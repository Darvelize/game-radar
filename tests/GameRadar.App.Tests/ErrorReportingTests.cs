using Avalonia.Headless.XUnit;

namespace GameRadar.App.Tests;

public sealed class ErrorReportingTests : AppTest
{
    private string LogsFolder => Path.Combine(TempFolder, "logs");

    [AvaloniaFact]
    public void An_unexpected_error_is_logged_and_pointed_to()
    {
        var vm = SampleData.Empty(TempFolder);

        vm.ReportUnexpectedError("UI", new InvalidOperationException("binding cassé"));

        Assert.Contains("journal des erreurs", vm.Notices.Message);
        string log = File.ReadAllText(Assert.Single(Directory.GetFiles(LogsFolder, "*.log")));
        Assert.Contains("UI · Unexpected error", log);
        Assert.Contains("System.InvalidOperationException: binding cassé", log);
    }

    [AvaloniaFact]
    public void The_logs_button_opens_the_folder_even_before_any_error()
    {
        Uri? opened = null;
        var vm = SampleData.Empty(TempFolder, uri => opened = uri);

        vm.OpenLogsFolderCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.True(opened.IsFile);
        Assert.Equal(Path.GetFullPath(LogsFolder), Path.GetFullPath(opened.LocalPath).TrimEnd(Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(LogsFolder));
    }
}
