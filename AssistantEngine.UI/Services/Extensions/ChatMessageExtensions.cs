using Microsoft.Extensions.AI;
using System.Text.RegularExpressions;

namespace AssistantEngine.Services.Extensions
{
    public static class ChatMessageExtensions
    {
        public static string RemoveThinkTags(this string input)
        {
            // remove all <think>…</think> lines
            var noThink = Regex.Replace(
                input,
                @"(?ims)^[ \t]*<think>.*?</think>[ \t]*\r?\n?",
                string.Empty
            );

            // trim any remaining whitespace or blank lines at the ends
            return noThink.Trim();
        }


        public static List<ChatMessage> RemoveThinkMessages(this List<ChatMessage> messagesToFilter)
        {
            var filtered = new List<ChatMessage>();

            foreach (var msg in messagesToFilter)
            {
                var clone = msg.Clone();

                // Replace RemoveAll with a loop to remove items from the list
                for (int i = clone.Contents.Count - 1; i >= 0; i--)
                {
                    if (clone.Contents[i] is TextContent tc)
                    {
                        tc.Text = RemoveThinkTags(tc.Text);
                        if (string.IsNullOrWhiteSpace(tc.Text))
                        {
                            clone.Contents.RemoveAt(i);
                        }
                    }
                }

                if (clone.Contents.Count > 0)
                    filtered.Add(clone);
            }

            return filtered;
        }

    }
}
