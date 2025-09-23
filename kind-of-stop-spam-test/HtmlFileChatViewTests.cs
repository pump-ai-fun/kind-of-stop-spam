using System;
using System.IO;
using FluentAssertions;
using kind_of_stop_spam;
using Xunit;

namespace KindOfStopSpam.Tests
{
    public class HtmlFileChatViewTests
    {
        [Fact]
        public void Writes_File_And_Appends_Messages()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".html");
            using (var view = new HtmlFileChatView(path, 10, "Test View"))
            {
                view.Add(new ChatMessage { User = "u", Content = "c1", ReceivedAt = DateTimeOffset.UtcNow });
                view.Add(new ChatMessage { User = "v", Content = "c2", ReceivedAt = DateTimeOffset.UtcNow });
            }
            File.Exists(path).Should().BeTrue();
            var html = File.ReadAllText(path);
            html.Should().Contain("Test View");
            html.Should().Contain("c1");
            html.Should().Contain("c2");
            File.Delete(path);
        }
    }
}
