// ChatMessageDto.cs
using Microsoft.Extensions.AI;

namespace AssistantEngine.UI.Services.Models.Chat
{
    public class ChatMessageDto
    {
        public string? MessageId { get; set; }
        public ChatRole Role { get; set; }
        public string? AuthorName { get; set; }
        public string Text { get; set; } = string.Empty;
        public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
    }
}
