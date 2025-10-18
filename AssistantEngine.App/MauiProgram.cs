
// MauiProgram.cs (AssistantEngine.App)
#if WINDOWS
using AssistantEngine.App.Platforms.Windows;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI;

using Windows.UI;
using Application = Microsoft.Maui.Controls.Application;
using Color = Windows.UI.Color;
using Window = Microsoft.UI.Xaml.Window;
#endif
#if WINDOWS || WEB
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Builder;
#endif
using AssistantEngine.UI.Config;
using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using System;


namespace AssistantEngine.App
{
    public static class MauiProgram
    {
        static bool _themeWired;

#if WINDOWS
        static void ApplyWinUiTheme(Microsoft.UI.Xaml.Window win, string theme)
        {
            if (win?.Content is FrameworkElement root)
                root.RequestedTheme = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase)
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
        }

#endif

        public static MauiApp CreateMauiApp()
        {
            // ---- App data/config store ----
            var dataRoot = Microsoft.Maui.Storage.FileSystem.AppDataDirectory; // per-user data root
            var logFile = AssistantEngine.App.Logging.FileConsoleRedirect.Init();
            //Console.WriteLine($"Log file started: {logFile}");
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
            builder.ConfigureLifecycleEvents(events =>
            {
#if WINDOWS
               

      
                events.AddWindows(w =>
                {
                    w.OnWindowCreated(win =>
                    {
                        win.ExtendsContentIntoTitleBar = true;

                        win.DispatcherQueue.TryEnqueue(() =>
                        {
                            var aw = win.AppWindow;
                            if (aw is null) return;

                            if (aw.Presenter.Kind != AppWindowPresenterKind.Overlapped)
                                aw.SetPresenter(AppWindowPresenterKind.Overlapped);

                            var tb = aw.TitleBar;
                            if (tb is null) return;

                            // initial paint so we don't see white
                            var bg = Windows.UI.Color.FromArgb(255, 30, 30, 30);
                            tb.BackgroundColor = bg;
                            tb.ButtonBackgroundColor = bg;
                            tb.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 224, 224, 224);
                            tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 42, 42, 42);
                            tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 54, 54, 54);
                            tb.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 45, 45, 45);
                            tb.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 160, 160, 160);
                            tb.ButtonHoverForegroundColor = tb.ButtonForegroundColor;
                            tb.ButtonPressedForegroundColor = tb.ButtonForegroundColor;
                            tb.ButtonInactiveForegroundColor = tb.InactiveForegroundColor;

                            WindowsTitleBar.Init(win);

                            var effective = (Application.Current?.UserAppTheme is AppTheme.Dark or AppTheme.Light)
                                ? Application.Current!.UserAppTheme
                                : AppInfo.Current.RequestedTheme;

                            var initial = Application.Current?.UserAppTheme == AppTheme.Light ? "light" : "dark";
                            WindowsTitleBar.Update(initial);
                            ApplyWinUiTheme(win, initial);

                            // SUBSCRIBE ONCE (no local functions)
                            if (!_themeWired)
                            {
                                AssistantEngine.UI.Services.ThemeBridge.ThemeChanged += ThemeBridge_ThemeChanged;
                                _themeWired = true;
                            }

                            // Unwire when window closes
                            win.Closed += (_, __) =>
                            {
                                if (_themeWired)
                                {
                                    AssistantEngine.UI.Services.ThemeBridge.ThemeChanged -= ThemeBridge_ThemeChanged;
                                    _themeWired = false;
                                }
                            };

                            // handler that DOES NOT call ThemeBridge.OnThemeChanged(...)
                            static void ThemeBridge_ThemeChanged(string t)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ThemeChanged] {t}");
                                Application.Current!.UserAppTheme =
                                    string.Equals(t, "dark", StringComparison.OrdinalIgnoreCase) ? AppTheme.Dark : AppTheme.Light;

                                // Get current WinUI window safely
                                if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Window wui)
                                {
                                    WindowsTitleBar.Update(t);
                                    ApplyWinUiTheme(wui, t);
                                }
                            }
                        });
                    });
                });
#endif


            });

            // ---- Shared registrations (standardized) ----
            bool noInternetMode = false; // flip if you intentionally want offline-only mode
            builder.Services.AddAssistantEngineCore(new ConfigStorageOptions
            {
                DefaultDataRoot = dataRoot,
                ConfigFilePath = Path.Combine(dataRoot, "App_Data", "appsettings.AssistantEngine.json"),
            }, noInternetMode);
          
            // Optional: serve .mjs correctly inside BlazorWebView
#if WINDOWS || WEB
            // Optional: serve .mjs correctly inside Blazor Server (not needed for MAUI BWebView)
            var provider = new FileExtensionContentTypeProvider();
            provider.Mappings[".mjs"] = "text/javascript";
            builder.Services.Configure<StaticFileOptions>(o =>
            {
                o.ContentTypeProvider = provider;
            });
#endif
            var mauiApp = builder.Build();
         

           // builder.Logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug);
         
            try
            {
                mauiApp.Services.RunAssistantEngineStartup();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Startup crashed:");
                Console.WriteLine(ex);
            }
            return mauiApp;
        }


    }
}