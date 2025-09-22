// Program.cs (AssistantEngine.Web)
using AssistantEngine.UI.Config;
using AssistantEngine.UI.Services.Options;
using AssistantEngine.UI.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- UI pipeline (Web-specific) ----
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents(o => o.DetailedErrors = true)
    .AddCircuitOptions(o => o.DetailedErrors = true);

// ---- App data/config store ----
var dataRoot = builder.Environment.ContentRootPath;

builder.Services.AddAssistantEngineCore(new ConfigStorageOptions
{
    DefaultDataRoot = dataRoot,
    ConfigFilePath = Path.Combine(dataRoot, "App_Data", "appsettings.AssistantEngine.json"),
}, noInternetMode: false);

var app = builder.Build();
app.Services.RunAssistantEngineStartup();
// ---- Middleware ----
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
