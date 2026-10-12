using System.Globalization;
using GameRadar.Core.Xbox;

namespace GameRadar.Core.Tests.Xbox;

public class CoreMessagesTests
{
    [Theory]
    [InlineData("fr-FR", "Ce compte Microsoft n'a pas de profil Xbox.", "Xbox Live a refusé l'accès (code 42).")]
    [InlineData("en-GB", "This Microsoft account has no Xbox profile.", "Xbox Live refused access (code 42).")]
    public void Messages_follow_the_language_of_the_app(string culture, string noProfile, string unknownCode)
    {
        // The Core reads the UI culture set by the App at startup.
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.StartsWith(noProfile, XboxLiveAuth.XstsRefusalMessage(2148916233, 401));
            Assert.Equal(unknownCode, XboxLiveAuth.XstsRefusalMessage(42, 401));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
