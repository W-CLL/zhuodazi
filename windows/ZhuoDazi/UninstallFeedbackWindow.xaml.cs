using System.ComponentModel;
using System.Windows;
using ZhuoDazi.Services;
using WpfRadioButton = System.Windows.Controls.RadioButton;

namespace ZhuoDazi;

internal enum UninstallFeedbackOutcome
{
    Cancel,
    Proceed
}

public partial class UninstallFeedbackWindow : Window
{
    private readonly Func<UninstallFeedbackChoice, CancellationToken, Task<UninstallIntentReceipt?>> _submit;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    private bool _proceeding;

    internal UninstallFeedbackOutcome Outcome { get; private set; } = UninstallFeedbackOutcome.Cancel;
    internal UninstallIntentReceipt? Receipt { get; private set; }

    internal UninstallFeedbackWindow(
        Func<UninstallFeedbackChoice, CancellationToken, Task<UninstallIntentReceipt?>> submit)
    {
        _submit = submit;
        InitializeComponent();
        ContentRendered += (_, _) => ConfirmButton.Focus();
    }

    private void Reason_Checked(object sender, RoutedEventArgs e)
    {
        if (CommentTextBox is null || sender is not WpfRadioButton reason) return;
        var isOther = string.Equals(reason.Tag as string, "other", StringComparison.Ordinal);
        CommentTextBox.IsEnabled = isOther && !_busy;
        if (!isOther) CommentTextBox.Clear();
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = FindSelectedReason();
        var choice = UninstallFeedbackChoice.Create(selected, selected == "other" ? CommentTextBox.Text : string.Empty);
        await ProceedAsync(choice);
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
        => await ProceedAsync(UninstallFeedbackChoice.Skip);

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _lifetime.Cancel();
        Outcome = UninstallFeedbackOutcome.Cancel;
        Close();
    }

    private async Task ProceedAsync(UninstallFeedbackChoice choice)
    {
        if (_busy) return;
        SetBusy(true);
        StatusText.Text = "正在提交卸载意向，网络异常不会阻止卸载…";
        UninstallIntentReceipt? receipt = null;
        var lifetimeToken = _lifetime.Token;
        try
        {
            receipt = await _submit(choice, lifetimeToken);
        }
        catch
        {
            // Feedback and reporting are best effort; proceeding never depends on the network.
        }
        if (lifetimeToken.IsCancellationRequested || !IsVisible) return;
        Receipt = receipt;
        Outcome = UninstallFeedbackOutcome.Proceed;
        _proceeding = true;
        Close();
    }

    private string? FindSelectedReason()
    {
        var selected = FindVisualChildren<WpfRadioButton>(this).FirstOrDefault(item => item.IsChecked == true);
        return string.IsNullOrWhiteSpace(selected?.Tag as string) ? null : (string)selected.Tag;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ConfirmButton.IsEnabled = !busy;
        SkipButton.IsEnabled = !busy;
        SubmitProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        foreach (var reason in FindVisualChildren<WpfRadioButton>(this)) reason.IsEnabled = !busy;
        var selected = FindSelectedReason();
        CommentTextBox.IsEnabled = !busy && selected == "other";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_proceeding)
        {
            _lifetime.Cancel();
            Outcome = UninstallFeedbackOutcome.Cancel;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject parent)
        where T : System.Windows.DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
