using Microsoft.Extensions.AI;
using System.Reflection;

namespace AssistantEngine.Services.Extensions
{
    public static class ChatOptionsExtensions
    {
        public static void MergeFrom(this ChatOptions target, ChatOptions source)
        {
            var props = typeof(ChatOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.Name != nameof(ChatOptions.Tools));

            foreach (var prop in props)
            {
                var value = prop.GetValue(source);
                if (value is not null)
                    prop.SetValue(target, value);
            }
        }
    }



}
