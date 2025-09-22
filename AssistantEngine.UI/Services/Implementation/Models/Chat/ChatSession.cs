using Microsoft.Extensions.AI;

namespace AssistantEngine.UI.Services.Implementation.Models.Chat
{
    public class ChatSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "New Chat";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<ChatMessage> Messages { get; set; } = new();

        public bool DefaultTitle()
        {
            return Title == "New Chat";
        }
    }

}


