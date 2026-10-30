namespace ZhuoDazi.Models;

public sealed class ResourcePackCatalog
{
    public List<ShopResourcePack> Packs { get; set; } = [];
}

public sealed class ShopResourcePack
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public int FileCount { get; set; }
    public bool IsWords => Category == "interaction-words";
    public string CategoryLabel => IsWords ? "互动词库" : "小剧场";
    public string Icon => IsWords ? "词" : "幕";
    public string Summary => $"{CategoryLabel} · {FileCount} 个内容 · {Size / 1024d:0.#} KB";
}
