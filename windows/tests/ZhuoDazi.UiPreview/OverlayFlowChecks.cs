using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ZhuoDazi;
using ZhuoDazi.Models;

internal static class OverlayFlowChecks
{
    private static int _assertions;

    public static async Task Run(AppController controller, PetWindow pet, string output)
    {
        var bubble = (Border)pet.FindName("SpeechBubble");
        var speech = (TextBlock)pet.FindName("SpeechText");
        var choices = (WrapPanel)pet.FindName("InteractionChoicePanel");
        var reactionsPosted = 0;
        DispatcherHookEventHandler posted = (_, args) =>
        {
            if (args.Operation.Priority == DispatcherPriority.Background) reactionsPosted++;
        };
        string? completionTrace = null;
        pet.ShowInteraction("普通互动", "保留卡片，等待用户回应", [new("回应", "answer")], _ => completionTrace = Environment.StackTrace);
        pet.Dispatcher.Hooks.OperationPosted += posted;
        try
        {
            pet.ShowReaction("互动期间的旧提示");
            await Task.Delay(200);
            Require(reactionsPosted < 8, "A visible interaction must not continuously repost deferred speech to the dispatcher.");
        }
        finally { pet.Dispatcher.Hooks.OperationPosted -= posted; }
        controller.SetAutoCheckUpdates(controller.Settings.AutoCheckUpdates);
        controller.RefreshPremiumAccess();
        await Task.Delay(5500);
        Require(pet.IsInteractionVisible, "Ordinary interaction survives settings/license refresh and the old bubble timeout. " + completionTrace);
        pet.HideSpeech();
        pet.DismissCurrentInteraction();
        await Task.Delay(100);
        Require(bubble.Visibility != Visibility.Visible, "Explicitly clearing speech also invalidates deferred status.");

        pet.ShowInteraction("回应优先", "回答后显示新台词", [new("回答", "answer")], _ => pet.ShowReaction("新的回答"));
        var oldButton = (Button)choices.Children[0];
        pet.ShowReaction("较早的状态提示");
        oldButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(100);
        Require(speech.Text == "新的回答", "Deferred status must not overwrite a response from the interaction callback.");
        var responses = 0;
        pet.ShowInteraction("下一张卡", "旧按钮已失效", [new("新的按钮", "new")], _ => responses++);
        oldButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(pet.IsInteractionVisible && responses == 0, "A stale button event cannot close or answer a replacement card.");
        pet.ShowReaction("关闭后再说");
        pet.DismissCurrentInteraction();
        Require(responses == 1 && speech.Text == "关闭后再说" && bubble.Visibility == Visibility.Visible,
            "A valid deferred status appears once when a card closes without a new scene.");

        var reminder = new ReminderDefinition { Message = "看完答案后提醒我", LocalTime = DateTime.Now.AddSeconds(-2) };
        controller.Settings.Reminders.Add(reminder);
        var checkReminders = typeof(AppController).GetMethod("CheckReminders", BindingFlags.Instance | BindingFlags.NonPublic)!;
        pet.ShowInteraction("题目", "等待回答", [new("回答", "answer")], _ =>
            pet.ShowInteraction("答案", "等待关闭答案", [new("看完了", "done")], _ => { }));
        checkReminders.Invoke(controller, null);
        Require(reminder.Enabled, "A reminder remains due while a question is visible.");
        ((Button)choices.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checkReminders.Invoke(controller, null);
        Require(reminder.Enabled && pet.IsInteractionVisible, "A reminder stays pending across question-to-answer replacement.");
        ((Button)choices.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checkReminders.Invoke(controller, null);
        Require(!reminder.Enabled && speech.Text == reminder.Message && bubble.Visibility == Visibility.Visible,
            "The reminder is displayed and consumed on the first tick after the answer closes.");

        controller.ReplayGuide();
        controller.Settings.Onboarding.Step = 2;
        await controller.PerformGuideActionAsync();
        controller.RefreshPremiumAccess();
        controller.SetAutoCheckUpdates(controller.Settings.AutoCheckUpdates);
        await Task.Delay(5500);
        Require(pet.IsInteractionVisible && controller.Settings.Onboarding.Step == 2,
            "Teaching interaction stays visible across background refresh until an explicit response.");
        pet.DismissCurrentInteraction();
        var tutorial = controller.PerformGuideActionAsync();
        await Task.Delay(1000);
        controller.RefreshPremiumAccess();
        await Task.Delay(4500);
        Require(controller.GuideBusy && controller.Settings.Onboarding.Step == 3 && Application.Current.Windows.OfType<PetWindow>().Count() == 2,
            "Teaching theater survives refresh and retains both actors halfway through playback.");
        await tutorial;
        Require(controller.Settings.Onboarding.Step == 4, "Teaching theater completes normally after refresh.");
        Application.Current.Windows.OfType<OnboardingWindow>().Single().Close();

        // Invoke the real normal scene with the offline trial entitlement and bundled actors.
        var runTheater = typeof(AppController).GetMethod("RunTheaterAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var theater = (Task)runTheater.Invoke(controller, [true])!;
        await Task.Delay(800);
        controller.RefreshPremiumAccess();
        controller.SetAutoCheckUpdates(controller.Settings.AutoCheckUpdates);
        await Task.Delay(4500);
        Require(!theater.IsCompleted && Application.Current.Windows.OfType<PetWindow>().Count() == 2,
            "Normal theater survives settings/license refresh with both native windows intact.");
        controller.TogglePetVisibility();
        await theater.WaitAsync(TimeSpan.FromSeconds(3));
        Require(!pet.IsVisible && Application.Current.Windows.OfType<PetWindow>().Count() == 1,
            "Explicitly hiding cancels normal theater and removes its companion.");
        controller.TogglePetVisibility();
        pet.HideSpeech();
        File.WriteAllText(Path.Combine(output, "overlay-flow-results.txt"), $"PASS: {_assertions} real WPF overlay lifecycle assertions.\nNo production API or user settings were used.\n");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }
}
