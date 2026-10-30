using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZhuoDazi.Models;

internal sealed class PetMarketCardViewModel : INotifyPropertyChanged
{
    private string? _thumbnailPath;

    internal PetMarketCardViewModel(PetMarketProduct product)
    {
        Product = product;
        Tags = (product.Tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .Select(tag => tag.Length <= 12 ? tag : tag[..12])
            .ToArray();
    }

    internal PetMarketProduct Product { get; }
    public string Id => Product.Id;
    public string Title => Product.Title;
    public string Description => Product.Description;
    public string PriceLabel => Product.PriceLabel;
    public bool IsFree => Product.IsFree;
    public bool Owned => Product.Owned;
    public bool IsFeatured => Product.IsFeatured;
    public bool IsDefault => Product.IsDefault;
    public IReadOnlyList<string> Tags { get; }
    public string AssetLabel => $"{Product.AssetCount} 个动作资源";
    public string OwnershipLabel => Product.Owned ? "已拥有" : Product.IsFree ? "可领取" : "可购买";

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        internal set
        {
            if (string.Equals(_thumbnailPath, value, StringComparison.Ordinal)) return;
            _thumbnailPath = value;
            OnPropertyChanged();
        }
    }

    internal bool Matches(string query, string filter)
    {
        if (filter == "free" && !IsFree) return false;
        if (filter == "paid" && IsFree) return false;
        if (filter == "owned" && !Owned) return false;
        if (string.IsNullOrWhiteSpace(query)) return true;
        return Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
