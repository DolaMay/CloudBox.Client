namespace CloudBox.Client.Services;

public static class AppSettingsStore
{
    private const string KeyServerUrl = "server_url";
    private const string KeyApiKey    = "api_key";

    public static string ServerUrl
    {
        get => Preferences.Default.Get(KeyServerUrl, "");
        set => Preferences.Default.Set(KeyServerUrl, value.Trim());
    }

    public static string ApiKey
    {
        get => Preferences.Default.Get(KeyApiKey, "");
        set => Preferences.Default.Set(KeyApiKey, value.Trim());
    }
}