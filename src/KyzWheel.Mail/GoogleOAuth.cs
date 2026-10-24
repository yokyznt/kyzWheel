using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit.Security;

namespace KyzWheel.Mail;

public sealed record GoogleSignIn(string Email, string Name, string RefreshToken);

public static class GoogleOAuth
{
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string Scope = "https://mail.google.com/ openid email profile";
    public const string ConsoleUrl = "https://console.cloud.google.com/auth/clients/create";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> Cache = new();

    public static string? ClientId { get; set; }
    public static string? ClientSecret { get; set; }
    public static bool Configured => !string.IsNullOrWhiteSpace(ClientId);

    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(48));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string Base64Url(byte[] b) => System.Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string AuthUrl(string clientId, string redirect, string challenge, string state, string? loginHint = null)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirect,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",
            ["prompt"] = "select_account consent",
            ["state"] = state,
        };
        if (loginHint is not null) q["login_hint"] = loginHint;
        return AuthEndpoint + "?" + string.Join('&', q.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));
    }

    public static (string Email, string Name) ParseIdToken(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2) throw new FormatException("id_token inválido");
        var p = parts[1].Replace('-', '+').Replace('_', '/');
        p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(System.Convert.FromBase64String(p));
        var root = doc.RootElement;
        var email = root.GetProperty("email").GetString() ?? throw new FormatException("Sin correo");
        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? email : email;
        return (email.ToLowerInvariant(), name);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private const string DoneHtml = "<!doctype html><meta charset=utf-8><title>kyzWheel</title><body style=\"font-family:Segoe UI,sans-serif;background:#0b0b10;color:#fff;display:grid;place-items:center;height:100vh;margin:0\"><div style=\"text-align:center\"><h2>Listo</h2><p style=\"opacity:.7\">Tu cuenta quedó vinculada a kyzWheel. Ya puedes cerrar esta pestaña.</p></div>";
    private const string FailHtml = "<!doctype html><meta charset=utf-8><title>kyzWheel</title><body style=\"font-family:Segoe UI,sans-serif;background:#0b0b10;color:#fff;display:grid;place-items:center;height:100vh;margin:0\"><div style=\"text-align:center\"><h2>No se vinculó</h2><p style=\"opacity:.7\">Vuelve a kyzWheel e inténtalo otra vez.</p></div>";

    public static async Task<GoogleSignIn> SignInAsync(CancellationToken ct, string? loginHint = null)
    {
        if (!Configured) throw new InvalidOperationException("Falta configurar el inicio de sesión con Google");
        var port = FreePort();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        var verifier = NewVerifier();
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        Process.Start(new ProcessStartInfo(AuthUrl(ClientId!, redirect, Challenge(verifier), state, loginHint)) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var reg = timeout.Token.Register(() => { try { listener.Stop(); } catch (ObjectDisposedException) { } });
        HttpListenerContext ctx;
        while (true)
        {
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                throw new OperationCanceledException("Se agotó el tiempo para iniciar sesión", e);
            }
            if (ctx.Request.Url?.AbsolutePath == "/") break;
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
        var query = System.Web.HttpUtility.ParseQueryString(ctx.Request.Url!.Query);
        var ok = query["state"] == state && query["code"] is { Length: > 0 };
        var html = Encoding.UTF8.GetBytes(ok ? DoneHtml : FailHtml);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = html.Length;
        await ctx.Response.OutputStream.WriteAsync(html, ct);
        ctx.Response.Close();
        if (!ok) throw new AuthenticationException(query["error"] == "access_denied" ? "Cancelaste el inicio de sesión" : "Google no devolvió la autorización");
        var form = new Dictionary<string, string>
        {
            ["code"] = query["code"]!,
            ["client_id"] = ClientId!,
            ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        };
        if (!string.IsNullOrEmpty(ClientSecret)) form["client_secret"] = ClientSecret;
        var json = await PostAsync(form, ct);
        var refresh = json.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(refresh)) throw new AuthenticationException("Google no entregó acceso permanente; quita kyzWheel de tu cuenta de Google y vuelve a intentar");
        var (email, name) = ParseIdToken(json.GetProperty("id_token").GetString()!);
        Remember(refresh, json);
        return new GoogleSignIn(email, name, refresh);
    }

    private static void Remember(string refresh, JsonElement json)
    {
        if (json.TryGetProperty("access_token", out var a) && a.GetString() is { } token)
            Cache[refresh] = (token, DateTimeOffset.UtcNow.AddSeconds((json.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600) - 120));
    }

    private static async Task<JsonElement> PostAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        if (!resp.IsSuccessStatusCode)
        {
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : resp.StatusCode.ToString();
            throw new AuthenticationException(err == "invalid_grant" ? "Google revocó el acceso; vuelve a vincular la cuenta" : "Google rechazó la sesión: " + err);
        }
        return doc.RootElement.Clone();
    }

    public static async Task<string> AccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        if (Cache.TryGetValue(refreshToken, out var c) && c.Expires > DateTimeOffset.UtcNow) return c.Token;
        if (!Configured) throw new AuthenticationException("Falta configurar el inicio de sesión con Google");
        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId!,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        };
        if (!string.IsNullOrEmpty(ClientSecret)) form["client_secret"] = ClientSecret;
        var json = await PostAsync(form, ct);
        Remember(refreshToken, json);
        return Cache[refreshToken].Token;
    }

    public static async Task<SaslMechanism> SaslAsync(string email, string refreshToken, CancellationToken ct)
        => new SaslMechanismOAuth2(email, await AccessTokenAsync(refreshToken, ct));
}
