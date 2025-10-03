using System;
using System.IO;
using Modatrix;
using Modatrix.Tests;

namespace Modatrix.Tests
{
    /// <summary>
    /// Utility methods used by tests to run the filter against a test dump file.
    /// Not used in production code.
    /// </summary>
    public static class TestDataRunner
    {
        public static int RunFromFile(string filePath, string? outputHtml = null, ChatFilterConfig? cfg = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException(filePath);

            var filter = new SpamFilter(
                perUserWindow: TimeSpan.FromMilliseconds(1),
                dedupTtl: TimeSpan.FromMinutes(3),
                config: cfg ?? ChatFilterConfig.LoadOrDefault()
            );

            var outPath = outputHtml ?? Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".html");
            using var view = new HtmlFileChatView(filePath: outPath, capacity: 500, title: $"Filtered Chat - TEST: {Path.GetFileName(filePath)}");

            var text = File.ReadAllText(filePath).Replace("\r\n", "\n");
            var parts = text.Split(new[] { "\n\n" }, StringSplitOptions.None);
            int accepted = 0;
            for (int i = 0; i + 2 < parts.Length; i += 3)
            {
                var user = parts[i].Trim();
                var content = parts[i + 1].Trim();
                var time = parts[i + 2].Trim();
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(content))
                    continue;

                var raw = user + "\n\n" + content + "\n\n" + time;
                if (filter.TryAcceptRaw(raw, out var msg))
                {
                    view.Add(msg);
                    accepted++;
                }
            }
            return accepted;
        }
    }
}
