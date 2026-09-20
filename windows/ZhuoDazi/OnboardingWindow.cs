using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace ZhuoDazi;

/// <summary>A persistent, keyboard-accessible guide; no timed text or auto dismissal.</summary>
public sealed class OnboardingWindow : Window
{
    private readonly AppController _controller;
    private readonly TextBlock _eyebrow = Text(12, "#167D6C");
    private readonly TextBlock _heading = Text(25, "#183B35");
    private readonly TextBlock _body = Text(15, "#425F59");
    private readonly TextBlock _status = Text(13, "#167D6C");
    private readonly WrapPanel _actions = new() { Margin = new Thickness(0, 18, 0, 0) };
    private static readonly Style ActionStyle = (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#183B35"/>
          <Setter Property="BorderBrush" Value="#CDDDD6"/><Setter Property="FontSize" Value="14"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="7" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate></Setter.Value></Setter>
          <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter Property="Opacity" Value="0.82"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter Property="Opacity" Value="0.64"/></Trigger>
          </Style.Triggers>
        </Style>
        """);

    public OnboardingWindow(AppController controller)
    {
        _controller = controller;
        Title = "认识桌搭子 · 使用引导";
        Width = 490;
        Height = 510;
        MinWidth = 360;
        MinHeight = 350;
        MaxHeight = Math.Max(350, SystemParameters.WorkArea.Height - 32);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(246, 250, 247));
        var panel = new StackPanel { Margin = new Thickness(28) };
        _heading.Margin = new Thickness(0, 12, 0, 18);
        _heading.FontWeight = FontWeights.SemiBold;
        _body.LineHeight = 26;
        _status.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(_eyebrow);
        panel.Children.Add(_heading);
        panel.Children.Add(_body);
        panel.Children.Add(_status);
        panel.Children.Add(_actions);
        panel.Children.Add(new TextBlock
        {
            Text = "随时可在陪伴设置 → 陪伴日常中继续或重看。",
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(91, 115, 108)),
            Margin = new Thickness(0, 18, 0, 0)
        });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Close(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Refresh();
    }

    public void Refresh(string? status = null)
    {
        var step = _controller.Settings.Onboarding.Step;
        _eyebrow.Text = step is >= 1 and <= 4 ? $"初次体验  ·  {step} / 4" : "桌搭子 3.3.0  ·  一分钟体验";
        (_heading.Text, _body.Text) = step switch
        {
            1 => ("先认识这只小搭子", "拖动桌宠可以换位置，双击打开设置，右键查看玩法和退出入口。\n\n你可以先试着拖一拖，再点下一步。"),
            2 => ("让它回应你", "点一下按钮，回应桌宠的小问候。\n\n断网也能玩。"),
            3 => ("看一段小剧场", "两只小搭子会演一段约 12 秒的对话。\n\n不用邀请好友，点一下就能看。"),
            4 => ("安静陪伴，也能随时找回", "需要专心时：右键 → 暂停主动打扰 1 小时；提醒和手动操作仍可使用。\n\n隐藏后：托盘图标 → 显示桌宠。鼠标穿透后：按 Ctrl+Shift+P 恢复触摸。\n\n完全退出：右键或托盘菜单 → 退出桌搭子。"),
            >= 5 => ("现在，一起玩吧", "你已经走完使用引导。可以继续互动、看小剧场，或去大厅认识在线的小搭子。\n\n体验期也能去大厅；正式激活后还可绑定私人搭子。进入大厅不会自动加入或发送。"),
            _ => ("花一分钟，认识你的桌搭子", _controller.GuideIsUpgradeNotice
                ? "3.3.0 加入了可以亲手体验的使用引导：互动一次、看段短剧，再学会暂停和找回。\n\n已有设置会保留，你可以现在体验，也可以以后从设置进入。"
                : "我已经到你的屏幕角落啦。花一分钟，一起玩点什么？\n\n试一次互动，看一小段剧场，再学会怎样暂停和找回我。每一步都可以跳过。")
        };
        _status.Text = status ?? string.Empty;
        _actions.Children.Clear();
        if (step >= 5)
        {
            Add("再互动一下", () => { Close(); _controller.StartRandomInteraction(); }, true);
            Add("去看小剧场", () => { Close(); _controller.StartTheater(); });
            Add("看看桌宠大厅", () => { Close(); _controller.ShowHall(); });
            Add("完成", Close);
            return;
        }
        if (_controller.GuideBusy)
        {
            Add(step == 3 ? "结束演示" : "收起演示", _controller.StopGuideDemo);
        }
        else
        {
            var label = step switch { 0 => "带我体验一下", 1 => "下一步", 2 => "让它回应我", 3 => "看一小段", _ => "知道了" };
            Add(label, () => _ = _controller.PerformGuideActionAsync(), true);
        }
        if (step > 0) Add("跳过这一步", () => _controller.AdvanceGuide(true));
        Add(step == 0 ? "先自己玩" : "稍后体验", Close);
        if (step == 4) Add("打开陪伴设置", () => { _controller.ShowInteractionSettings(); });
    }

    private void Add(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, Style = ActionStyle, Margin = new Thickness(0, 0, 8, 10), Padding = new Thickness(15, 9, 15, 9), MinHeight = 40 };
        if (primary)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(22, 125, 108));
            button.Foreground = Brushes.White;
            button.FontWeight = FontWeights.SemiBold;
        }
        button.Click += (_, _) => action();
        _actions.Children.Add(button);
    }

    private static TextBlock Text(double size, string color) => new()
    {
        FontSize = size, TextWrapping = TextWrapping.Wrap,
        Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!
    };
}
