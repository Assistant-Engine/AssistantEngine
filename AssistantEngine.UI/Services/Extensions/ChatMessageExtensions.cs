using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace AssistantEngine.Services.Extensions
{
    public static class ChatMessageExtensions
    {
        // --- Centralized regex ---
        private static readonly Regex CitationRegex =
            new(@"<citation filename='(?<file>[^']*)' page_number='(?<page>\d*)'>(?<quote>.*?)</citation>", RegexOptions.NonBacktracking);

        private static readonly Regex ThinkBlockRegex =
            new(@"<think>(?<quote>[\s\S]*?)(?:</think>|$)", RegexOptions.NonBacktracking);

        private static readonly Regex ThinkStripRegex =
            new(@"<think>[\s\S]*?(?:</think>|$)", RegexOptions.NonBacktracking);

        private static readonly Regex ThinkTagAny =
            new(@"</?think>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex MultiSpace =
            new(@"\s{2,}", RegexOptions.Multiline);

        public static bool IsThinkingContent(this AIContent c)
        {
            if (c is TextContent t && !string.IsNullOrEmpty(t.Text) && ThinkTagAny.IsMatch(t.Text))
            {
               
                    return true;
                
            }

            var n = c?.GetType().Name ?? "";
            return n.Contains("Reasoning", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Thinking", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetThinkingTextSafe(this AIContent c)
        {
            if (c is TextContent t && !string.IsNullOrEmpty(t.Text)) return t.Text;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
            var type = c.GetType();

            return type.GetProperty("Text", flags)?.GetValue(c)?.ToString()
                ?? type.GetProperty("Reasoning", flags)?.GetValue(c)?.ToString()
                ?? type.GetProperty("Thinking", flags)?.GetValue(c)?.ToString()
                ?? type.GetProperty("Thoughts", flags)?.GetValue(c)?.ToString()
                ?? string.Empty;
        }

        public static string SanitizeThinking(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var noTags = ThinkTagAny.Replace(s, "");
            return MultiSpace.Replace(noTags, " ");
        }

        

        public static IEnumerable<string> ExtractThoughts(string text)
        {
            var parts = ThinkBlockRegex
                .Matches(text)
                .Select(m => m.Groups["quote"].Value.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (parts.Count == 0) return Enumerable.Empty<string>();

            // Collapse multiple <think>…</think> into a single unified block
            return new[] { string.Join("\n\n", parts) };
        }

        public static List<(string File, int? Page, string Quote)> ExtractCitations(string text)
        {
            var matches = CitationRegex.Matches(text);
            return matches.Count > 0
                ? matches.Select(m => (
                      m.Groups["file"].Value,
                      int.TryParse(m.Groups["page"].Value, out var page) ? page : (int?)null,
                      m.Groups["quote"].Value)).ToList()
                : new();
        }

        public static string StripThinkBlocks(string text) =>
            ThinkStripRegex.Replace(text, string.Empty);


        public sealed class ReasoningAccumulator
        {
            private readonly ChatMessage _hostMessage;
            private readonly StringBuilder _raw = new();
            private TextContent? _thinkContent;
            private bool _opened;

            public ReasoningAccumulator(ChatMessage hostMessage) => _hostMessage = hostMessage;

            public void AppendSanitized(string? delta)
            {
                var cleaned = SanitizeThinking(delta);
                if (string.IsNullOrWhiteSpace(cleaned)) return;

                if (!_opened)
                {
                    _thinkContent = new TextContent("<think>");
                    _hostMessage.Contents.Add(_thinkContent);
                    _opened = true;
                }

                _raw.Append(cleaned);
                _thinkContent!.Text += cleaned;
            }

         
            public bool CloseIfOpen()
            {
                if (_opened && _thinkContent is not null &&
                    !_thinkContent.Text.EndsWith("</think>", StringComparison.OrdinalIgnoreCase))
                {
                    if(_thinkContent.AdditionalProperties == null)
                    {
                        _thinkContent.AdditionalProperties = new();
                    }
                    if (_hostMessage.AdditionalProperties == null)
                    {
                        _hostMessage.AdditionalProperties = new();
                    }
                    _thinkContent.AdditionalProperties["FinishedAt"] = DateTime.UtcNow;
                    _hostMessage.AdditionalProperties["FinishedThinkingAt"] = DateTime.UtcNow;
                    _thinkContent.Text += "</think>";
                    _opened = false;
                    return true;
                }
                return false;
            }
        }


        public static string RemoveThinkTags(this string input)
        {
            // remove all <think>…</think> lines
            var noThink = Regex.Replace(
                input,
                @"(?ims)^[ \t]*<think>.*?</think>[ \t]*\r?\n?",
                string.Empty
            );

       
            return noThink.Trim();
        }

        public static List<ChatMessage> RemoveThinkMessages(this List<ChatMessage> messagesToFilter)
        {
            var filtered = new List<ChatMessage>();

            foreach (var msg in messagesToFilter)
            {
                var clone = msg.Clone();

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
