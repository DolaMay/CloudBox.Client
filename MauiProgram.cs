using Microsoft.Extensions.Logging;

namespace CloudBox.Client;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Core 的路径注入：必须在任何 Core 代码使用之前
        CloudBox.Core.AppPaths.AppDataDirectory = FileSystem.AppDataDirectory;
        CloudBox.Core.AppPaths.CacheDirectory = FileSystem.CacheDirectory;
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}