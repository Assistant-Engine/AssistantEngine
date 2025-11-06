using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Notifications
{
    public sealed class ModalService : IModalService
    {
        public event Action<RenderFragment, string?, string?, string?>? OnShow;
        public event Action? OnClose;

        public void Show(RenderFragment content, string? title = null, string? size = null, string? className = null)
            => OnShow?.Invoke(content, title, size, className);

        public void Close() => OnClose?.Invoke();
    }

}
