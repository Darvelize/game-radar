using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GameRadar.Core.Xbox;

/// <summary>
/// Tiny local web server receiving the end of the browser sign-in: Microsoft
/// redirects to http://localhost:port/?code=...&amp;state=... ("loopback
/// redirect", the recommended way for desktop apps in RFC 8252). Microsoft
/// ignores the port for localhost, so any free one works.
/// </summary>
internal sealed class LoopbackListener : IDisposable
{
    private readonly HttpListener _listener;

    private LoopbackListener(HttpListener listener, string redirectUri)
    {
        _listener = listener;
        RedirectUri = redirectUri;
    }

    public string RedirectUri { get; }

    /// <summary>Listens on a free port of this machine only (localhost).</summary>
    public static LoopbackListener Start()
    {
        // The port found free may be taken in between by another program:
        // a few attempts make that race harmless.
        for (int attempt = 1; ; attempt++)
        {
            string redirectUri = $"http://localhost:{FreePort()}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            try
            {
                listener.Start();
                return new LoopbackListener(listener, redirectUri);
            }
            catch (HttpListenerException ex)
            {
                listener.Close();
                // Every attempt refused (a port rule of Windows...): explained.
                if (attempt == 3)
                    throw new XboxLiveException(CoreText.Format(CoreStrings.Xbox_LoopbackFailed, ex.Message));
            }
        }
    }

    /// <summary>
    /// Waits for the browser to come back, answers it with a page telling the
    /// user to close the tab, and returns the sign-in code.
    /// </summary>
    /// <param name="expectedState">
    /// Random value sent with the sign-in request: an answer carrying another
    /// one was not started by us (anti-forgery check).
    /// </param>
    public async Task<string> WaitForCodeAsync(string expectedState, CancellationToken ct)
    {
        // GetContextAsync takes no CancellationToken: stopping the listener
        // is what interrupts it.
        using var registration = ct.Register(() => _listener.Stop());

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ct.IsCancellationRequested
                && ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                throw new OperationCanceledException(ct);
            }

            var query = context.Request.QueryString;
            string? code = query["code"];
            string? error = query["error"];

            // Anything else (favicon.ico...): not the answer we wait for.
            if (code is null && error is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                continue;
            }

            // Not our state (anti-forgery check): not our sign-in. Ignored, and
            // the real answer still awaited: another local program, or a page
            // probing localhost, must not end the user's sign-in.
            bool genuine = query["state"] == expectedState;
            await RespondAsync(context.Response, success: genuine && code is not null).ConfigureAwait(false);
            if (!genuine)
                continue;

            if (code is null)
                throw new XboxLiveException(error switch
                {
                    "access_denied" => CoreStrings.Xbox_AccessDenied,
                    XboxLiveAuth.AppBlockedError => CoreStrings.Xbox_AppBlocked,
                    _ => CoreText.Format(CoreStrings.Xbox_SignInFailed, query["error_description"] ?? error)
                });
            return code;
        }
    }

    private static async Task RespondAsync(HttpListenerResponse response, bool success)
    {
        string message = success
            ? CoreStrings.Xbox_BrowserSuccess
            : CoreStrings.Xbox_BrowserFailure;
        byte[] page = Encoding.UTF8.GetBytes(
            $"<!DOCTYPE html><html lang=\"{System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}\"><head><meta charset=\"utf-8\"><title>GameRadar</title></head>"
            + "<body style=\"font-family:'Segoe UI',sans-serif;display:grid;place-items:center;height:90vh;margin:0\">"
            + $"<h2 style=\"font-weight:600\">{message}</h2></body></html>");

        // A courtesy only: the browser may close the tab before it is written,
        // and the code it brought must not be lost for that.
        try
        {
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = page.Length;
            await response.OutputStream.WriteAsync(page).ConfigureAwait(false);
            response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    internal static int FreePort()
    {
        // Port 0 = "any free port", chosen by the operating system.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose() => _listener.Close();
}
