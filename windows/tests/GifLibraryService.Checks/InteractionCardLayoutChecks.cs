using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using ZhuoDazi.Controls;

internal static class InteractionCardLayoutChecks
{
    public static void Run()
    {
        foreach (var width in new[] { 184d, 250d, 320d, 430d })
        {
            CheckLayout(width);
        }
    }

    private static void CheckLayout(double width)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "assets", "PetWindow.xaml")).Root!;
        var content = new XElement(source.Element(presentation + "Grid")!);
        foreach (var attribute in content.Descendants().Attributes("Click").ToArray()) attribute.Remove();
        content.AddFirst(new XElement(presentation + "Grid.Resources", source.Element(presentation + "Window.Resources")!.Elements()));
        content.SetAttributeValue(XNamespace.Xmlns + "x", xaml.NamespaceName);
        var root = (Grid)XamlReader.Parse(content.ToString());
        var window = new Window
        {
            Width = width,
            Height = 320,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Content = root
        };
        try
        {
            var card = (Border)root.FindName("InteractionCard");
            var title = (TextBlock)root.FindName("InteractionTitle");
            var text = (TextBlock)root.FindName("InteractionMessage");
            var scroll = (ScrollViewer)root.FindName("InteractionMessageScroll");
            var choices = (WrapPanel)root.FindName("InteractionChoicePanel");
            var scenarios = new[]
            {
                (Name: "joke", Title: "冷笑话时间", Message: "开会为什么像看电影？", Choices: new[] { "看答案" }, Scrolls: false),
                (Name: "riddle", Title: "脑筋急转弯", Message: "一座小桥不通车，鼻梁上面安家？", Choices: new[] { "水", "眼球", "眼镜", "洋葱" }, Scrolls: false),
                (Name: "long", Title: "趣味知识", Message: string.Concat(Enumerable.Repeat("这是一段较长的互动题干，需要换行并且能够滚动查看完整内容。", 12)), Choices: new[] { "看答案" }, Scrolls: true),
                (Name: "answer", Title: "答案", Message: "都有人，但不一定有剧情。", Choices: new[] { "知道了" }, Scrolls: false)
            };
            foreach (var scenario in scenarios)
            {
                card.Visibility = Visibility.Collapsed;
                root.RowDefinitions[0].Height = new GridLength(120);
                root.Measure(new Size(width, 320));
                root.Arrange(new Rect(0, 0, width, 320));
                root.UpdateLayout();
                title.Text = scenario.Title;
                text.Text = scenario.Message;
                choices.Children.Clear();
                foreach (var label in scenario.Choices)
                    choices.Children.Add(new Button { Content = label, Style = (Style)root.FindResource("InteractionChoiceButton") });
                card.Visibility = Visibility.Visible;
                scroll.ScrollToHome();
                var maximumBubbleHeight = 340d;
                var measuredHeight = InteractionCardLayout.MeasureHeight(card, scroll, text, width, maximumBubbleHeight, 96);
                var bubbleHeight = Math.Clamp(measuredHeight, 220, maximumBubbleHeight);
                root.RowDefinitions[0].Height = new GridLength(bubbleHeight);
                var size = new Size(width, bubbleHeight + 200);
                root.Measure(size);
                root.Arrange(new Rect(size));
                root.UpdateLayout();
                var bounds = card.TransformToAncestor(root).TransformBounds(new Rect(card.RenderSize));
                Console.WriteLine($"Layout {width}/{scenario.Name}: card={bounds}, desired={card.DesiredSize}, text={text.RenderSize}, scroll={scroll.ViewportWidth}x{scroll.ViewportHeight}/{scroll.ExtentWidth}x{scroll.ExtentHeight}, row={bubbleHeight}");
                var availableWidth = Math.Min(card.MaxWidth, width - card.Margin.Left - card.Margin.Right);
                Require(Math.Abs(card.ActualWidth - availableWidth) <= 1,
                    $"Interaction card shrank to {card.ActualWidth} instead of its available width {availableWidth} for {scenario.Name}.");
                Require(bounds.Left >= 0 && bounds.Right <= width + 1, "Interaction card extends outside the pet window.");
                Require(text.ActualWidth <= scroll.ViewportWidth + 1, "Interaction text extends outside its viewport.");
                Require(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "Interaction text requires inaccessible horizontal scrolling.");
                var expectedText = new TextBlock
                {
                    Text = scenario.Message,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = text.FontFamily,
                    FontSize = text.FontSize,
                    LineHeight = text.LineHeight
                };
                expectedText.Measure(new Size(scroll.ViewportWidth, double.PositiveInfinity));
                Require(text.ActualHeight >= expectedText.DesiredSize.Height - 1, "Interaction prompt is clipped instead of wrapping.");
                var choiceBounds = choices.TransformToAncestor(root).TransformBounds(new Rect(choices.RenderSize));
                Require(choiceBounds.Bottom <= bubbleHeight + 1, $"Interaction choices are clipped by the bubble row: {choiceBounds} / {bubbleHeight} for {width}/{scenario.Name}.");
                if (scenario.Scrolls)
                {
                    Require(scroll.ScrollableHeight > 0, "Long interaction text cannot be scrolled.");
                    scroll.ScrollToEnd();
                    root.UpdateLayout();
                    Require(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1, "The end of the interaction prompt is unreachable.");
                    scroll.ScrollToHome();
                    root.UpdateLayout();
                }
                else
                {
                    Require(scroll.ScrollableHeight < 1, "Short interaction prompt should fit without scrolling.");
                }
                SavePreview(root, width, bubbleHeight, scenario.Name);
            }
            Console.WriteLine($"Interaction layout checks passed at width {width}: both reported prompts, long text, and answer transitions.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void SavePreview(Grid root, double width, double height, string name)
    {
        var directory = Environment.GetEnvironmentVariable("ZHUODAZI_INTERACTION_REPRO_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"interaction-{width}-{name}.png"));
        encoder.Save(file);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
