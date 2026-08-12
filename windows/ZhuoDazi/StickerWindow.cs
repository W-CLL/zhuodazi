using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZhuoDazi;

public sealed class StickerWindow : Window
{
    private sealed record StickerDefinition(string Asset, string Label, System.Windows.Media.Color Color);

    private static readonly IReadOnlyDictionary<string, StickerDefinition> Stickers =
        new Dictionary<string, StickerDefinition>
        {
            ["heart"] = new("heart", "心心", System.Windows.Media.Color.FromRgb(232, 93, 112)),
            ["coffee"] = new("coffee", "咖啡", System.Windows.Media.Color.FromRgb(171, 111, 72)),
            ["catPaw"] = new("catPaw", "猫爪", System.Windows.Media.Color.FromRgb(196, 126, 178)),
            ["star"] = new("star", "星星", System.Windows.Media.Color.FromRgb(231, 173, 48)),
            ["cheer"] = new("cheer", "加油", System.Windows.Media.Color.FromRgb(74, 155, 187)),
            ["goodnight"] = new("goodnight", "晚安", System.Windows.Media.Color.FromRgb(106, 117, 190))
        };

    public StickerWindow(string stickerId, string senderName)
    {
        var sticker = Stickers.TryGetValue(stickerId, out var value)
            ? value
            : new StickerDefinition("heart", "贴纸", System.Windows.Media.Color.FromRgb(232, 93, 112));
        Title = $"{senderName} 发来的{sticker.Label}";
        Width = 64;
        Height = 64;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ToolTip = $"{senderName} 发来的{sticker.Label}，点击收起";

        var border = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(246, 255, 250, 244)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(sticker.Color),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 2,
                Opacity = 0.18,
                Color = System.Windows.Media.Colors.Black
            },
            Child = new System.Windows.Controls.Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri($"pack://application:,,,/assets/stickers/{sticker.Asset}.png")),
                Width = 38,
                Height = 38,
                Stretch = System.Windows.Media.Stretch.Uniform
            }
        };
        Content = border;
        MouseLeftButtonUp += OnClick;
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        Close();
        e.Handled = true;
    }
}
