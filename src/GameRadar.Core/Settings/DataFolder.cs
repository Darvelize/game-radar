namespace GameRadar.Core.Settings;

/// <summary>
/// Where GameRadar keeps everything on this computer: the single entry
/// point for its folders (a future portable mode only has to change the
/// root). Settings and secrets sit in the root, the rest in subfolders.
/// </summary>
public sealed class DataFolder(string root)
{
    /// <summary>
    /// %LOCALAPPDATA%\GameRadar. Local, not Roaming: nothing here has to
    /// follow the user from PC to PC (caches can be downloaded again, secrets
    /// are encrypted for this Windows session anyway), and on a company
    /// network a Roaming folder is copied to the server at each session.
    /// </summary>
    public static DataFolder Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameRadar"));

    public string Root { get; } = root;

    /// <summary>
    /// The "erased" state shared by every store of this folder: one erasure
    /// ("Effacer toutes mes données") stops all their writes.
    /// </summary>
    public EraseGuard Guard { get; } = new();

    /// <summary>True when the data sit in a "Data" folder next to the exe (<see cref="Resolve"/>).</summary>
    public bool IsPortable { get; private init; }

    /// <summary>Name of the folder that switches on the portable mode.</summary>
    public const string PortableFolderName = "Data";

    // Created then deleted in the portable folder to know it can be written to.
    internal const string WriteProbeName = ".gameradar-write-test";

    /// <summary>
    /// Portable mode, only when the user chose it: a "Data" folder that
    /// ALREADY exists next to the exe holds everything instead of the default
    /// root. It is never created here (downloaded updates, "Program Files",
    /// synced folders: the user decides where it lives). A "Data" folder that
    /// cannot be written to is refused (<c>PortableRefused</c>): the default
    /// root is used and the caller says so. A "Data" that is a file is not a
    /// choice of the portable mode: ignored.
    /// </summary>
    public static (DataFolder Folder, bool PortableRefused) Resolve(string exeDirectory, string defaultRoot)
    {
        string portable = Path.Combine(exeDirectory, PortableFolderName);
        if (!Directory.Exists(portable))
            return (new DataFolder(defaultRoot), false);

        if (!CanWriteTo(portable))
            return (new DataFolder(defaultRoot), true);

        return (new DataFolder(portable) { IsPortable = true }, false);
    }

    private static bool CanWriteTo(string folder)
    {
        string probe = Path.Combine(folder, WriteProbeName);
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public string Cache => Path.Combine(Root, "cache");

    public string Images => Path.Combine(Root, "images");

    public string Logs => Path.Combine(Root, "logs");
}
