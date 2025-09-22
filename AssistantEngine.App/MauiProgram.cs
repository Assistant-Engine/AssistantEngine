// MauiProgram.cs (AssistantEngine.App)
using AssistantEngine.UI.Config;
using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;

namespace AssistantEngine.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // ---- App data/config store ----
            var dataRoot = Microsoft.Maui.Storage.FileSystem.AppDataDirectory; // per-user data root


            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<AssistantEngine.App.App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif
            builder.Services.AddMauiBlazorWebView();

            // ---- Shared registrations (standardized) ----
            bool noInternetMode = false; // flip if you intentionally want offline-only mode
            builder.Services.AddAssistantEngineCore(new ConfigStorageOptions
            {
                DefaultDataRoot = dataRoot,
                ConfigFilePath = Path.Combine(dataRoot, "App_Data", "appsettings.AssistantEngine.json"),
            }, noInternetMode);

            // Optional: serve .mjs correctly inside BlazorWebView
            var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            provider.Mappings[".mjs"] = "text/javascript";
            builder.Services.Configure<Microsoft.AspNetCore.Builder.StaticFileOptions>(o =>
            {
                o.ContentTypeProvider = provider;
            });

            var mauiApp = builder.Build();

   
            mauiApp.Services.RunAssistantEngineStartup();
            return mauiApp;
        }

   
    }
}
