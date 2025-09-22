// ChatMappings.cs
using AssistantEngine.UI.Services.Models.Chat;
using Microsoft.Extensions.AI;

namespace AssistantEngine.UI.Services.Implementation.Models.Chat
{
    public static class ChatMappings
    {
        public static ChatMessageDto ToDto(this ChatMessage m)
            => new()
            {
                MessageId = m.MessageId!,
                Role = m.Role,
                AuthorName = m.AuthorName,
                Text = m.Text,
                AdditionalProperties = m.AdditionalProperties
            };

        public static ChatMessage ToModel(this ChatMessageDto d)
        {
            var m = new ChatMessage(d.Role, d.Text);
            m.MessageId = d.MessageId;
            m.AuthorName = d.AuthorName;
            m.AdditionalProperties = d.AdditionalProperties;
            return m;
        }

        public static ChatSessionDto ToDto(this ChatSession s)
            => new()
            {
                Id = s.Id,
                Title = s.Title,
                CreatedAt = s.CreatedAt,
                Messages = s.Messages.Select(m => m.ToDto()).ToList()
            };

        public static ChatSession ToModel(this ChatSessionDto d)
            => new()
            {
                Id = d.Id,
                Title = d.Title,
                CreatedAt = d.CreatedAt,
                Messages = d.Messages.Select(x => x.ToModel()).ToList()
            };
    }
}
