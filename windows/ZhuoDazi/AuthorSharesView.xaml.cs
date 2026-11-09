using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace ZhuoDazi;

public partial class AuthorSharesView : System.Windows.Controls.UserControl
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly bool _localPreview;
    private bool _loading;
    public AuthorSharesView(bool localPreview = false) { _localPreview = localPreview; InitializeComponent(); }
    private async void View_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private async Task LoadAsync()
    {
        if (_loading) return;
        if (_localPreview) { Status.Text = "暂无分享"; return; }
        _loading = true;
        Status.Text = "正在加载…";
        var selected = (Categories.SelectedItem as ShareCategory)?.Id;
        try
        {
            var response = await Client.GetFromJsonAsync<ShareResponse>("https://in.desktoppet.online/api/public/author-shares");
            var categories = response?.Categories ?? [];
            Categories.ItemsSource = categories;
            Categories.SelectedItem = categories.FirstOrDefault(category => category.Id == selected) ?? categories.FirstOrDefault();
            if (categories.Count == 0) { Links.ItemsSource = null; Status.Text = "暂无分享"; }
        }
        catch { Status.Text = "暂时无法加载，请稍后刷新。"; }
        finally { _loading = false; }
    }
    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        var items = (Categories.SelectedItem as ShareCategory)?.Items ?? [];
        Links.ItemsSource = items;
        Status.Text = items.Count == 0 ? "这个分类还没有分享" : string.Empty;
    }
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: ShareItem item } || !Uri.TryCreate(item.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(uri.UserInfo)) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { Status.Text = "无法打开浏览器，请稍后重试。"; }
    }
    public sealed record ShareResponse(List<ShareCategory> Categories);
    public sealed record ShareCategory(string Id, string Name, List<ShareItem> Items);
    public sealed record ShareItem(string Id, string Title, string Description, string Url);
}
