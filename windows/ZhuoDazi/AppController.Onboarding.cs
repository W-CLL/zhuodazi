using System.Windows;
using ZhuoDazi.Models;
using Point = System.Windows.Point;

namespace ZhuoDazi;

public sealed partial class AppController
{
    public bool IsTrialVerificationPending { get; private set; }
    public string? TrialVerificationStatus { get; private set; }
    public event Action? TrialVerificationRequested;
    public void RetryTrialVerification() => TrialVerificationRequested?.Invoke();
    public void SetTrialVerificationState(bool pending, string? status = null)
    {
        IsTrialVerificationPending = pending;
        TrialVerificationStatus = status;
        StateChanged?.Invoke();
    }

    private OnboardingWindow? _guideWindow;
    private CancellationTokenSource? _guideCancellation;
    private int _guideGeneration;
    public bool IsGuideActive { get; private set; }
    public bool GuideBusy { get; private set; }
    public bool GuideIsUpgradeNotice { get; private set; }
    public string GuideActionLabel => Settings.Onboarding.IsComplete ? "重看使用引导" : Settings.Onboarding.Step > 0 ? "继续使用引导" : "开始一分钟体验";

    public void ReplayGuide() => StartOnboardingIfNeeded(true);

    public void StartOnboardingIfNeeded(bool manual = false)
    {
        if (_disposed || IsExiting || _petWindow is null) return;
        if (_guideWindow is not null) { _guideWindow.Activate(); return; }
        var progress = Settings.Onboarding;
        if (!manual && (IsQuiet || progress.IsComplete || progress.Dismissed && !progress.UpgradeNoticePending)) return;
        if (_interactionActive || _theaterActive || _trialVisitBusy || GuideBusy)
        {
            if (manual) ShowActionMessage("先玩完这一轮，再来认识我吧。");
            return;
        }
        if (manual && progress.IsComplete) Settings.Onboarding = progress = new OnboardingProgress();
        GuideIsUpgradeNotice = progress.UpgradeNoticePending;
        progress.UpgradeNoticePending = false;
        // Persist before showing: a crash or restart must not force the same card on the user.
        progress.Dismissed = true;
        Settings.OnboardingHintSeen = true;
        IsGuideActive = true;
        _visitorQueue.PauseActive();
        _randomTimer.Stop();
        _theaterTimer.Stop();
        _interactionTimer.Stop();
        _petWindow.HideSpeech();
        _petWindow.RefreshBehavior();
        _petWindow.Show();
        _petWindow.RefreshAppearance(CurrentPetPath());
        Save();
        _guideWindow = new OnboardingWindow(this);
        _guideWindow.Closed += (_, _) =>
        {
            _guideWindow = null;
            EndGuideSession();
        };
        _guideWindow.Show();
        StateChanged?.Invoke();
    }

    public void ShowInteractionSettings()
    {
        ShowSettings();
        _settingsWindow?.ShowInteractionTab();
    }

    public void AdvanceGuide(bool skipped = false)
    {
        if (_guideWindow is null) return;
        StopGuideDemo();
        Settings.Onboarding.Advance(skipped);
        Save();
        if (Settings.Onboarding.IsComplete) RestoreGuideBehavior();
        _guideWindow.Refresh();
        StateChanged?.Invoke();
    }

