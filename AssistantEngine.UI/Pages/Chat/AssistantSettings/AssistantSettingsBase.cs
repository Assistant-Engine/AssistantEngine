using AssistantEngine.Factories;
using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Implementation.Config;
using AssistantEngine.UI.Services.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Xml.Linq;
namespace AssistantEngine.UI.Pages.Chat.AssistantSettings;
public abstract class AssistantSettingsBase : ComponentBase
{
    [CascadingParameter] public AssistantConfig AssistantConfig { get; set; } = default!;

    // Replace CascadingParameter for clientState with DI injection (app-wide state)
    [Inject] public ChatClientState ClientState { get; set; } = default!;

    // Optional: parent refresh callback as a cascading delegate
    [CascadingParameter] public Func<Task>? RequestRefresh { get; set; }
    [CascadingParameter(Name = "SaveAssistant")] public Func<Task>? SaveAssistantAsync { get; set; }
    [CascadingParameter(Name = "MarkDirty")]      public Action? MarkDirty { get; set; }
    [Parameter] public EventCallback OnStateChange { get; set; } // keep for parent pages that use it directly

    [Inject] protected IAssistantConfigStore ConfigStore { get; set; } = default!;
    [Inject] protected IModalService Modal { get; set; } = default!;
    [Inject] protected IJSRuntime Js { get; set; } = default!;
}
