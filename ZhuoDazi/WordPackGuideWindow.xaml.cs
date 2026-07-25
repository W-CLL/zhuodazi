using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ZhuoDazi;

public partial class WordPackGuideWindow : Window
{
    private static readonly string TemplateDirectory = Path.Combine(
        AppContext.BaseDirectory, "resources", "templates");
    private static readonly string TemplatePath = Path.Combine(TemplateDirectory, "互动词包模板.json");
    private static readonly string GuidePath = Path.Combine(TemplateDirectory, "互动词包AI生成说明.txt");

    public WordPackGuideWindow()
    {
        InitializeComponent();
        AiPromptText.Text = File.Exists(GuidePath)
            ? File.ReadAllText(GuidePath)
            : "模板说明文件不存在，请重新安装桌搭子。";
    }

    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        WpfClipboard.SetText(AiPromptText.Text);
        GuideStatusText.Text = "AI 提示词已复制";
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(TemplatePath))
        {
            WpfMessageBox.Show(this, "模板文件不存在，请重新安装桌搭子。", "保存模板失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new WpfSaveFileDialog
        {
            Title = "保存互动词包模板",
            Filter = "JSON 词包 (*.json)|*.json",
            FileName = "桌搭子互动词包模板.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true) return;
        File.Copy(TemplatePath, dialog.FileName, overwrite: true);
        GuideStatusText.Text = "JSON 模板已保存";
    }

    private void OpenTemplateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(TemplateDirectory))
        {
            WpfMessageBox.Show(this, "模板目录不存在，请重新安装桌搭子。", "打开目录失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = TemplateDirectory, UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