    public async Task PerformGuideActionAsync()
    {
        if (_guideWindow is null || GuideBusy || _petWindow is null) return;
        if (Settings.Onboarding.Step is 0 or 1 or 4) { AdvanceGuide(); return; }
        _petWindow.Show();
        var generation = ++_guideGeneration;
        GuideBusy = true;
        if (Settings.Onboarding.Step == 2)
        {
            _guideWindow.Refresh();
            _petWindow.ShowInteraction("初次见面", "欢迎来到屏幕角落！今天想怎样度过？",
                [new("元气满满", "happy", true), new("慢慢来就好", "rest")], choice =>
                {
                    if (!IsGuideActive || generation != _guideGeneration) return;
                    GuideBusy = false;
                    AdvanceGuide();
                });
            _petWindow.Activate();
            return;
        }
        if (Settings.Onboarding.Step != 3) { GuideBusy = false; return; }
        _guideCancellation = new CancellationTokenSource();
        var token = _guideCancellation.Token;
        _guideWindow.Refresh();
        var main = _petWindow;
        var original = new Point(main.Left, main.Top);
        PetWindow? guest = null;
        var completed = false;
        try
        {
            main.EnterScriptedMode();
            guest = new PetWindow(this, true);
            // Always use bundled artwork, independent of user imports or online entitlements.
            var bundled = _library.Scan(null);
            guest.RefreshAppearance(bundled.FirstOrDefault(path => path != CurrentPetPath()));
            guest.EnterScriptedMode();
            var area = main.GetWorkingArea();
            var left = Math.Max(area.Left, area.Left + (area.Width - main.Width - guest.Width - 20) / 2);
            var top = Math.Max(area.Top, area.Bottom - Math.Max(main.Height, guest.Height));
            main.Place(new Point(left, top));
            guest.Place(new Point(Math.Min(area.Right - guest.Width, left + main.Width + 20), top));
            guest.Show();
            var lines = new[]
            {
                "演示 · 新来的邻居，你好呀！", "演示 · 我带了一点下班的快乐。",
                "演示 · 那我们一起守住这个角落。", "演示 · 好呀，你忙，我们陪着你。"
            };
            for (var index = 0; index < lines.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                (index % 2 == 0 ? main : guest).ShowReaction(lines[index]);
                await Task.Delay(3000, token);
            }
            completed = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { _guideWindow?.Refresh($"演示暂时没能开始：{error.Message}。可以重试或跳过。"); }
        finally
        {
            guest?.Close();
            main.HideSpeech();
            main.LeaveScriptedMode();
            main.Place(original);
            _guideCancellation?.Dispose();
            _guideCancellation = null;
            GuideBusy = false;
            if (!IsGuideActive) RestoreGuideBehavior();
            _guideWindow?.Refresh();
        }
        if (generation != _guideGeneration) return;
        if (completed && IsGuideActive) AdvanceGuide();
        else _guideWindow?.Refresh("演示已结束。可以重看，或选择跳过这一步。");
    }

    public void StopGuideDemo()
    {
        _guideGeneration++;
        _guideCancellation?.Cancel();
        // Cancellation invalidates the callback before dismissing, so hiding is not completion.
        _petWindow?.DismissCurrentInteraction();
        _petWindow?.HideSpeech();
        if (_guideCancellation is null) GuideBusy = false;
        _guideWindow?.Refresh("进度已保留，可以继续体验。");
    }

    private void EndGuideSession()
    {
        StopGuideDemo();
        Settings.Onboarding.Dismissed = true;
        Save();
        RestoreGuideBehavior();
    }

    private void RestoreGuideBehavior()
    {
        IsGuideActive = false;
        _petWindow?.RefreshBehavior();
        _petWindow?.RefreshAppearance(CurrentPetPath());
        RestartRandomTimer();
        RestartTheaterTimer();
        RestartInteractionTimer();
        if (!IsExiting && !_disposed) _ = ShowQueuedVisitsAsync();
    }

    private void DismissGuide() => _guideWindow?.Close();

    private void ShowActionMessage(string message)
    {
        System.Windows.MessageBox.Show(_settingsWindow ?? (Window?)_guideWindow ?? _petWindow!, message,
            "桌搭子", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool PrepareManualScene()
    {
        if (IsGuideActive) { _guideWindow?.Activate(); return false; }
        if (_interactionActive || _theaterActive || _visitorQueue.IsShowing || GuideBusy)
        {
            ShowActionMessage("先玩完这一轮吧。");
            return false;
        }
        if (_petWindow is null) return false;
        if (!_petWindow.IsVisible) _petWindow.Show();
        if (Settings.ClickThrough) SetClickThrough(false);
        return true;
    }
}
