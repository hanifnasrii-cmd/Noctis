using System.Text.Json;
using Noctis.Services;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The About-the-artist pipeline's pure parsers against MusicBrainz / Wikidata /
/// Wikipedia response shapes: only an exact (or very high scoring) search hit is
/// trusted, lookup fields map to display facts, and disambiguation pages never
/// become a biography.
/// </summary>
public class ArtistInfoServiceTests
{
    private const string SearchJson = """
    {"artists":[
      {"id":"bad-bunny-id","score":100,"name":"Bad Bunny","type":"Person"},
      {"id":"other-id","score":72,"name":"Bad Bunny Tribute"}
    ]}
    """;

    private const string LookupJson = """
    {
      "id":"bad-bunny-id","name":"Bad Bunny","type":"Person","gender":"male","country":"PR",
      "area":{"name":"Puerto Rico"},"begin-area":{"name":"Vega Baja"},
      "life-span":{"begin":"1994-03-10","ended":false},
      "genres":[{"name":"reggaeton","count":12},{"name":"latin trap","count":9},{"name":"pop","count":1},{"name":"rap","count":3},{"name":"dembow","count":4}],
      "relations":[
        {"type":"wikidata","url":{"resource":"https://www.wikidata.org/wiki/Q29027587"}},
        {"type":"social network","url":{"resource":"https://www.instagram.com/badbunnypr/"}},
        {"type":"streaming","url":{"resource":"https://www.deezer.com/artist/10583405"}},
        {"type":"streaming","url":{"resource":"https://open.spotify.com/artist/4q3ewBCX7sLwd24euuV69X"}},
        {"type":"streaming","url":{"resource":"https://music.apple.com/us/artist/bad-bunny/1126808565"}},
        {"type":"youtube","url":{"resource":"https://www.youtube.com/channel/UCmBA_wu8xGg1OfOkfW13Q0Q"}},
        {"type":"social network","url":{"resource":"https://twitter.com/sanbenito"},"ended":true},
        {"type":"social network","url":{"resource":"https://www.facebook.com/BadBunnyPR"}},
        {"type":"official homepage","url":{"resource":"https://www.badbunnypr.com"}}
      ]
    }
    """;

    [Fact]
    public void ParseLookup_BuildsTheLinkRow_KnownServicesInDisplayOrder()
    {
        using var doc = JsonDocument.Parse(LookupJson);
        var info = ArtistInfoService.ParseLookup(doc.RootElement, "Bad Bunny");

        // Spotify, Apple Music, YouTube, Instagram, site — Deezer and Facebook are not
        // offered, and the ended Twitter relation is a dead account.
        Assert.Equal(new[] { "spotify", "applemusic", "youtube", "instagram", "website" }, info.Links.Select(l => l.Kind));
        Assert.Equal("SpotifyIcon", info.Links[0].IconKey);
        Assert.Equal("Apple Music", info.Links[1].Label);
        Assert.Equal("https://www.badbunnypr.com", info.Links[^1].Url);
        Assert.True(info.HasLinks);
        Assert.Equal("Reggaeton · Latin trap · Dembow · Rap", info.GenresDotted);
        Assert.Equal("1994", info.BeginYear);
    }

    [Theory]
    [InlineData("streaming", "https://open.spotify.com/artist/x", "spotify")]
    [InlineData("free streaming", "https://music.youtube.com/channel/x", "youtube")]
    [InlineData("soundcloud", "https://soundcloud.com/x", "soundcloud")]
    [InlineData("bandcamp", "https://artist.bandcamp.com/", "bandcamp")]
    [InlineData("social network", "https://x.com/artist", "x")]
    [InlineData("official homepage", "https://example.com", "website")]
    [InlineData("discogs", "https://www.discogs.com/artist/1", null)]
    [InlineData("official homepage", "ftp://example.com", null)]
    public void ClassifyUrl_JudgesTheHostNotTheRelationType(string type, string url, string? expected)
        => Assert.Equal(expected, ArtistLink.ClassifyUrl(type, url));

    [Fact]
    public void Cache_RefetchesAHitWrittenBeforeLinksExisted()
    {
        var old = new ArtistInfo { Found = true, FetchedAtUtc = DateTime.UtcNow, Schema = 1 };
        Assert.True(ArtistInfoService.IsStale(old));
        var current = new ArtistInfo { Found = true, FetchedAtUtc = DateTime.UtcNow, Schema = ArtistInfoService.CurrentSchema };
        Assert.False(ArtistInfoService.IsStale(current));
        // A miss from an older pipeline retries too: the newer one may find the artist.
        var miss = new ArtistInfo { Found = false, FetchedAtUtc = DateTime.UtcNow, Schema = 0 };
        Assert.True(ArtistInfoService.IsStale(miss));
        var currentMiss = new ArtistInfo { Found = false, FetchedAtUtc = DateTime.UtcNow, Schema = ArtistInfoService.CurrentSchema };
        Assert.False(ArtistInfoService.IsStale(currentMiss));
    }

