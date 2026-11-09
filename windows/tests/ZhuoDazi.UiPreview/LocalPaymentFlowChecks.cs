using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZhuoDazi;
using ZhuoDazi.Services;

internal static class LocalPaymentFlowChecks
{
    internal static async Task Run(string configPath, string output)
    {
        Directory.CreateDirectory(output);
        var environment = LocalPaymentTestEnvironment.Load(Path.GetFullPath(configPath));
        DeskPetHttp.HandlerFactory = environment.CreateHandler;
        using var licenses = new LicenseService(environment.DataDirectory);
        await licenses.CheckTrialAsync();
        if (licenses.IsActivated || !licenses.IsTrialActive) throw new Exception("UI check requires an isolated fresh trial profile.");
        var store = new SettingsStore(environment.DataDirectory);
        using var controller = new AppController(licenses, store, true,
            new InteractionService(store, licenses, environment.CreateHandler(), environment.SigningPublicKey));
        controller.ConfigureLocalPaymentTest(environment);
        controller.ShowSettings();
        var settings = Application.Current.Windows.OfType<SettingsWindow>().Single();
        settings.ShowPetMarketTab();
        PetMarketView? market = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(150);
            market = Descendants(settings).OfType<PetMarketView>().FirstOrDefault();
            if (market?.FindName("ProductList") is ListBox list && list.Items.Count == 2) break;
        }
        if (market?.FindName("ProductList") is not ListBox products || products.Items.Count != 2)
            throw new Exception("Full settings shop did not load both live local fixtures.");
        products.SelectedIndex = 0;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (products.Items.Cast<ZhuoDazi.Models.PetMarketCardViewModel>().All(card => card.ThumbnailPath is not null)) break;
            await Task.Delay(100);
        }
        if (products.Items.Cast<ZhuoDazi.Models.PetMarketCardViewModel>().Any(card => card.ThumbnailPath is null))
            throw new Exception("Every product card must load its thumbnail alongside the detail preview.");
        if (market.FindName("BuyButton") is not Button { Visibility: Visibility.Visible })
            throw new Exception("Paid product must show the purchase entry in the full settings shop.");
        Capture(settings, Path.Combine(output, "full-settings-shop.png"));
        var activation = new ActivationWindow(licenses, false, null, false, null, controller.PaymentCheckout) { Owner = settings };
        activation.Show();
        await Task.Delay(500);
        if (activation.FindName("AlipayActivationButton") is not Button { IsVisible: true, IsEnabled: true })
            throw new Exception("Activation window must expose enabled official checkout.");
        ((TextBox)activation.FindName("DeviceCodeText")).Text = "[测试设备码已隐藏]";
        Capture(activation, Path.Combine(output, "activation-alipay.png"));
        activation.Close();
        settings.Hide();
        Console.WriteLine("Full local payment UI checks passed: integrated shop, both products, purchase entry and activation checkout button.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
