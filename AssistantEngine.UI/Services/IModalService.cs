using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services
{
    public interface IModalService
    {
        event Action<RenderFragment, string?, string?, string?>? OnShow;
        event Action? OnClose;

        void Show(RenderFragment content, string? title = null, string? size = null, string? className = null); // size: "sm" | "lg" | "xl"
        void Close();
    }

}
