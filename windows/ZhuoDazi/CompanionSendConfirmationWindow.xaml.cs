using System.Windows;

namespace ZhuoDazi;

public partial class CompanionSendConfirmationWindow : Window
{
    public CompanionSendConfirmationWindow(bool? online)
    {
        InitializeComponent();
        PromptText.Text = online == false
            ? "对方当前不在线，是否继续发送？"
            : "当前无法确认对方在线状态，是否继续发送？";
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
