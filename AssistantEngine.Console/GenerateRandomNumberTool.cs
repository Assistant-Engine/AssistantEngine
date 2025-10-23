using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


    using AssistantEngine.Factories; // ChatClientState
    using AssistantEngine.UI.Services;
    using AssistantEngine.UI.Services.Options;
    using global::AssistantEngine.Services.Implementation.Tools;
    using Microsoft.Extensions.AI;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    //using AssistantEngine.Services.Abstractions; // where ITool lives
    using Microsoft.Extensions.Logging;
    using System;
    using System.ComponentModel;
    using System.ComponentModel;               // for [Description]
    using System.Security;
    using System.Security;                     // for SecurityElement.Escape
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    namespace AssistantEngine.Services.Implementation.Tools
    {
        /// <summary>
        /// Utility tool that generates random numbers.
        /// </summary>
        public class GenerateRandomNumberTool : ITool
        {
            private readonly ILogger<GenerateRandomNumberTool> _logger;
            private readonly Random _random = new Random();

            public GenerateRandomNumberTool(ILogger<GenerateRandomNumberTool> logger)
            {
                _logger = logger;
            }

            [Description("Generates a random number within the specified range.")]
            public Task<string> GenerateRandomNumberAsync(
                [Description("Minimum value (inclusive).")] int minValue,
                [Description("Maximum value (exclusive).")] int maxValue,
                CancellationToken ct = default)
            {
                try
                {
                    if (maxValue <= minValue)
                        throw new ArgumentException("maxValue must be greater than minValue");

                    int value = _random.Next(minValue, maxValue);
                    _logger.LogInformation("Generated random number: {Value}", value);

                    return Task.FromResult(value.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GenerateAsync failed with range {Min}–{Max}", minValue, maxValue);
                    return Task.FromResult(
                        $"<error message=\"{SecurityElement.Escape(ex.Message)}\" type=\"{ex.GetType().Name}\" />"
                    );
                }
            }

            [Description("Generates a random double between 0.0 and 1.0.")]
            public Task<string> GenerateUnitAsync(CancellationToken ct = default)
            {
                try
                {
                    double value = _random.NextDouble();
                    _logger.LogInformation("Generated random double: {Value}", value);

                    return Task.FromResult(value.ToString("G17"));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GenerateUnitAsync failed");
                    return Task.FromResult(
                        $"<error message=\"{SecurityElement.Escape(ex.Message)}\" type=\"{ex.GetType().Name}\" />"
                    );
                }
            }
        }
    }


