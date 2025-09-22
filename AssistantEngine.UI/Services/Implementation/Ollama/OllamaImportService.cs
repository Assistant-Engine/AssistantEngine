using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;


namespace AssistantEngine.UI.Services.Implementation.Ollama
{

    public class OllamaImportService
    {
        private readonly IAppConfigStore _config;
        private readonly HttpClient _http;
        private readonly IMemoryCache _cache;

        private const string CacheKey = "ollama_models";
      
        private readonly string _cacheFilePath;
        private static readonly TimeSpan FileTtl = TimeSpan.FromDays(1);
        public OllamaImportService(IAppConfigStore config,HttpClient http, IMemoryCache cache)
        {
            _http = http;
            _cache = cache;
            _config = config;
            _cacheFilePath = Path.Combine(_config.AppDataDirectory, "ollama_models.json");
        }

        public async Task<List<OllamaImportModel>> GetModelsAsync()
        {
            if (_cache.TryGetValue(CacheKey, out List<OllamaImportModel> cachedMem))
                return cachedMem;

            // File cache (fresh)
            if (File.Exists(_cacheFilePath) &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFilePath) <= FileTtl)
            {
                var fromFile = await TryLoadFromFileAsync();
                if (fromFile is { Count: > 0 })
                {
                    _cache.Set(CacheKey, fromFile, FileTtl);
                    return fromFile;
                }
            }

            // Build fresh
            var html = await _http.GetStringAsync("https://ollama.com/library");
            var matches = Regex.Matches(html, "href=\"/library/([^\"]+)");
            var modelNames = matches.Select(m => m.Groups[1].Value)
                                    .Distinct()
                                    .ToList();

            int count = 0, maxModels = 50;
            List<OllamaImportModel> models = new();

            foreach (var modelName in modelNames)
            {
                var model = await FillModel(modelName);
                models.Add(model);
                if (++count == maxModels) break;
            }

            await SaveToFileAsync(models);
            _cache.Set(CacheKey, models, FileTtl);
            return models;
        }
        private async Task<List<OllamaImportModel>?> TryLoadFromFileAsync()
        {
            try
            {
                await using var fs = File.OpenRead(_cacheFilePath);
                return await JsonSerializer.DeserializeAsync<List<OllamaImportModel>>(fs,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return null; }
        }

        private async Task SaveToFileAsync(List<OllamaImportModel> models)
        {
            try
            {
                await using var fs = File.Create(_cacheFilePath);
                await JsonSerializer.SerializeAsync(fs, models);
            }
            catch { /* swallow: caching shouldn't break the page */ }
        }

        private async Task<List<string>> GetTagsAsync(string model)
        {
            var html = await _http.GetStringAsync($"https://ollama.com/library/{model}/tags");
            var matches = Regex.Matches(html, $"{model}:([^\" ]*q[^\" ]*)");
            return matches.Select(m => $"{model}:{m.Groups[1].Value}")
                          .Where(tag => !Regex.IsMatch(tag, "text|base|fp|q[45]_[01]"))
                          .Distinct()
                          .ToList();
        }

        private async Task<OllamaImportModel> FillModel(string model)
        {
            var html = await _http.GetStringAsync($"https://ollama.com/library/{model}/tags");

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var returnVal = new OllamaImportModel
            {
                Name = model,
                Dropdowns = doc.DocumentNode
                    .SelectNodes("//div[contains(@class,'divide-gray-200')]//div[contains(@class,'group')]")
                    ?.Select(node => node.OuterHtml.Trim())
                    .Distinct()
                    .ToList() ?? new List<string>(),
                Description = doc.GetElementbyId("summary-content")?.InnerHtml.Trim() ?? ""
            };

            return returnVal;
        }

    }
    //can we not in
    public class OllamaImportModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<string> Dropdowns { get; set; } = new(); public bool IsExpanded { get; set; } // for toggle
    }
}
