/// Transport and ZIP32 bounds shared by catalog, download and local installation.
/// These are independent of the smaller limits for words and theater resource packs.
public enum PetMarketLimits {
    public static let maximumArchiveBytes = 300 * 1024 * 1024
    public static let maximumFileBytes = maximumArchiveBytes
    public static let maximumTotalBytes = maximumArchiveBytes
    public static let maximumManifestBytes = 1024 * 1024
    // ZIP32's 65535 count is a ZIP64 sentinel. Reserve one real entry for the manifest.
    public static let maximumGIFCount = 65_533
    public static let maximumArchiveEntries = maximumGIFCount + 1
    public static let maximumGIFDimension = 4096
}
