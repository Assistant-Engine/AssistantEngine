using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Notifications
{

    public class EvaluationResponse
    {
        [JsonPropertyName("result")] public string? Result { get; set; } // "pass" | "defer" | "error"
        [JsonPropertyName("state")] public JsonElement? State { get; set; } // optional
        [JsonPropertyName("notify")] public EvaluationNotification? Notify { get; set; } // optional
        [JsonPropertyName("error")] public string? Error { get; set; } // optional
    }

   
    public class EvaluationNotification
    {
            [JsonPropertyName("level")] public string? Level { get; set; } // info|success|warning|error
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("message")] public string? Message { get; set; }
    }

 

}
