using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.UI;
using Color = Windows.UI.Color;
using Window = Microsoft.UI.Xaml.Window;
namespace AssistantEngine.App.Platforms.Windows
{



    internal static class WindowsTitleBar
    {
        private static Microsoft.UI.Windowing.AppWindow? _appWindow;

        public static void Init(Window win)
        {
            _appWindow = win.AppWindow;
        }

        public static void Update(string theme)
        {
            if (_appWindow is null) return;
            var tb = _appWindow.TitleBar;

            if (string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase))
            {
                var bg = Color.FromArgb(255, 30, 30, 30);
                tb.BackgroundColor = bg;
                tb.ForegroundColor = Color.FromArgb(255, 224, 224, 224);
                tb.ButtonBackgroundColor = bg;
                tb.ButtonForegroundColor = Color.FromArgb(255, 224, 224, 224);
                tb.InactiveBackgroundColor = Color.FromArgb(255, 24, 24, 24);
                tb.InactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
                tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 48, 48, 48);
                tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 64, 64, 64);
            }
            else
            {
                var bg = Color.FromArgb(255, 243, 243, 243);
                tb.BackgroundColor = bg;
                tb.ForegroundColor = Color.FromArgb(255, 30, 30, 30);
                tb.ButtonBackgroundColor = bg;
                tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
                tb.InactiveBackgroundColor = Color.FromArgb(255, 230, 230, 230);
                tb.InactiveForegroundColor = Color.FromArgb(255, 100, 100, 100);
                tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 220, 220, 220);
                tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 200, 200, 200);
            }
        }
    }

}
