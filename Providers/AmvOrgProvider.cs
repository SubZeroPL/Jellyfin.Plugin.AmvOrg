using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AMVOrg.Providers
{
    public class AmvOrgProvider : IRemoteMetadataProvider<MusicVideo, MusicVideoInfo>
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AmvOrgProvider> _logger;

        private const string BaseUrl = "https://www.animemusicvideos.org";
        private const string DirectUrl = "https://www.animemusicvideos.org/members/members_videoinfo.php?v={0}";
        private const string NoResults = "We're sorry, but there were no results for your query";
        private const string NotExists = "Does not exist";

        private const string SearchUrlTitle =
            "https://www.animemusicvideos.org/search/supersearch.php?title={0}&action=Search&go=go#results";

        private const string SearchUrlAnimeArtistSong =
            "https://www.animemusicvideos.org/search/supersearch.php?anime_criteria={0}&artist_criteria={1}&song_criteria={2}&action=Search&go=go#results";

        private class Entry
        {
            public string? Anime;
            public string? Title;
            public string? Artist;
            public string? Song;
        }

        public string Name => AmvOrgConstants.Name;

        public AmvOrgProvider(IHttpClientFactory httpClientFactory, ILogger<AmvOrgProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MusicVideoInfo searchInfo,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("GetSearchResults");
            _logger.LogDebug("Params: Name:{Name}, Path:{Path}", searchInfo.Name, searchInfo.Path);
            var result = new List<RemoteSearchResult>();

            var id = searchInfo.GetProviderId(Name);
            _logger.LogDebug("Saved id:{Id}", id);

            HtmlDocument? doc;

            if (!string.IsNullOrWhiteSpace(id))
            {
                doc = await SearchById(id, cancellationToken);
                if (doc != null)
                {
                    var item = ParsePageToSearchResult(doc);
                    result.Add(item);
                }
            }

            var entry = SplitFilename(searchInfo.Name);
            if (entry == null) return result;
            if (!string.IsNullOrEmpty(entry.Title))
            {
                doc = await SearchByTitle(entry.Title, cancellationToken);
                if (doc != null)
                {
                    var titleResultsList = await GetSearchResultsList(doc, cancellationToken);
                    result.AddRange(titleResultsList.Select(ParsePageToSearchResult));
                }
            }

            doc = await SearchByAnimeArtistSong(entry, cancellationToken);

            if (doc == null) return result;

            var resultsList = await GetSearchResultsList(doc, cancellationToken);
            result.AddRange(resultsList.Select(ParsePageToSearchResult));

            return result;
        }

        public async Task<MetadataResult<MusicVideo>> GetMetadata(MusicVideoInfo info,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("GetMetadata");
            _logger.LogDebug(
                "Params: Name:{Name}, Path:{Path}, Year:{Year}, IndexNumber:{IndexNumber}, ProviderIds{Ids}",
                info.Name, info.Path, info.Year, info.IndexNumber, info.ProviderIds);
            var result = new MetadataResult<MusicVideo>
            {
                HasMetadata = false
            };

            var id = info.GetProviderId(Name);
            _logger.LogDebug("Parsed id:{Id}", id);

            HtmlDocument? doc;
            var entry = SplitFilename(info.Name);

            if (string.IsNullOrWhiteSpace(id))
            {
                if (entry == null) return result;
                if (string.IsNullOrEmpty(entry.Title))
                    doc = await SearchByAnimeArtistSong(entry, cancellationToken);
                else
                    doc = await SearchByTitle(entry.Title, cancellationToken);
                if (doc != null)
                {
                    if (HasMultipleSearchResults(doc)) return result;
                    doc = await GetFirstSearchResult(doc, cancellationToken);
                }
            }
            else
            {
                doc = await SearchById(id, cancellationToken);
            }

            if (doc == null) return result;

            result = ParsePageToMetadata(doc);
            result.Item.Path = info.Path;

            return result;
        }

        private static MetadataResult<MusicVideo> ParsePageToMetadata(HtmlDocument doc)
        {
            var result = new MetadataResult<MusicVideo>();
            var title = doc.DocumentNode.SelectSingleNode("//span[@class='videoTitle']")?.InnerText;
            var author = doc.DocumentNode.SelectNodes("//span[@class='infoTitle']")
                ?.First(n => n.InnerText == "Member:")?.ParentNode?.Descendants("a")?.First()?.InnerText;
            var studio = doc.DocumentNode.SelectSingleNode("//span[@class='videoStudio']")?.InnerText;
            var datePresent =
                DateTime.TryParse(doc.DocumentNode.SelectSingleNode("//span[@class='videoPremiere']")?.InnerText,
                    out var date);
            var comments = doc.DocumentNode.SelectSingleNode("//span[@class='comments']")?.InnerHtml?.Trim();
            var rating = doc.DocumentNode.SelectSingleNode("//ul[@class='opinionValues']")?.Descendants("li")?.Last()
                ?.InnerText;
            var scorePresent = float.TryParse(rating, out var score);
            var categories = doc.DocumentNode.SelectSingleNode("//ul[@class='videoCategory']").Descendants("li")
                .ToList().ConvertAll(n => n.InnerText);
            
            result.Item = new MusicVideo
            {
                Name = title,
                PremiereDate = datePresent ? date : null,
                ProductionYear = datePresent ? date.Year : null,
                CommunityRating = scorePresent ? score : -1,
                Overview = comments
            };
            if (!string.IsNullOrEmpty(studio))
                result.Item.AddStudio(studio);

            foreach (var category in categories)
                result.Item.AddGenre(category);

            if (!string.IsNullOrEmpty(author))
            {
                result.AddPerson(new PersonInfo()
                {
                    Name = author,
                    Type = PersonType.Director
                });
            }
            
            result.HasMetadata = true;

            return result;
        }

        private RemoteSearchResult ParsePageToSearchResult(HtmlDocument doc)
        {
            var title = doc.DocumentNode.SelectSingleNode("//span[@class='videoTitle']")?.InnerText;
            var artist = doc.DocumentNode.SelectSingleNode("//span[@class='artist']").InnerText;
            var datePresent =
                DateTime.TryParse(doc.DocumentNode.SelectSingleNode("//span[@class='videoPremiere']")?.InnerText,
                    out var date);
            var comments = doc.DocumentNode.SelectSingleNode("//span[@class='comments']")?.InnerText?.Trim();
            var id = doc.DocumentNode.SelectSingleNode("//h3/abbr").GetAttributeValue("title", "").Split("/").Last();

            var result = new RemoteSearchResult
            {
                Name = title,
                PremiereDate = datePresent ? date : null,
                ProductionYear = datePresent ? date.Year : null,
                Overview = comments
            };
            var artists = new List<RemoteSearchResult> { new()
            {
                Name = artist
            } };
            result.Artists = artists.ToArray();
            result.AlbumArtist = artists.First();
            result.SetProviderId(Name, id);

            return result;
        }

        private static async Task<HtmlDocument?> SearchById(string id, CancellationToken cancellationToken)
        {
            var web = new HtmlWeb();
            var url = string.Format(DirectUrl, id);
            var doc = await web.LoadFromWebAsync(url, cancellationToken);
            if (doc.DocumentNode.InnerText.Contains(NotExists))
                return null;
            return await web.LoadFromWebAsync(url, cancellationToken);
        }

        private static async Task<HtmlDocument?> SearchByAnimeArtistSong(Entry? entry,
            CancellationToken cancellationToken)
        {
            if (entry == null)
                return null;
            var url = string.Format(SearchUrlAnimeArtistSong, entry.Anime, entry.Artist, entry.Song);
            var searchResults = await GetSearchResultsPage(url, cancellationToken);
            return searchResults ?? null;
        }

        private static async Task<HtmlDocument?> SearchByTitle(string title, CancellationToken cancellationToken)
        {
            var url = string.Format(SearchUrlTitle, UrlEncoder.Default.Encode(title));
            var searchResults = await GetSearchResultsPage(url, cancellationToken);
            return searchResults ?? null;
        }

        private static async Task<HtmlDocument?> GetSearchResultsPage(string url, CancellationToken cancellationToken)
        {
            var web = new HtmlWeb();
            var searchResults = await web.LoadFromWebAsync(url, cancellationToken);
            return searchResults.DocumentNode.InnerText.Contains(NoResults) ? null : searchResults;
        }

        private static bool HasMultipleSearchResults(HtmlDocument searchResults)
        {
            var resultList = searchResults.DocumentNode.SelectNodes(
                "//ul[@class='resultsList']/li[@class='video']/div[@class='identification']/a[@class='title']");
            return resultList.Count > 1;
        }
        
        private static async Task<HtmlDocument?> GetFirstSearchResult(HtmlDocument searchResults,
            CancellationToken cancellationToken)
        {
            var first = await GetSearchResultsList(searchResults, cancellationToken);
            return first.First();
        }

        private static async Task<List<HtmlDocument>> GetSearchResultsList(HtmlDocument searchResults,
            CancellationToken cancellationToken)
        {
            var result = new List<HtmlDocument>();
            var resultList = searchResults.DocumentNode.SelectNodes(
                "//ul[@class='resultsList']/li[@class='video']/div[@class='identification']/a[@class='title']");
            var web = new HtmlWeb();
            foreach (var node in resultList)
            {
                var url = node.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(url)) continue;
                var doc = await web.LoadFromWebAsync(BaseUrl + url, cancellationToken);
                result.Add(doc);
            }

            return result;
        }

        private static Entry? SplitFilename(string filename)
        {
            var entries = filename.Split('-', StringSplitOptions.TrimEntries);
            return entries.Length switch
            {
                5 => new Entry
                {
                    Anime = entries[0].Split(',', StringSplitOptions.TrimEntries)[0],
                    Title = $"{entries[1]} - {entries[2]}", Artist = entries[3], Song = entries[4]
                },
                4 => new Entry { Anime = entries[0], Title = entries[1], Artist = entries[2], Song = entries[3] },
                3 => new Entry { Anime = entries[0], Artist = entries[1], Song = entries[2], Title = null },
                2 => new Entry { Artist = entries[0], Song = entries[1], Anime = null, Title = null },
                1 => new Entry { Title = entries[0], Anime = null, Artist = null, Song = null },
                _ => null
            };
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            _logger.LogDebug("GetImageResponse");
            _logger.LogDebug("Params: url:{Url}", url);
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
        }
    }
}