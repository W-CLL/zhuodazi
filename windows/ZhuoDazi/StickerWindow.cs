using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZhuoDazi;

public sealed class StickerWindow : Window
{
    private static readonly IReadOnlyDictionary<string, (string Emoji, string Label)> Stickers =
        new Dictionary<string, (string Emoji, string Label)>
        {
            ["heart"] = ("❤️", "心心"),
            ["coffee"] = ("☕", "咖啡"),
            ["catPaw"] = ("🐾", "猫爪"),
            ["star"] = ("⭐", "星星"),
            ["cheer"] = ("💪", "加油"),
            ["goodnight"] = ("🌙", "晚安")
        };

    public StickerWindow(string stickerId, string senderName)
    {
        var sticker = Stickers.TryGetValue(stickerId, out var value) ? value : ("💌", "贴纸");
        Title = $"{senderName} 发来的{sticker.Label}";
        Width = 96;
        Height = 96;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ToolTip = $"{senderName} 发来的{sticker.Label}，点击收起";

        var border = new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromArgb(238, 255, 250, 244)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 236, 208, 190)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.22,
                Color = Colors.Black
            },
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = sticker.Emoji,
                        FontFamily = new FontFamily("Segoe UI Emoji"),
                        FontSize = 42,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = senderName,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(104, 83, 72)),
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
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
