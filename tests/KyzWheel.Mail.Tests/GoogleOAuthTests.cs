using System.Text;
using KyzWheel.Mail;

namespace KyzWheel.Mail.Tests;

public sealed class GoogleOAuthTests
{
    [Fact]
    public void Pkce_challenge_matches_rfc7636_example()
        => Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", GoogleOAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Verifier_is_url_safe_and_long_enough()
    {
        var v = GoogleOAuth.NewVerifier();
        Assert.InRange(v.Length, 43, 128);
        Assert.DoesNotContain('+', v);
        Assert.DoesNotContain('/', v);
        Assert.DoesNotContain('=', v);
    }

    [Fact]
    public void Auth_url_has_offline_pkce_and_account_chooser()
    {
        var u = new Uri(GoogleOAuth.AuthUrl("cid.apps.googleusercontent.com", "http://127.0.0.1:5123/", "chal", "st"));
        var q = System.Web.HttpUtility.ParseQueryString(u.Query);
        Assert.Equal("accounts.google.com", u.Host);
        Assert.Equal("cid.apps.googleusercontent.com", q["client_id"]);
        Assert.Equal("http://127.0.0.1:5123/", q["redirect_uri"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Contains("select_account", q["prompt"]);
        Assert.Contains("https://mail.google.com/", q["scope"]);
        Assert.Equal("st", q["state"]);
    }

    [Fact]
    public void Id_token_gives_email_and_name()
    {
        var payload = GoogleOAuth.Base64Url(Encoding.UTF8.GetBytes("{\"email\":\"Yokis@Gmail.com\",\"name\":\"Yokis Ñ\"}"));
        var (email, name) = GoogleOAuth.ParseIdToken($"h.{payload}.s");
        Assert.Equal("yokis@gmail.com", email);
        Assert.Equal("Yokis Ñ", name);
    }

    [Fact]
    public void Needs_both_id_and_secret()
    {
        GoogleOAuth.ClientId = "x.apps.googleusercontent.com";
        GoogleOAuth.ClientSecret = null;
        Assert.False(GoogleOAuth.Configured);
        GoogleOAuth.ClientSecret = "s";
        Assert.True(GoogleOAuth.Configured);
        GoogleOAuth.ClientId = null;
        GoogleOAuth.ClientSecret = null;
    }

    [Fact]
    public async Task Refresh_without_client_fails_clearly()
    {
        GoogleOAuth.ClientId = null;
        var e = await Assert.ThrowsAsync<MailKit.Security.AuthenticationException>(() => GoogleOAuth.AccessTokenAsync("r", CancellationToken.None));
        Assert.Contains("Google", e.Message);
    }
}
