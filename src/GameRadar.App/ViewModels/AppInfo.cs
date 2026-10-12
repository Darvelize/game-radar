namespace GameRadar.App.ViewModels;

/// <summary>What the view models know about the running app.</summary>
public static class AppInfo
{
    /// <summary>Version of the running app, from &lt;Version&gt; in the .csproj.</summary>
    public static Version Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
}
