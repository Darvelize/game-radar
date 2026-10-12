using System.Text.Json;

namespace GameRadar.App.Tests;

/// <summary>The app tests run like the exe: no JSON by reflection (see GameRadar.Core.Tests).</summary>
public class JsonReflectionOffTests
{
    [Fact]
    public void ReflectionIsOffInTheTestHost()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
    }
}
