namespace ZhuoDazi.Services;

internal static class DeskPetApi
{
    public const string Host = "in.desktoppet.online";
    public const string BaseUrl = "https://" + Host;
    public const string SigningPublicKeySpki = "MCowBQYDK2VwAyEANjBEMMQ5TY+0ECNoRqQy9780eoVOzkKpzFDq2TwLytU=";

    public const string Activate = BaseUrl + "/api/activate";
    public const string Trial = BaseUrl + "/api/trial";
    public const string Feedback = BaseUrl + "/api/feedback";
    public const string InteractionProfile = BaseUrl + "/api/interactions/profile";
    public const string InteractionEvents = BaseUrl + "/api/interactions/events";
    public const string ContentBatch = BaseUrl + "/api/content/batch";
    public const string ContentOfflinePack = BaseUrl + "/api/content/offline-pack";
    public const string Companion = BaseUrl + "/api/companion";
    public const string CompanionPair = Companion + "/pair";
    public const string CompanionDeliveries = Companion + "/deliveries";
    public const string AnalyticsEvents = BaseUrl + "/api/analytics/events";
    public const string UpdateLatest = BaseUrl + "/api/update/latest?platform=windows&architecture=x64";
    public const string DownloadPathPrefix = "/downloads/";
}
