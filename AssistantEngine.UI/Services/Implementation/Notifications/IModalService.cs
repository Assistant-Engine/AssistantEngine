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

        private TaskCompletionSource<bool>? _confirmTcs;
        public bool IsConfirmActive { get; private set; }
        public string ConfirmOkText { get; private set; } = "OK";
        public string ConfirmCancelText { get; private set; } = "Cancel";

        public void Show(RenderFragment content, string? title = null, string? size = null, string? className = null) => OnShow?.Invoke(content, title, size, className);

        public void Close()
        {
            if (IsConfirmActive) ResolveConfirm(false); // backdrop/× = cancel
            OnClose?.Invoke();
        }

        public Task<bool> ConfirmAsync(string message, string? title = null, string ok = "OK", string cancel = "Cancel", string? size = null, string? className = null)
        {
            _confirmTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            IsConfirmActive = true;
            ConfirmOkText = ok;
            ConfirmCancelText = cancel;

            RenderFragment content = b =>
            {
                b.OpenElement(0, "p");
                b.AddContent(1, message);
                b.CloseElement();
            };
            var finalClass = string.IsNullOrWhiteSpace(className) ? "confirm-box": (className.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("confirm-box", StringComparer.OrdinalIgnoreCase) ? className : $"{className} confirm-box");

            Show(content, title ?? "Confirm", size, finalClass);

            return _confirmTcs.Task;
        }

        public void ResolveConfirm(bool result)
        {
            _confirmTcs?.TrySetResult(result);
            _confirmTcs = null;
            IsConfirmActive = false;
        }
  
    }

}
