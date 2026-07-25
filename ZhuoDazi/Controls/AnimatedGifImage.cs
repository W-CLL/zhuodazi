using System.Windows;

namespace ZhuoDazi.Controls;

public sealed class AnimatedGifImage : System.Windows.Controls.Image
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath),
        typeof(string),
        typeof(AnimatedGifImage),
        new PropertyMetadata(null, OnFilePathChanged));

    private readonly AnimatedGifPlayer _player;

    public AnimatedGifImage()
    {
        _player = new AnimatedGifPlayer(this);
        Loaded += (_, _) => _player.Load(FilePath);
        Unloaded += (_, _) => _player.Stop();
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    private static void OnFilePathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var image = (AnimatedGifImage)sender;
        if (image.IsLoaded) image._player.Load(args.NewValue as string);
    }
}
