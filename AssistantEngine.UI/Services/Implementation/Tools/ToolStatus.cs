using AssistantEngine.UI.Services.Types;

namespace AssistantEngine.Services.Implementation
{

    public interface IToolStatusNotifier
    {
        event Action<StatusMessage>? OnStatusMessage;

        void StatusMessage(StatusMessage s);
        void StatusMessage(string msg, StatusLevel level = StatusLevel.Information, string? title = null, string? source = null);
    }


    public class ToolStatusNotifier : IToolStatusNotifier
    {
        public event Action<StatusMessage>? OnStatusMessage;

        public void StatusMessage(StatusMessage s) => OnStatusMessage?.Invoke(s);

        public void StatusMessage(string msg, StatusLevel level = StatusLevel.Information, string? title = null, string? source = null)
            => OnStatusMessage?.Invoke(new StatusMessage(msg, level, title, source));
    }


}
