using System.Text.Json;

namespace GameRadar.Core.Tests;

/// <summary>
/// The test host runs like the exe: no JSON by reflection. Without it, a type
/// forgotten in a generated context would pass the tests and fail only in the
/// trimmed exe.
/// </summary>
public class JsonReflectionOffTests
{
    [Fact]
    public void ReflectionIsOffInTheTestHost()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
    }

    [Fact]
    public void ATypeOutsideEveryContextFailsInsteadOfWorkingByReflection()
    {
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(new Unregistered("x")));
    }

    private sealed record Unregistered(string Name);
}
