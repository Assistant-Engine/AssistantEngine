using AssistantEngine.Factories;
using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Implementation.Config;
using AssistantEngine.UI.Services.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Xml.Linq;
namespace AssistantEngine.UI.Pages.Chat.AssistantSettings;
public abstract class AssistantSettingsBase : ComponentBase
{
    [CascadingParameter(Name = "AssistantConfig")] public AssistantConfig AssistantConfig { get; set; } = default!;
    [CascadingParameter(Name = "SaveAssistant")] public Func<Task>? SaveAssistantAsync { get; set; }
    [CascadingParameter(Name = "MarkDirty")] public Action? MarkDirty { get; set; }
    [CascadingParameter(Name = "RequestRefresh")] public Func<Task>? RequestRefresh { get; set; }
    [Inject] public ChatClientState ClientState { get; set; } = default!;
    [Parameter] public EventCallback OnStateChange { get; set; } // keep for parent pages that use it directly
    [Inject] protected IAssistantConfigStore ConfigStore { get; set; } = default!;
    [Inject] protected IModalService Modal { get; set; } = default!;
    [Inject] protected IJSRuntime Js { get; set; } = default!;
}
