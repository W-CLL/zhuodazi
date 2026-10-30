using System.Windows;

namespace ZhuoDazi;

public partial class PetMarketWindow : Window
{
    private readonly PetShopView _view;

    internal PetMarketWindow(AppController controller)
    {
        InitializeComponent();
        _view = new PetShopView(controller);
        ShopContent.Content = _view;
        Closed += (_, _) => _view.Dispose();
    }
}
