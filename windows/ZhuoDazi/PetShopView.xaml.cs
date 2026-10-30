using System.Windows;
using System.Windows.Controls;

namespace ZhuoDazi;

public partial class PetShopView : System.Windows.Controls.UserControl, IDisposable
{
    private readonly AppController _controller;
    private readonly PetMarketView _pets;
    private ResourcePackView? _resources;
    internal PetShopView(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        _pets = new PetMarketView(controller);
        PetsContent.Content = _pets;
    }

    private void Section_Checked(object sender, RoutedEventArgs e)
    {
        if (PetsContent is null || ResourcesContent is null) return;
        var resources = sender is System.Windows.Controls.RadioButton { Tag: "resources" };
        if (resources)
        {
            _resources ??= new ResourcePackView(_controller);
            ResourcesContent.Content = _resources;
        }
        // Keep the pet view loaded while browsing resources, so paid orders still complete.
        PetsContent.Visibility = resources ? Visibility.Collapsed : Visibility.Visible;
        ResourcesContent.Visibility = resources ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Dispose() { _pets.Dispose(); _resources?.Dispose(); }
}
