using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services
{
    public static class ThemeBridge
    {
        public static event Action<string>? ThemeChanged;

        [JSInvokable("OnThemeChanged")]
        public static void OnThemeChanged(string theme)
        {
            System.Diagnostics.Debug.WriteLine($"ThemeBridge.OnThemeChanged: {theme}");
            ThemeChanged?.Invoke(theme);
            // Optional: force a break when it’s called
            // System.Diagnostics.Debugger.Break();
        }
    }
}
