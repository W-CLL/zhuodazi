using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ZhuoDazi.Services;

namespace ZhuoDazi;

public partial class AuthorContactWindow : Window
{
    internal AuthorContactWindow(RemoteConfig config)
    {
        InitializeComponent();
        WechatIdText.Text = config.WechatId;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            using var stream = config.ActivationQrCodeDataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)
                && config.ActivationQrCodeDataUrl.Length <= 350_000
                ? new MemoryStream(Convert.FromBase64String(config.ActivationQrCodeDataUrl[22..])) : null;
            if (stream is not null) image.StreamSource = stream;
            else image.UriSource = new Uri("pack://application:,,,/ZhuoDazi;component/assets/contact-author-wechat.png");
            image.DecodePixelWidth = 440; image.EndInit(); image.Freeze(); ContactQrImage.Source = image;
        }
        catch { ContactQrImage.Source = new BitmapImage(new Uri("pack://application:,,,/ZhuoDazi;component/assets/contact-author-wechat.png")); }
    }
    private void CopyWechat_Click(object sender, RoutedEventArgs e)
        => System.Windows.Clipboard.SetText(WechatIdText.Text);
}
