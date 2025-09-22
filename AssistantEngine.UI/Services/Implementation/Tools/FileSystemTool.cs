using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Threading.Tasks;

namespace AssistantEngine.Services.Implementation.Tools
{
    public class FileSystemTool : ITool
    {
        [Description("Lists all entries (files + subdirectories) in the given directory.")]
        public async Task<IEnumerable<string>> ListFileEntriesAsync(
            [Description("Absolute or relative path to the directory.")] string directoryPath)
        {
            try
            {
                ChatOptions ch = new ChatOptions(); //ch.sg
                return Directory.EnumerateFileSystemEntries(directoryPath);
            }
            catch (Exception ex)
            {
                return new[]
                {
                    $"<error message=\"{SecurityElement.Escape(ex.Message)}\" type=\"{ex.GetType().Name}\" />"
                };
            }
        }

        [Description("Reads all text from a file.")]
        public async Task<string> ReadFileAsync(
            [Description("Path to the file.")] string filePath)
        {
            try
            {
                return await File.ReadAllTextAsync(filePath);
            }
            catch (Exception ex)
            {
                return $"<error message=\"{SecurityElement.Escape(ex.Message)}\" type=\"{ex.GetType().Name}\" />";
            }
        }

        [Description("Writes text to a file, creating or overwriting it.")]
        public async Task WriteFileAsync(
            [Description("Path to the file.")] string filePath,
            [Description("Content to write.")] string content)
        {
            try
            {
                await File.WriteAllTextAsync(filePath, content);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"<error message=\"{SecurityElement.Escape(ex.Message)}\" type=\"{ex.GetType().Name}\" />"
                );
            }
        }

        [Description("Deletes the specified file.")]
        public Task DeleteFileAsync(
            [Description("Path to the file to delete.")] string filePath)
        {
            try
            {
                File.Delete(filePath);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"<error message=\"{SecurityElement.Escape(ex.Message)}\" type=\"{ex.GetType().Name}\" />"
                );
            }
        }

        [Description("Watches a directory for the given number of seconds and returns any change events.")]
        public Task<IEnumerable<string>> WatchDirectoryAsync(
            [Description("Path to the directory to watch.")] string directoryPath,
            [Description("How many seconds to watch for changes.")] int durationSeconds = 30)
        {
            try
            {
                var tcs = new TaskCompletionSource<IEnumerable<string>>();
                var events = new List<string>();
                var watcher = new FileSystemWatcher(directoryPath)
                {
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                FileSystemEventHandler onChange = (s, e) =>
                    events.Add($"{e.ChangeType}: {e.FullPath}");
                watcher.Created += onChange;
                watcher.Changed += onChange;
                watcher.Deleted += onChange;
                watcher.Renamed += (s, e) =>
                    events.Add($"Renamed: {e.OldFullPath} → {e.FullPath}");

                Task.Delay(TimeSpan.FromSeconds(durationSeconds))
                    .ContinueWith(_ =>
                    {
                        watcher.Dispose();
                        tcs.SetResult(events);
                    });

                return tcs.Task;
            }
            catch (Exception ex)
            {
                return Task.FromResult<IEnumerable<string>>(new[]
                {
                    $"<error message=\"{SecurityElement.Escape(ex.Message)}\" type=\"{ex.GetType().Name}\" />"
                });
            }
        }
    }
}
