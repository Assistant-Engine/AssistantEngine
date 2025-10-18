// REMOVE these two methods from the existing class (leave AddAssistantEngineCore as-is)

// ADD this new file DependencyInjection.Web.cs
#if WINDOWS || WEB
using Microsoft.AspNetCore.Builder;

namespace AssistantEngine.UI.Services;

public static class DependencyInjectionWebExtensions
{
    public static IApplicationBuilder UseAssistantEngineStartup(this IApplicationBuilder app)
    {
        AssistantEngine.UI.Services.Implementation.Startup.StartupInit.FireAndForget(app.ApplicationServices);
        return app;
    }

  
}
#endif
