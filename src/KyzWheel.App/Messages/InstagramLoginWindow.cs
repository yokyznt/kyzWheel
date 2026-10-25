using KyzWheel.App.Shell;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace KyzWheel.App.Messages;

public sealed class InstagramLoginWindow : GlassWindow
{
    private readonly WebView2 _web = new();
    private bool _done;

    public event Action<Dictionary<string, string>>? LoggedIn;

    public InstagramLoginWindow() : base("Entrar a Instagram", 480, 720)
    {
        var g = new Grid { Margin = new Microsoft.UI.Xaml.Thickness(0, 32, 0, 0) };
        g.Children.Add(_web);
        Surface.Children.Add(g);
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            var dir = Path.Combine(Log.Dir, "webview-instagram");
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, dir, new CoreWebView2EnvironmentOptions());
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.NavigationCompleted += async (_, _) => await CheckAsync();
            _web.CoreWebView2.SourceChanged += async (_, _) => await CheckAsync();
            _web.CoreWebView2.Navigate("https://www.instagram.com/accounts/login/?next=%2Fdirect%2Finbox%2F");
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    private async Task CheckAsync()
    {
        if (_done) return;
        var list = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.instagram.com");
        var c = list.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.First().Value);
        if (!c.TryGetValue("sessionid", out var sid) || sid.Length == 0 || !c.ContainsKey("ds_user_id")) return;
        _done = true;
        var keep = new[] { "sessionid", "csrftoken", "ds_user_id", "mid", "ig_did", "rur", "datr" };
        LoggedIn?.Invoke(c.Where(kv => keep.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
        Close();
    }
}
