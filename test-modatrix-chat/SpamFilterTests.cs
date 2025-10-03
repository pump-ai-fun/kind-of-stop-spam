using System;
using System.IO;
using FluentAssertions;
using Modatrix;
using Xunit;

namespace Modatrix.Tests
{
    public class SpamFilterTests
    {
        [Fact]
        public void Accepts_NonBanned_NonDuplicate_Messages()
        {
            var cfg = new ChatFilterConfig { BannedKeywords = new() { "bad" }, BannedMentions = new() { "@x" } };
            cfg.Normalize();
            var f = new SpamFilter(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5), cfg);

            var raw = "alice\n\nHello world!\n\n12:00";
            f.TryAcceptRaw(raw, out var kept).Should().BeTrue();
            kept.User.Should().Be("alice");
            kept.Content.Should().Be("Hello world!");
        }

        [Fact]
        public void Filters_Banned_Keyword()
        {
            var cfg = new ChatFilterConfig { BannedKeywords = new() { "rug" } };
            cfg.Normalize();
            var f = new SpamFilter(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5), cfg);

            var raw = "bob\n\nThis will rug you\n\n12:01";
            f.TryAcceptRaw(raw, out _).Should().BeFalse();
        }

        [Fact]
        public void Dedupes_On_Content_Text()
        {
            var f = new SpamFilter(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(60));
            f.TryAcceptRaw("u\n\nSame text\n\n10:00", out _).Should().BeTrue();
            f.TryAcceptRaw("v\n\nSame   text\n\n10:01", out _).Should().BeFalse();
        }

        [Fact]
        public void RateLimits_Per_User()
        {
            var f = new SpamFilter(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));
            f.TryAcceptRaw("u\n\nmsg1\n\n10:00", out _).Should().BeTrue();
            f.TryAcceptRaw("u\n\nmsg2\n\n10:00", out _).Should().BeFalse();
        }

        [Fact]
        public void Can_Process_Test_Data_File()
        {
            var testFile = Path.Combine(AppContext.BaseDirectory, "test-data.txt");
            File.Exists(testFile).Should().BeTrue("test-data.txt should be copied to test output");

            var cfg = new ChatFilterConfig
            {
                BannedMentions = new() { "@rugdefs", "@rugdbs" }
            };
            cfg.Normalize();

            int accepted = TestDataRunner.RunFromFile(testFile, outputHtml: null, cfg: cfg);
            accepted.Should().BeGreaterThan(0);
        }
    }
}
