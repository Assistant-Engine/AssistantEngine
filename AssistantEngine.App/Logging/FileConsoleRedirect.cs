using System.Text;

namespace AssistantEngine.App.Logging
{
    public static class FileConsoleRedirect
    {
        public static string Init(string? fileName = null)
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "Logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName ?? "AssistantEngine.log");

            // Mirror Console.Out + Error to the log file
            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var sw = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(sw);
            Console.SetError(sw);

            Console.WriteLine($"=== Start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            Console.WriteLine($"AppDataDirectory: {FileSystem.AppDataDirectory}");
            Console.WriteLine("File logging active.");

            return path;
        }
    }
}
