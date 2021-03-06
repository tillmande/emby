using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using MediaBrowser.Controller.IO;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Xml;

namespace MediaBrowser.Providers.TV
{

    /// <summary>
    /// Class RemoteEpisodeProvider
    /// </summary>
    class TvdbEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private static readonly string FullIdKey = MetadataProviders.Tvdb + "-Full";

        internal static TvdbEpisodeProvider Current;
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _config;
        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IXmlReaderSettingsFactory _xmlSettings;

        public TvdbEpisodeProvider(IFileSystem fileSystem, IServerConfigurationManager config, IHttpClient httpClient, ILogger logger, IXmlReaderSettingsFactory xmlSettings)
        {
            _fileSystem = fileSystem;
            _config = config;
            _httpClient = httpClient;
            _logger = logger;
            _xmlSettings = xmlSettings;
            Current = this;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var list = new List<RemoteSearchResult>();

            // The search query must either provide an episode number or date
            if (!searchInfo.IndexNumber.HasValue && !searchInfo.PremiereDate.HasValue)
            {
                return Task.FromResult((IEnumerable<RemoteSearchResult>)list);
            }

            if (TvdbSeriesProvider.IsValidSeries(searchInfo.SeriesProviderIds))
            {
                var seriesDataPath = TvdbSeriesProvider.GetSeriesDataPath(_config.ApplicationPaths, searchInfo.SeriesProviderIds);

                try
                {
                    var metadataResult = FetchEpisodeData(searchInfo, seriesDataPath, cancellationToken);

                    if (metadataResult.HasMetadata)
                    {
                        var item = metadataResult.Item;

                        list.Add(new RemoteSearchResult
                        {
                            IndexNumber = item.IndexNumber,
                            Name = item.Name,
                            ParentIndexNumber = item.ParentIndexNumber,
                            PremiereDate = item.PremiereDate,
                            ProductionYear = item.ProductionYear,
                            ProviderIds = item.ProviderIds,
                            SearchProviderName = Name,
                            IndexNumberEnd = item.IndexNumberEnd
                        });
                    }
                }
                catch (FileNotFoundException)
                {
                    // Don't fail the provider because this will just keep on going and going.
                }
                catch (IOException)
                {
                    // Don't fail the provider because this will just keep on going and going.
                }
            }

            return Task.FromResult((IEnumerable<RemoteSearchResult>)list);
        }

