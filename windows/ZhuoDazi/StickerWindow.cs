using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZhuoDazi;

public sealed class StickerWindow : Window
{
    private sealed record StickerDefinition(string Emoji, string Label);

    private static readonly IReadOnlyDictionary<string, StickerDefinition> Stickers =
        new Dictionary<string, StickerDefinition>
        {
            ["heart"] = new("❤️", "心心"),
            ["coffee"] = new("☕", "咖啡"),
            ["catPaw"] = new("🐾", "猫爪"),
            ["star"] = new("⭐", "星星"),
            ["cheer"] = new("💪", "加油"),
            ["goodnight"] = new("🌙", "晚安")
        };

    public StickerWindow(string stickerId, string senderName)
    {
        var sticker = Stickers.TryGetValue(stickerId, out var value)
            ? value
            : new StickerDefinition("💌", "贴纸");
        Title = $"{senderName} 发来的{sticker.Label}";
        Width = 96;
        Height = 96;
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
            CornerRadius = new CornerRadius(20),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(238, 255, 250, 244)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 236, 208, 190)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.22,
                Color = System.Windows.Media.Colors.Black
            },
            Child = new StackPanel
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = sticker.Emoji,
                        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                        FontSize = 42,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = senderName,
                        FontSize = 10,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 83, 72)),
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 78
                    }
                }
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
