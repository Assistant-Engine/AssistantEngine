// ChatSessionDto.cs
namespace AssistantEngine.UI.Services.Models.Chat
{
    public class ChatSessionDto
    {
        public string Id { get; set; } = default!;
        public string Title { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public List<ChatMessageDto> Messages { get; set; } = new();
    }
}