        public string Name
        {
            get { return "TheTVDB"; }
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();
            result.QueriedById = true;

            if (TvdbSeriesProvider.IsValidSeries(searchInfo.SeriesProviderIds) &&
                (searchInfo.IndexNumber.HasValue || searchInfo.PremiereDate.HasValue))
            {
                var seriesDataPath = await TvdbSeriesProvider.Current.EnsureSeriesInfo(searchInfo.SeriesProviderIds, null, null, searchInfo.MetadataLanguage, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrEmpty(seriesDataPath))
                {
                    return result;
                }

                try
                {
                    result = FetchEpisodeData(searchInfo, seriesDataPath, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    // Don't fail the provider because this will just keep on going and going.
                }
                catch (IOException)
                {
                    // Don't fail the provider because this will just keep on going and going.
                }
            }
            else
            {
                _logger.Debug("No series identity found for {0}", searchInfo.Name);
            }

            return result;
        }

        /// <summary>
        /// Gets the episode XML files.
        /// </summary>
        /// <param name="seriesDataPath">The series data path.</param>
        /// <param name="searchInfo">The search information.</param>
        /// <returns>List{FileInfo}.</returns>
		internal List<XmlReader> GetEpisodeXmlNodes(string seriesDataPath, EpisodeInfo searchInfo)
        {
            var seriesXmlPath = TvdbSeriesProvider.Current.GetSeriesXmlPath(searchInfo.SeriesProviderIds, searchInfo.MetadataLanguage);

            try
            {
                return GetXmlNodes(seriesXmlPath, searchInfo);
            }
            catch (FileNotFoundException)
            {
                return new List<XmlReader>();
            }
            catch (IOException)
            {
                return new List<XmlReader>();
            }
        }

        /// <summary>
        /// Fetches the episode data.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <param name="searchNumbers">The search numbers.</param>
        /// <param name="seriesDataPath">The series data path.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{System.Boolean}.</returns>
        private MetadataResult<Episode> FetchEpisodeData(EpisodeInfo id, string seriesDataPath, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>()
            {
                Item = new Episode
                {
                    IndexNumber = id.IndexNumber,
                    ParentIndexNumber = id.ParentIndexNumber,
                    IndexNumberEnd = id.IndexNumberEnd
                }
            };

            var xmlNodes = GetEpisodeXmlNodes(seriesDataPath, id);

            if (xmlNodes.Count > 0)
            {
                FetchMainEpisodeInfo(result, xmlNodes[0], id.SeriesDisplayOrder, cancellationToken);

                result.HasMetadata = true;
            }

            foreach (var node in xmlNodes.Skip(1))
            {
                FetchAdditionalPartInfo(result, node, cancellationToken);
            }

            return result;
        }

        private List<XmlReader> GetXmlNodes(string xmlFile, EpisodeInfo searchInfo)
        {
            var list = new List<XmlReader>();

            if (searchInfo.IndexNumber.HasValue)
            {
                var files = GetEpisodeXmlFiles(searchInfo.SeriesDisplayOrder, searchInfo.ParentIndexNumber, searchInfo.IndexNumber, searchInfo.IndexNumberEnd, _fileSystem.GetDirectoryName(xmlFile));

                list = files.Select(GetXmlReader).ToList();
            }

            if (list.Count == 0 && searchInfo.PremiereDate.HasValue)
            {
                list = GetXmlNodesByPremiereDate(xmlFile, searchInfo.PremiereDate.Value);
            }

            return list;
        }

        private string GetEpisodeFileName(string seriesDisplayOrder, int? seasonNumber, int? episodeNumber)
        {
            if (string.Equals(seriesDisplayOrder, "absolute", StringComparison.OrdinalIgnoreCase))
            {
                return string.Format("episode-abs-{0}.xml", episodeNumber);
            }
            else if (string.Equals(seriesDisplayOrder, "dvd", StringComparison.OrdinalIgnoreCase))
            {
                return string.Format("episode-dvd-{0}-{1}.xml", seasonNumber.Value, episodeNumber);
            }
            else
            {
                return string.Format("episode-{0}-{1}.xml", seasonNumber.Value, episodeNumber);
            }
        }

        private FileSystemMetadata GetEpisodeFileInfoWithFallback(string seriesDataPath, string seriesDisplayOrder, int? seasonNumber, int? episodeNumber)
        {
            var file = Path.Combine(seriesDataPath, GetEpisodeFileName(seriesDisplayOrder, seasonNumber, episodeNumber));
            var fileInfo = _fileSystem.GetFileInfo(file);

            if (fileInfo.Exists)
            {
                return fileInfo;
            }

            if (!seasonNumber.HasValue)
            {
                return fileInfo;
            }

            // revert to aired order
            if (string.Equals(seriesDisplayOrder, "absolute", StringComparison.OrdinalIgnoreCase) || string.Equals(seriesDisplayOrder, "dvd", StringComparison.OrdinalIgnoreCase))
            {
                file = Path.Combine(seriesDataPath, GetEpisodeFileName(null, seasonNumber, episodeNumber));
                return _fileSystem.GetFileInfo(file);
            }

            return fileInfo;
        }

        private List<FileSystemMetadata> GetEpisodeXmlFiles(string seriesDisplayOrder, int? seasonNumber, int? episodeNumber, int? endingEpisodeNumber, string seriesDataPath)
        {
            var files = new List<FileSystemMetadata>();

            if (episodeNumber == null)
            {
                return files;
            }

            if (!seasonNumber.HasValue)
            {
                seriesDisplayOrder = "absolute";
            }

            var fileInfo = GetEpisodeFileInfoWithFallback(seriesDataPath, seriesDisplayOrder, seasonNumber, episodeNumber);

            if (fileInfo.Exists)
            {
                files.Add(fileInfo);
            }

            var end = endingEpisodeNumber ?? episodeNumber;
            episodeNumber++;

            while (episodeNumber <= end)
            {
                fileInfo = GetEpisodeFileInfoWithFallback(seriesDataPath, seriesDisplayOrder, seasonNumber, episodeNumber);

                if (fileInfo.Exists)
                {
                    files.Add(fileInfo);
                }
                else
                {
                    break;
                }

                episodeNumber++;
            }

            return files;
        }

        private XmlReader GetXmlReader(FileSystemMetadata xmlFile)
        {
            return GetXmlReader(_fileSystem.ReadAllText(xmlFile.FullName, Encoding.UTF8));
        }

        private XmlReader GetXmlReader(String xml)
        {
            var streamReader = new StringReader(xml);

            var settings = _xmlSettings.Create(false);

            settings.CheckCharacters = false;
            settings.IgnoreProcessingInstructions = true;
            settings.IgnoreComments = true;

            return XmlReader.Create(streamReader, settings);
        }

        private List<XmlReader> GetXmlNodesByPremiereDate(string xmlFile, DateTime premiereDate)
        {
            var list = new List<XmlReader>();

            using (var fileStream = _fileSystem.GetFileStream(xmlFile, FileOpenMode.Open, FileAccessMode.Read, FileShareMode.Read))
            {
                using (var streamReader = new StreamReader(fileStream, Encoding.UTF8))
                {
                    // Use XmlReader for best performance

                    var settings = _xmlSettings.Create(false);

                    settings.CheckCharacters = false;
                    settings.IgnoreProcessingInstructions = true;
                    settings.IgnoreComments = true;

                    using (var reader = XmlReader.Create(streamReader, settings))
                    {
                        reader.MoveToContent();
                        reader.Read();

                        // Loop through each element
                        while (!reader.EOF && reader.ReadState == ReadState.Interactive)
                        {
                            if (reader.NodeType == XmlNodeType.Element)
                            {
                                switch (reader.Name)
                                {
                                    case "Episode":
                                        {
                                            var outerXml = reader.ReadOuterXml();

                                            var airDate = GetEpisodeAirDate(outerXml);

                                            if (airDate.HasValue && premiereDate.Date == airDate.Value.Date)
                                            {
                                                list.Add(GetXmlReader(outerXml));
                                                return list;
                                            }

                                            break;
                                        }

                                    default:
                                        reader.Skip();
                                        break;
                                }
                            }
                            else
                            {
                                reader.Read();
                            }
                        }
                    }
                }
            }

            return list;
        }

        private DateTime? GetEpisodeAirDate(string xml)
        {
            using (var streamReader = new StringReader(xml))
            {
                var settings = _xmlSettings.Create(false);

                settings.CheckCharacters = false;
                settings.IgnoreProcessingInstructions = true;
                settings.IgnoreComments = true;

                // Use XmlReader for best performance
                using (var reader = XmlReader.Create(streamReader, settings))
                {
                    reader.MoveToContent();
                    reader.Read();

                    // Loop through each element
                    while (!reader.EOF && reader.ReadState == ReadState.Interactive)
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            switch (reader.Name)
                            {
                                case "FirstAired":
                                    {
                                        var val = reader.ReadElementContentAsString();

                                        if (!string.IsNullOrWhiteSpace(val))
                                        {
                                            DateTime date;
                                            if (DateTime.TryParse(val, out date))
                                            {
                                                date = date.ToUniversalTime();

                                                return date;
                                            }
                                        }

                                        break;
                                    }

                                default:
                                    reader.Skip();
                                    break;
                            }
                        }
                        else
                        {
                            reader.Read();
                        }
                    }
                }
            }
            return null;
        }

        private readonly CultureInfo _usCulture = new CultureInfo("en-US");

        private void FetchMainEpisodeInfo(MetadataResult<Episode> result, XmlReader reader, string seriesOrder, CancellationToken cancellationToken)
        {
            var item = result.Item;

            int? episodeNumber = null;
            int? seasonNumber = null;
            int? combinedEpisodeNumber = null;
            int? combinedSeasonNumber = null;

            // Use XmlReader for best performance
            using (reader)
            {
                result.ResetPeople();

                reader.MoveToContent();
                reader.Read();

                // Loop through each element
                while (!reader.EOF && reader.ReadState == ReadState.Interactive)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.Name)
                        {
                            case "id":
                                {
                                    var val = reader.ReadElementContentAsString();
                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        item.SetProviderId(MetadataProviders.Tvdb, val);
                                    }
                                    break;
                                }

                            case "IMDB_ID":
                                {
                                    var val = reader.ReadElementContentAsString();
                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        item.SetProviderId(MetadataProviders.Imdb, val);
                                    }
                                    break;
                                }

                            case "EpisodeNumber":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        int rval;

                                        // int.TryParse is local aware, so it can be probamatic, force us culture
                                        if (int.TryParse(val, NumberStyles.Integer, _usCulture, out rval))
                                        {
                                            episodeNumber = rval;
                                        }
                                    }

                                    break;
                                }

                            case "SeasonNumber":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        int rval;

                                        // int.TryParse is local aware, so it can be probamatic, force us culture
                                        if (int.TryParse(val, NumberStyles.Integer, _usCulture, out rval))
                                        {
                                            seasonNumber = rval;
                                        }
                                    }

                                    break;
                                }

                            case "Combined_episodenumber":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        float num;

                                        if (float.TryParse(val, NumberStyles.Any, _usCulture, out num))
                                        {
                                            combinedEpisodeNumber = Convert.ToInt32(num);
                                        }
                                    }

                                    break;
                                }

                            case "Combined_season":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        float num;

                                        if (float.TryParse(val, NumberStyles.Any, _usCulture, out num))
                                        {
                                            combinedSeasonNumber = Convert.ToInt32(num);
                                        }
                                    }

                                    break;
                                }

                            case "airsbefore_episode":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        int rval;

                                        // int.TryParse is local aware, so it can be probamatic, force us culture
                                        if (int.TryParse(val, NumberStyles.Integer, _usCulture, out rval))
                                        {
                                            item.AirsBeforeEpisodeNumber = rval;
                                        }
                                    }

                                    break;
                                }

                            case "airsafter_season":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        int rval;

                                        // int.TryParse is local aware, so it can be probamatic, force us culture
                                        if (int.TryParse(val, NumberStyles.Integer, _usCulture, out rval))
                                        {
                                            item.AirsAfterSeasonNumber = rval;
                                        }
                                    }

                                    break;
                                }

                            case "airsbefore_season":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        int rval;

                                        // int.TryParse is local aware, so it can be probamatic, force us culture
                                        if (int.TryParse(val, NumberStyles.Integer, _usCulture, out rval))
                                        {
                                            item.AirsBeforeSeasonNumber = rval;
                                        }
                                    }

                                    break;
                                }

                            case "EpisodeName":
                                {
                                    var val = reader.ReadElementContentAsString();
                                    if (!item.LockedFields.Contains(MetadataFields.Name))
                                    {
                                        if (!string.IsNullOrWhiteSpace(val))
                                        {
                                            item.Name = val;
                                        }
                                    }
                                    break;
                                }

                            case "Overview":
                                {
                                    var val = reader.ReadElementContentAsString();
                                    if (!item.LockedFields.Contains(MetadataFields.Overview))
                                    {
                                        if (!string.IsNullOrWhiteSpace(val))
                                        {
                                            item.Overview = val;
                                        }
                                    }
                                    break;
                                }
                            case "Rating":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        float rval;

                                        // float.TryParse is local aware, so it can be probamatic, force us culture
                                        if (float.TryParse(val, NumberStyles.AllowDecimalPoint, _usCulture, out rval))
                                        {
                                            item.CommunityRating = rval;
                                        }
                                    }
                                    break;
                                }
                            case "RatingCount":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        int rval;

                                        // int.TryParse is local aware, so it can be probamatic, force us culture
                                        if (int.TryParse(val, NumberStyles.Integer, _usCulture, out rval))
                                        {
                                            //item.VoteCount = rval;
                                        }
                                    }

                                    break;
                                }

                            case "FirstAired":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        DateTime date;
                                        if (DateTime.TryParse(val, out date))
                                        {
                                            date = date.ToUniversalTime();

                                            item.PremiereDate = date;
                                            item.ProductionYear = date.Year;
                                        }
                                    }

                                    break;
                                }

                            case "Director":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        if (!item.LockedFields.Contains(MetadataFields.Cast))
                                        {
                                            AddPeople(result, val, PersonType.Director);
                                        }
                                    }

                                    break;
                                }
                            case "GuestStars":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        if (!item.LockedFields.Contains(MetadataFields.Cast))
                                        {
                                            AddGuestStars(result, val);
                                        }
                                    }

                                    break;
                                }
                            case "Writer":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        if (!item.LockedFields.Contains(MetadataFields.Cast))
                                        {
                                            //AddPeople(result, val, PersonType.Writer);
                                        }
                                    }

                                    break;
                                }
                            case "Language":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        result.ResultLanguage = val;
                                    }

                                    break;
                                }

                            default:
                                reader.Skip();
                                break;
                        }
                    }
                    else
                    {
                        reader.Read();
                    }
                }
            }

            if (string.Equals(seriesOrder, "dvd", StringComparison.OrdinalIgnoreCase))
            {
                episodeNumber = combinedEpisodeNumber ?? episodeNumber;
                seasonNumber = combinedSeasonNumber ?? seasonNumber;
            }

            if (episodeNumber.HasValue)
            {
                item.IndexNumber = episodeNumber;
            }

            if (seasonNumber.HasValue)
            {
                item.ParentIndexNumber = seasonNumber;
            }
        }

        private void AddPeople<T>(MetadataResult<T> result, string val, string personType)
        {
            // Sometimes tvdb actors have leading spaces
            foreach (var person in val.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                                            .Where(i => !string.IsNullOrWhiteSpace(i))
                                            .Select(str => new PersonInfo { Type = personType, Name = str.Trim() }))
            {
                result.AddPerson(person);
            }
        }

        private void AddGuestStars<T>(MetadataResult<T> result, string val)
            where T : BaseItem
        {
            // example:
            // <GuestStars>|Mark C. Thomas|  Dennis Kiefer|  David Nelson (David)|  Angela Nicholas|  Tzi Ma|  Kevin P. Kearns (Pasco)|</GuestStars>
            var persons = val.Split('|')
                .Select(i => i.Trim())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList();

            foreach (var person in persons)
            {
                var index = person.IndexOf('(');
                string role = null;
                var name = person;

                if (index != -1)
                {
                    role = person.Substring(index + 1).Trim().TrimEnd(')');

                    name = person.Substring(0, index).Trim();
                }

                result.AddPerson(new PersonInfo
                {
                    Type = PersonType.GuestStar,
                    Name = name,
                    Role = role
                });
            }
        }

        private void FetchAdditionalPartInfo(MetadataResult<Episode> result, XmlReader reader, CancellationToken cancellationToken)
        {
            var item = result.Item;

            // Use XmlReader for best performance
            using (reader)
            {
                reader.MoveToContent();
                reader.Read();

                // Loop through each element
                while (!reader.EOF && reader.ReadState == ReadState.Interactive)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.Name)
                        {
                            case "EpisodeName":
                                {
                                    var val = reader.ReadElementContentAsString();
                                    if (!item.LockedFields.Contains(MetadataFields.Name))
                                    {
                                        if (!string.IsNullOrWhiteSpace(val))
                                        {
                                            item.Name += ", " + val;
                                        }
                                    }
                                    break;
                                }

                            case "Overview":
                                {
                                    var val = reader.ReadElementContentAsString();
                                    if (!item.LockedFields.Contains(MetadataFields.Overview))
                                    {
                                        if (!string.IsNullOrWhiteSpace(val))
                                        {
                                            item.Overview += Environment.NewLine + Environment.NewLine + val;
                                        }
                                    }
                                    break;
                                }
                            case "Director":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        if (!item.LockedFields.Contains(MetadataFields.Cast))
                                        {
                                            AddPeople(result, val, PersonType.Director);
                                        }
                                    }

                                    break;
                                }
                            case "GuestStars":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        if (!item.LockedFields.Contains(MetadataFields.Cast))
                                        {
                                            AddGuestStars(result, val);
                                        }
                                    }

                                    break;
                                }
                            case "Writer":
                                {
                                    var val = reader.ReadElementContentAsString();

                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        if (!item.LockedFields.Contains(MetadataFields.Cast))
                                        {
                                            //AddPeople(result, val, PersonType.Writer);
                                        }
                                    }

                                    break;
                                }

                            default:
                                reader.Skip();
                                break;
                        }
                    }
                    else
                    {
                        reader.Read();
                    }
                }
            }
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClient.GetResponse(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url
            });
        }

        public int Order { get { return 0; } }
    }
}
