using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class SettingsLayoutPreview
{
    // Render the actual layout with offline sample data, never constructing the app or loading a license.
    public static void RenderIfRequested()
    {
        var directory = Environment.GetEnvironmentVariable("ZHUODAZI_SETTINGS_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "assets", "SettingsWindow.xaml")).Root!;
        var app = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "assets", "App.xaml")).Root!;
        var events = new HashSet<string> { "Click", "Checked", "Unchecked", "SelectionChanged", "ValueChanged", "PreviewMouseWheel", "MouseDoubleClick", "KeyDown" };
        foreach (var width in new[] { 900d, 1000d })
        {
            var content = new XElement(source.Element(presentation + "Grid")!);
            foreach (var element in content.Descendants())
            {
                foreach (var attribute in element.Attributes().Where(a => events.Contains(a.Name.LocalName)).ToArray()) attribute.Remove();
                if (element.Name.LocalName == "AnimatedGifImage") element.Name = presentation + "Image";
            }
            content.AddFirst(new XElement(presentation + "Grid.Resources",
                app.Element(presentation + "Application.Resources")!.Elements(),
                source.Element(presentation + "Window.Resources")!.Elements()));
            content.SetAttributeValue(XNamespace.Xmlns + "x", xaml.NamespaceName);
            var root = (Grid)XamlReader.Parse(content.ToString());
            root.SetValue(TextElement.FontFamilyProperty, new FontFamily("Microsoft YaHei UI"));
            var tabs = (TabControl)root.FindName("MainTabs");
            var hall = (TabItem)root.FindName("HallTab");
            tabs.SelectedItem = hall;
            ((TextBox)root.FindName("HallNicknameText")).Text = "桌角的朋友";
            ((TextBlock)root.FindName("HallCountText")).Text = "4 人在线";
            ((CheckBox)root.FindName("CompanionHallEnabledCheck")).IsChecked = true;
            ((TextBlock)root.FindName("CompanionHallStatusText")).Text = "已加入大厅 · 昵称与在线状态对大厅用户可见";
            ((TextBlock)root.FindName("CompanionHallEmptyText")).Visibility = Visibility.Collapsed;
            var people = new[] { new PreviewPerson("小鱼同学"), new PreviewPerson("一颗快乐的土豆"), new PreviewPerson("今天准时下班"), new PreviewPerson("奶茶半糖") };
            var list = (ListBox)root.FindName("CompanionHallList");
            list.ItemsSource = people;
            list.SelectedIndex = 0;
            ((TextBlock)root.FindName("HallSendTargetText")).Text = "发送给 小鱼同学";
            ((TextBlock)root.FindName("HallPetNameText")).Text = "月薪喵 017 · 来自月薪喵图鉴";
            ((TextBox)root.FindName("CompanionHallMessageText")).Text = "工作间隙，给你送来一点好心情。";
            ((Image)root.FindName("HallPreviewImage")).Source = BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "partial-frame.gif")));
            var height = width == 900 ? 650d : 720d;
            Render(root, width, height, Path.Combine(directory, $"hall-{width}-top.png"));
            var scroll = (ScrollViewer)hall.Content;
            if (scroll.ExtentWidth > scroll.ViewportWidth + 1) throw new InvalidOperationException("Hall layout requires horizontal scrolling.");
            scroll.ScrollToEnd();
            Render(root, width, height, Path.Combine(directory, $"hall-{width}-send.png"));
            tabs.SelectedIndex = 1;
            ((TextBlock)root.FindName("QuietStatusText")).Text = "已暂停主动打扰，18:30 恢复";
            ((Button)root.FindName("PauseCompanionshipButton")).Content = "恢复主动陪伴";
            Render(root, width, height, Path.Combine(directory, $"quiet-{width}.png"));
        }
    }

    private static void Render(Grid root, double width, double height, string path)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    public sealed record PreviewPerson(string DisplayName)
    {
        public string Initial => DisplayName[..1];
        public string StatusText => "● 在线 · 点击选择";
    }
}