    [Fact]
    public void WikidataNameSearch_AcceptsExactLabelsOnly_AndNeedsAMusicBrainzClaim()
    {
        // wbsearchentities: the band, its discography page, and an alias-only match.
        using var search = JsonDocument.Parse("""
        {"search":[
          {"id":"Q46537070","label":"Chase Atlantic","match":{"type":"label","text":"Chase Atlantic"}},
          {"id":"Q999","label":"Chase Atlantic discography","match":{"type":"label","text":"Chase Atlantic discography"}},
          {"id":"Q777","label":"CA (band)","match":{"type":"alias","text":"chase atlantic"}}
        ]}
        """);
        var ids = ArtistInfoService.ParseWikidataCandidates(search.RootElement, "Chase Atlantic");
        Assert.Equal(new[] { "Q46537070", "Q777" }, ids);

        // wbgetentities: only the first carries P434.
        using var entities = JsonDocument.Parse("""
        {"entities":{
          "Q46537070":{"claims":{"P434":[{"mainsnak":{"datavalue":{"value":"ecab255c-3af3-4154-87ec-2685041ce297"}}}]}},
          "Q777":{"claims":{"P31":[{"mainsnak":{"datavalue":{"value":"x"}}}]}}
        }}
        """);
        Assert.Equal("ecab255c-3af3-4154-87ec-2685041ce297", ArtistInfoService.ParseMusicBrainzClaim(entities.RootElement, "Q46537070"));
        Assert.Null(ArtistInfoService.ParseMusicBrainzClaim(entities.RootElement, "Q777"));
        Assert.Null(ArtistInfoService.ParseMusicBrainzClaim(entities.RootElement, "Q1"));
    }

    [Theory]
    [InlineData("https://cdn-images.dzcdn.net/images/artist/77799269d54de23d983a2af3ca33a322/1000x1000-000000-80-0-0.jpg",
                "https://cdn-images.dzcdn.net/images/artist/77799269d54de23d983a2af3ca33a322/1800x1800-000000-80-0-0.jpg")]
    [InlineData("https://e-cdns-images.dzcdn.net/images/artist/abc/500x500-000000-80-0-0.jpg",
                "https://e-cdns-images.dzcdn.net/images/artist/abc/500x500-000000-80-0-0.jpg")]
    [InlineData("https://example.com/photo/1000x1000-x.jpg", "https://example.com/photo/1000x1000-x.jpg")]
    public void DeezerPortrait_IsRequestedAt1800px(string url, string expected)
        => Assert.Equal(expected, ArtistImageService.UpgradeDeezerSize(url));

    [Fact]
    public void ParseWikidataSearch_ReadsTheItemFoundByMusicBrainzId()
    {
        using var doc = JsonDocument.Parse("""{"query":{"searchinfo":{"totalhits":1},"search":[{"ns":0,"title":"Q46537070","pageid":47662410}]}}""");
        Assert.Equal("Q46537070", ArtistInfoService.ParseWikidataSearch(doc.RootElement));
        using var none = JsonDocument.Parse("""{"query":{"searchinfo":{"totalhits":0},"search":[]}}""");
        Assert.Null(ArtistInfoService.ParseWikidataSearch(none.RootElement));
    }

    [Fact]
    public void PickBestMatch_TakesTheExactNameHit()
    {
        using var doc = JsonDocument.Parse(SearchJson);
        Assert.Equal("bad-bunny-id", ArtistInfoService.PickBestMatch(doc.RootElement, "bad bunny"));
    }

    [Fact]
    public void PickBestMatch_RefusesALooseTopHit()
    {
        using var doc = JsonDocument.Parse("""{"artists":[{"id":"x","score":88,"name":"Bunny Wailer"}]}""");
        Assert.Null(ArtistInfoService.PickBestMatch(doc.RootElement, "Bad Bunny"));
    }

    [Fact]
    public void PickBestMatch_AcceptsAnAliasOrANearCertainTopHit()
    {
        using var alias = JsonDocument.Parse("""{"artists":[{"id":"a","score":90,"name":"Benito Antonio Martínez Ocasio","aliases":[{"name":"Bad Bunny"}]}]}""");
        Assert.Equal("a", ArtistInfoService.PickBestMatch(alias.RootElement, "Bad Bunny"));
        using var top = JsonDocument.Parse("""{"artists":[{"id":"b","score":97,"name":"BAD BUNNY (PR)"}]}""");
        Assert.Equal("b", ArtistInfoService.PickBestMatch(top.RootElement, "Bad Bunny"));
    }

