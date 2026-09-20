using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZhuoDazi;
using ZhuoDazi.Models;

internal static class GuideFlowChecks
{
    private static int _assertions;
    public static async Task Run(AppController controller, PetWindow pet, string output)
    {
        Directory.CreateDirectory(output);
        var preferences = (controller.Settings.RandomPetEnabled, controller.Settings.RandomInteractionsEnabled,
            controller.Settings.TheaterEnabled, controller.Settings.DailySpeechEnabled);
        controller.StartOnboardingIfNeeded();
        var guide = Current();
        Require(controller.IsGuideActive && !controller.CanShowVisits, "Welcome excludes incoming visits.");
        Capture(guide, output, "01-welcome");
        Click(guide, "带我体验一下");
        Require(controller.Settings.Onboarding.Step == 1, "Explicit start advances to controls.");
        Click(guide, "下一步");
        Click(guide, "让它回应我");
        Require(pet.IsInteractionVisible && controller.Settings.Onboarding.Step == 2, "Showing interaction alone cannot complete it.");
        Capture(guide, output, "02-interaction-guide");
        Capture(pet, output, "03-interaction-pet");
        Click(pet, "元气满满");
        Require(controller.Settings.Onboarding.Step == 3 && !pet.IsInteractionVisible, "Responding completes the local interaction.");
        var theater = controller.PerformGuideActionAsync();
        await Task.Delay(250);
        Require(controller.GuideBusy, "Theater actually opens before reporting progress.");
        controller.StopGuideDemo();
        await theater;
        Require(controller.Settings.Onboarding.Step == 3 && !controller.GuideBusy, "Stopping theater retains the current step and allows retry.");
        await controller.PerformGuideActionAsync();
        Require(controller.Settings.Onboarding.Step == 4, "Successfully presented short theater advances to recovery instructions.");
        guide.Width = 360;
        guide.Height = 350;
        Capture(guide, output, "04-recovery-small");
        guide.Width = 490;
        guide.Height = 510;
        Capture(guide, output, "05-recovery");
        Click(guide, "知道了");
        Require(controller.Settings.Onboarding.IsComplete && !controller.IsGuideActive, "Completion restores normal behavior.");
        Require(preferences == (controller.Settings.RandomPetEnabled, controller.Settings.RandomInteractionsEnabled,
            controller.Settings.TheaterEnabled, controller.Settings.DailySpeechEnabled), "Tutorial must not change automatic settings.");
        Capture(guide, output, "06-complete");
        Click(guide, "完成");
        controller.StartOnboardingIfNeeded();
        Require(!Application.Current.Windows.OfType<OnboardingWindow>().Any(), "Completed guide does not reopen automatically.");
        controller.ReplayGuide();
        guide = Current();
        Click(guide, "带我体验一下");
        Click(guide, "跳过这一步");
        Click(guide, "让它回应我");
        controller.TogglePetVisibility();
        Require(controller.Settings.Onboarding.Step == 2 && !pet.IsVisible && !pet.IsInteractionVisible && !controller.IsGuideActive,
            "Hiding during interaction must cancel without claiming completion.");
        controller.ReplayGuide();
        guide = Current();
        Require(pet.IsVisible && controller.Settings.Onboarding.Step == 2, "Replay resumes an interrupted guide and shows the pet.");
        Click(guide, "让它回应我");
        pet.DismissCurrentInteraction();
        Require(controller.Settings.Onboarding.Step == 3, "Explicitly closing the visible demo card completes interaction.");
        theater = controller.PerformGuideActionAsync();
        await Task.Delay(100);
        controller.PauseForOneHour();
        await theater;
        Require(controller.Settings.Onboarding.Step == 3 && controller.IsQuiet && !controller.IsGuideActive,
            "Pausing cancels the scene without advancing or resurrecting it.");
        controller.ResumeCompanionship();
        Require(!controller.GuideBusy && !controller.IsGuideActive, "Resume must not restart a cancelled tutorial.");
        controller.ReplayGuide();
        theater = controller.PerformGuideActionAsync();
        await Task.Delay(100);
        var reminder = new ReminderDefinition { Message = "提醒不能被演示收尾清除", LocalTime = DateTime.Now.AddSeconds(-2) };
        controller.Settings.Reminders.Add(reminder);
        var checkReminders = typeof(AppController).GetMethod("CheckReminders", BindingFlags.Instance | BindingFlags.NonPublic)!;
        checkReminders.Invoke(controller, null);
        Require(reminder.Enabled, "A reminder must stay due until cancelled tutorial cleanup has finished.");
        await theater;
        checkReminders.Invoke(controller, null);
        Require(!reminder.Enabled && ((TextBlock)pet.FindName("SpeechText")).Text == reminder.Message,
            "The reminder is displayed after cleanup and then marked delivered.");
        controller.ReplayGuide();
        guide = Current();
        guide.WindowState = WindowState.Minimized;
        await Task.Delay(100);
        Require(!controller.IsGuideActive && !Application.Current.Windows.OfType<OnboardingWindow>().Any(),
            "Minimizing is treated as later, and cannot keep ordinary companionship paused.");
        File.WriteAllText(Path.Combine(output, "guide-flow-results.txt"), $"PASS: {_assertions} real WPF guide flow assertions; 6 window renders.\nMigration/restart checks run separately in GifLibraryService.Checks.\n");
    }

    private static OnboardingWindow Current() => Application.Current.Windows.OfType<OnboardingWindow>().Single();
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }
    private static void Click(Window window, string label)
    {
        window.UpdateLayout();
        Children<Button>(window).Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static void Capture(Window window, string output, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }
}
