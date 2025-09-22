using Microsoft.AspNetCore.Components.WebView.Maui;

namespace AssistantEngine.App
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();

#if DEBUG && WINDOWS
            Loaded += async (_, __) =>
            {
                // Get the underlying WebView2
                var field = typeof(BlazorWebView)
                    .GetField("_webview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                var webview = field?.GetValue(BlazorView) as Microsoft.UI.Xaml.Controls.WebView2;
                if (webview != null)
                {
                    await webview.EnsureCoreWebView2Async();
                    webview.CoreWebView2.OpenDevToolsWindow();
                }
            };
#endif
        }
    }
}