    [Fact]
    public void ParseLookup_MapsIdentityOriginDatesGenresAndLinks()
    {
        using var doc = JsonDocument.Parse(LookupJson);
        var info = ArtistInfoService.ParseLookup(doc.RootElement, "Bad Bunny");

        Assert.Equal("Vega Baja, Puerto Rico", info.FromDisplay);
        Assert.Equal("BORN", info.BeginLabel);
        Assert.Equal("March 10, 1994", info.BeginDisplay);
        Assert.Equal("1994 – present", info.ActiveDisplay);
        Assert.Equal("Solo artist · Male", info.TypeDisplay);
        // Top four by vote count, most-voted first.
        Assert.Equal(new[] { "reggaeton", "latin trap", "dembow", "rap" }, info.Genres);
        Assert.Equal("Q29027587", info.WikidataId);
        Assert.Equal("https://www.badbunnypr.com", info.WebsiteUrl);
        Assert.Equal("https://musicbrainz.org/artist/bad-bunny-id", info.MusicBrainzUrl);
        Assert.False(info.HasBio);
    }

    [Fact]
    public void ParseLookup_SurvivesMusicBrainzNulls()
    {
        // Chase Atlantic's real record: "begin-area": null, no relations, tags only.
        using var doc = JsonDocument.Parse("""
        {"id":"ecab255c","name":"Chase Atlantic","type":"Group","country":"AU","begin-area":null,"area":{"name":"Australia"},
         "life-span":{"begin":"2014","ended":null},"genres":[],"tags":[{"name":"pop rap","count":2},null],"relations":null}
        """);
        var info = ArtistInfoService.ParseLookup(doc.RootElement, "Chase Atlantic");
        Assert.Equal("Australia", info.FromDisplay);
        Assert.Equal("2014", info.BeginYear);
        Assert.Equal(new[] { "pop rap" }, info.Genres);
        Assert.Empty(info.Links);
    }

    [Fact]
    public void WikidataFacts_FillAPartialEntry_PersonAndGroup()
    {
        // Bad Bunny's real claims (trimmed): human, born San Juan 1994-03-10, reggaeton, Puerto Rico.
        using var person = JsonDocument.Parse("""
        {"entities":{"Q44333953":{"claims":{
          "P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q5"}}}}],
          "P19":[{"mainsnak":{"datavalue":{"value":{"id":"Q739675"}}}}],
          "P569":[{"mainsnak":{"datavalue":{"value":{"time":"+1994-03-10T00:00:00Z","precision":11}}}}],
          "P136":[{"mainsnak":{"datavalue":{"value":{"id":"Q54329757"}}}},{"mainsnak":{"datavalue":{"value":{"id":"Q11401"}}}}],
          "P27":[{"mainsnak":{"datavalue":{"value":{"id":"Q1183"}}}}]
        }}}}
        """);
        var facts = ArtistInfoService.ParseWikidataFacts(person.RootElement, "Q44333953")!;
        Assert.True(facts.IsPerson);
        Assert.Equal("1994-03-10", facts.Begin);
        Assert.Equal(new[] { "Q739675", "Q1183", "Q54329757", "Q11401" }, facts.ItemIds);

        using var labelsDoc = JsonDocument.Parse("""
        {"entities":{"Q739675":{"labels":{"en":{"value":"San Juan"}}},"Q1183":{"labels":{"en":{"value":"Puerto Rico"}}},
                     "Q54329757":{"labels":{"en":{"value":"Latin trap"}}},"Q11401":{"labels":{"en":{"value":"reggaeton"}}}}}
        """);
        var labels = ArtistInfoService.ParseWikidataLabels(labelsDoc.RootElement);
        var info = new ArtistInfo { Name = "Bad Bunny", Partial = true };
        ArtistInfoService.ApplyWikidataFacts(info, facts, labels);
        Assert.Equal("San Juan, Puerto Rico", info.FromDisplay);
        Assert.Equal("BORN", info.BeginLabel);
        Assert.Equal("March 10, 1994", info.BeginDisplay);
        Assert.Equal("Latin trap · Reggaeton", info.GenresDotted);

        // Chase Atlantic: a group with inception at year precision and a country of origin.
        using var group = JsonDocument.Parse("""
        {"entities":{"Q46537070":{"claims":{
          "P31":[{"mainsnak":{"datavalue":{"value":{"id":"Q215380"}}}}],
          "P571":[{"mainsnak":{"datavalue":{"value":{"time":"+2011-01-01T00:00:00Z","precision":9}}}}],
          "P495":[{"mainsnak":{"datavalue":{"value":{"id":"Q408"}}}}]
        }}}}
        """);
        var gf = ArtistInfoService.ParseWikidataFacts(group.RootElement, "Q46537070")!;
        Assert.False(gf.IsPerson);
        Assert.Equal("2011", gf.Begin);
        var ginfo = new ArtistInfo { Name = "Chase Atlantic", Partial = true };
        ArtistInfoService.ApplyWikidataFacts(ginfo, gf, new Dictionary<string, string> { ["Q408"] = "Australia" });
        Assert.Equal("Australia", ginfo.FromDisplay);
        Assert.Equal("FORMED", ginfo.BeginLabel);
        Assert.Equal("2011", ginfo.BeginDisplay);
        Assert.Null(ArtistInfoService.ParseWikidataFacts(group.RootElement, "Q1"));
    }

