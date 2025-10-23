using AssistantEngine.Services.Implementation.Tools;
using AssistantEngine.Factories; // ChatClientState
using AssistantEngine.Services.Implementation.Tools;
using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

internal class Program
{
    static async Task Main()
    {
        var services = new ServiceCollection();
        var root = AppContext.BaseDirectory;

        services.AddAssistantEngineCore(new ConfigStorageOptions
        {
            DefaultDataRoot = root,
            ConfigFilePath = Path.Combine(root, "App_Data", "appsettings.AssistantEngine.json"),// where the current model files are stored fyi
        }, noInternetMode: false); 

        var sp = services.BuildServiceProvider();
        ITool tool = new GenerateRandomNumberTool(null);
        services.AddSingleton<ITool>(tool);
        sp.RunAssistantEngineStartup(); //pre existing methods
        
        var state = sp.GetRequiredService<ChatClientState>();
        await state.ChangeModelAsync();   
        var msgs = new List<ChatMessage> //microsoft.extensions.ai models
        {    new(ChatRole.System, "Be concise."),
             new(ChatRole.User, "Can you use check if you have the GenerateRandomNumber function injected and if so use it to generate a random number between 10, and 20 and output it to me")
        };
        
        var response = await state.Client.GetResponseAsync(msgs, state.ChatOptions);
        Console.WriteLine(response);
        Console.ReadLine();
    }
}
