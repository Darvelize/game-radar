using System.Text.Json;
using GameRadar.Core.EA;
using GameRadar.Core.Epic;

namespace GameRadar.Core.Tests.Services;

/// <summary>
/// EA and Epic each keep their GraphQL envelope (a connector's formats stay in its
/// folder): the bodies they send are pinned here.
/// </summary>
public class GraphQlRequestTests
{
    [Fact]
    public void TheBodiesSent_ArePinned()
    {
        // EA: a query without variables writes null; Epic always has some.
        Assert.Equal("""{"query":"q","variables":null}""",
            JsonSerializer.Serialize(new GameRadar.Core.EA.GraphQlRequest<NoVariables> { Query = "q" }, EaJsonContext.Default.GraphQlRequestNoVariables));
        Assert.Equal("""{"query":"q","variables":{"gameSlugs":["a"]}}""",
            JsonSerializer.Serialize(new GameRadar.Core.EA.GraphQlRequest<GameSlugsVariables> { Query = "q", Variables = new() { GameSlugs = ["a"] } }, EaJsonContext.Default.GraphQlRequestGameSlugsVariables));
        Assert.Equal("""{"query":"q","variables":{"sandboxId":"s","locale":"en-US"}}""",
            JsonSerializer.Serialize(new GameRadar.Core.Epic.GraphQlRequest<SandboxVariables> { Query = "q", Variables = new() { SandboxId = "s" } }, EpicJsonContext.Default.GraphQlRequestSandboxVariables));
    }
}
