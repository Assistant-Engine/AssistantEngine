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
        bool IsConfirmActive { get; }
        string ConfirmOkText { get; }
        string ConfirmCancelText { get; }
        void ResolveConfirm(bool result);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="title"></param>
        /// <param name="confirmText"></param>
        /// <param name="cancelText"></param>
        /// <param name="size">"sm" | "lg" | "xl"</param>
        /// <param name="className"></param>
        /// <returns></returns>
        Task<bool> ConfirmAsync(string message, string? title = null, string confirmText = "OK", string cancelText = "Cancel", string? size = null, string? className = null);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="content"></param>
        /// <param name="title"></param>
        /// <param name="size">"sm" | "lg" | "xl"</param>
        /// <param name="className"></param>
        void Show(RenderFragment content, string? title = null, string? size = null, string? className = null); 
        void Close();
    }

}
