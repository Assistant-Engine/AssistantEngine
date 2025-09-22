namespace AssistantEngine.Services.Implementation
{
    // 1) Define the interface
    public interface IToolStatusNotifier
    {
        /// <summary>
        /// Fired whenever a tool wants to emit a status update.
        /// </summary>
        event Action<string>? OnStatusMessage;

        /// <summary>
        /// Call this from inside your tool to broadcast a status.
        /// </summary>
        void StatusMessage(string msg);
    }

    // 2) Implement it
    public class ToolStatusNotifier : IToolStatusNotifier
    {
        public event Action<string>? OnStatusMessage;
        public void StatusMessage(string msg)
            => OnStatusMessage?.Invoke(msg);
    }

}