    [Fact]
    public void Cache_PartialHitRetriesOnTheMissClock()
    {
        var partial = new ArtistInfo { Found = true, Partial = true, Schema = ArtistInfoService.CurrentSchema, FetchedAtUtc = DateTime.UtcNow.AddDays(-2) };
        Assert.True(ArtistInfoService.IsStale(partial));
        var full = new ArtistInfo { Found = true, Schema = ArtistInfoService.CurrentSchema, FetchedAtUtc = DateTime.UtcNow.AddDays(-2) };
        Assert.False(ArtistInfoService.IsStale(full));
    }

    [Fact]
    public void ParseLookup_GroupUsesFormedAndDisbanded()
    {
        using var doc = JsonDocument.Parse("""
        {"id":"g","name":"The Beatles","type":"Group","country":"GB","begin-area":{"name":"Liverpool"},
         "life-span":{"begin":"1960","end":"1970-04-10","ended":true},"tags":[{"name":"rock","count":40}]}
        """);
        var info = ArtistInfoService.ParseLookup(doc.RootElement, "The Beatles");
        Assert.True(info.IsGroup);
        Assert.Equal("FORMED", info.BeginLabel);
        Assert.Equal("1960", info.BeginDisplay);
        Assert.Equal("DISBANDED", info.EndLabel);
        Assert.Equal("April 10, 1970", info.EndDisplay);
        Assert.Equal("1960 – 1970", info.ActiveDisplay);
        Assert.Equal("Liverpool, United Kingdom", info.FromDisplay);
        Assert.Equal(new[] { "rock" }, info.Genres); // tags are the fallback when no genres
    }

    [Fact]
    public void ParseWikidataTitle_ReadsTheEnglishSitelink()
    {
        using var doc = JsonDocument.Parse("""{"entities":{"Q1":{"sitelinks":{"enwiki":{"title":"Bad Bunny"}}}}}""");
        Assert.Equal("Bad Bunny", ArtistInfoService.ParseWikidataTitle(doc.RootElement, "Q1"));
        Assert.Null(ArtistInfoService.ParseWikidataTitle(doc.RootElement, "Q2"));
    }

    [Fact]
    public void ApplyWikipediaSummary_UsesStandardArticlesOnly()
    {
        var info = new ArtistInfo();
        using var disambig = JsonDocument.Parse("""{"type":"disambiguation","extract":"Bunny may refer to:"}""");
        ArtistInfoService.ApplyWikipediaSummary(disambig.RootElement, info);
        Assert.False(info.HasBio);

        using var article = JsonDocument.Parse("""
        {"type":"standard","description":"Puerto Rican rapper and singer","extract":"Benito … is a Puerto Rican rapper.",
         "content_urls":{"desktop":{"page":"https://en.wikipedia.org/wiki/Bad_Bunny"}}}
        """);
        ArtistInfoService.ApplyWikipediaSummary(article.RootElement, info);
        Assert.True(info.HasBio);
        Assert.Equal("Wikipedia", info.BioSource);
        Assert.Equal("Puerto Rican rapper and singer", info.ShortDescription);
        Assert.Equal("https://en.wikipedia.org/wiki/Bad_Bunny", info.WikipediaUrl);
    }

    [Theory]
    [InlineData("1994-03-10", "March 10, 1994")]
    [InlineData("1994-03", "March 1994")]
    [InlineData("1994", "1994")]
    [InlineData("", "")]
    public void FormatPartialDate_HandlesEveryMusicBrainzPrecision(string iso, string expected)
        => Assert.Equal(expected, ArtistInfo.FormatPartialDate(iso));
}
