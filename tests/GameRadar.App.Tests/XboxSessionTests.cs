using System.Net;
using System.Text;
using Avalonia.Headless.XUnit;
using GameRadar.App.Resources;
using GameRadar.Core.Settings;
using GameRadar.Core.Xbox;

namespace GameRadar.App.Tests;

/// <summary>
/// The Xbox account as the sync leaves it: a saved session Microsoft no longer
/// accepts is dropped, and the screens follow without a restart.
/// </summary>
public sealed class XboxSessionTests : AppTest
{
    [AvaloniaFact]
    public async Task A_dropped_session_shows_the_sign_in_again_after_a_sync()
    {
        new SecretStore(TempFolder).Save(Secret.XboxAccount, new SavedXboxAccount("dead-refresh-token", "RadarPlayer"), XboxJsonContext.Default.SavedXboxAccount);
        var vm = SampleData.Empty(TempFolder, network: new SessionRefused());
        Assert.True(vm.Accounts.XboxSync.IsConnected);

        await vm.Accounts.RefreshHistoryCommand.ExecuteAsync(null);

        // Microsoft refused the saved session: the account is signed out, and the
        // Accounts tab offers the sign-in again (instead of a stale "signed in").
        Assert.False(vm.Accounts.XboxSync.IsConnected);
        Assert.Equal(Strings.Account_NotSignedIn, vm.Accounts.AccountLabel);
    }

    /// <summary>Microsoft's token endpoint refuses every refresh token: "invalid_grant".</summary>
    private sealed class SessionRefused : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{ "error": "invalid_grant" }""", Encoding.UTF8, "application/json")
            });
    }
}
