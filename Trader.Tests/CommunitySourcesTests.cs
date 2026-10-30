using System.ServiceModel.Syndication;
using System.Xml;
using FluentAssertions;
using Trader.Core.Models;
using Trader.Infrastructure.News.Sources;

namespace Trader.Tests;

public class CommunitySourcesTests
{
    [Fact]
    public void MastodonParse_KeepsEnglishPostsAndSkipsShortOrOtherLanguages()
    {
        const string json = """
            [
              {"url":"https://mastodon.social/@a/1","created_at":"2026-10-30T10:00:00.000Z","language":"en","spoiler_text":"","content":"<p>$NVDA looks strong ahead of earnings, buyers are stepping in</p>"},
              {"url":"https://mastodon.social/@a/2","created_at":"2026-10-30T10:00:00.000Z","language":"pt","spoiler_text":"","content":"<p>Bolsa em alta hoje, com forte volume de compra</p>"},
              {"url":"https://mastodon.social/@a/3","created_at":"2026-10-30T10:00:00.000Z","language":"en","spoiler_text":"","content":"<p>gm</p>"}
            ]
            """;

        IReadOnlyList<RawArticle> articles = MastodonConnector.Parse(json);

        articles.Should().ContainSingle();
        articles[0].Title.Should().StartWith("$NVDA looks strong");
        articles[0].SourceType.Should().Be(SourceType.Social);
    }

    [Fact]
    public void LemmyParse_SkipsRemovedAndOldPostsAndPrefersExternalLink()
    {
        const string json = """
            {"posts":[
              {"post":{"name":"Fed signals pause","url":"https://example.com/fed","ap_id":"https://lemmy.world/post/1","published":"2026-10-29T10:00:00Z","body":"Discussion body","removed":false,"deleted":false,"nsfw":false},"community":{"name":"economics"}},
              {"post":{"name":"Removed post","ap_id":"https://lemmy.world/post/2","published":"2026-10-29T10:00:00Z","removed":true,"deleted":false,"nsfw":false},"community":{"name":"economics"}},
              {"post":{"name":"Very old post","ap_id":"https://lemmy.world/post/3","published":"2026-08-01T10:00:00Z","removed":false,"deleted":false,"nsfw":false},"community":{"name":"economics"}},
              {"post":{"name":"Text only post","ap_id":"https://lemmy.world/post/4","published":"2026-10-30T08:00:00Z","removed":false,"deleted":false,"nsfw":false},"community":{"name":"stocks"}}
            ]}
            """;

        IReadOnlyList<RawArticle> articles = LemmyConnector.Parse(json, DateTimeOffset.Parse("2026-10-30T10:00:00Z"));

        articles.Select(a => a.Url).Should().Equal("https://example.com/fed", "https://lemmy.world/post/4");
        articles[0].Source.Should().Be("Lemmy c/economics");
    }

    [Fact]
    public void ApeWisdomParse_ReadsNumbersAndDecodesNames()
    {
        const string json = """{"results":[{"rank":4,"ticker":"INTC","name":"Intel &amp; Co","mentions":202,"upvotes":"1635","rank_24h_ago":29,"mentions_24h_ago":null}]}""";

        ApeWisdomEntry entry = ApeWisdomParser.Parse(json).Single();

        entry.Name.Should().Be("Intel & Co");
        entry.Upvotes.Should().Be(1635);
        entry.MentionsPrevious.Should().BeNull();
    }

    [Fact]
    public void ApeWisdomSpikes_OnlyWatchlistSymbolsWithBigJumps()
    {
        ApeWisdomEntry[] entries =
        [
            new(1, "INTC", "Intel", 202, 1635, 29, 17),
            new(2, "GOOG", "Alphabet", 376, 4992, 1, 494),
            new(3, "AMZN", "Amazon", 20, 100, 16, 5),
            new(4, "RDDT", "Reddit", 329, 20557, 10, 52)
        ];

        IReadOnlyList<RawArticle> spikes = ApeWisdomConnector.Spikes(entries, ["INTC", "GOOG", "AMZN"], new ApeWisdomOptions(), DateTimeOffset.Parse("2026-10-30T10:00:00Z"));

        spikes.Should().ContainSingle();
        spikes[0].Symbols.Should().Equal("INTC");
        spikes[0].Url.Should().EndWith("?day=20261030");
    }

    [Fact]
    public void RedditFeedParse_RemovesSubmissionBoilerplateAndUsesSubreddit()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom"><entry>
              <category term="stocks" label="r/stocks"/>
              <title>Why I like semiconductors</title>
              <link href="https://www.reddit.com/r/stocks/comments/abc/why/"/>
              <updated>2026-10-30T09:00:00+00:00</updated>
              <content type="html">&lt;div class="md"&gt;&lt;p&gt;Demand keeps rising.&lt;/p&gt;&lt;/div&gt; submitted by /u/someone to r/stocks [link] [comments]</content>
            </entry></feed>
            """;

        using XmlReader reader = XmlReader.Create(new StringReader(xml));
        RawArticle article = RedditFeedConnector.Parse(SyndicationFeed.Load(reader)).Single();

        article.Source.Should().Be("r/stocks");
        article.Summary.Should().Be("Demand keeps rising.");
    }
}
