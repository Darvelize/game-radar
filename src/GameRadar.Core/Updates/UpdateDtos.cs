using System.Text.Json.Serialization;

namespace GameRadar.Core.Updates;

// Shape of api.github.com/repos/{owner}/{repo}/releases/latest (only the
// field we read). Internal: a GitHub API change only touches this folder.

internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }
}
