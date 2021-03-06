using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Emby.Dlna.Didl;
using Emby.Dlna.Server;
using Emby.Dlna.Service;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Xml;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Controller.LiveTv;

namespace Emby.Dlna.ContentDirectory
{
    public class ControlHandler : BaseControlHandler
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly IServerConfigurationManager _config;
        private readonly User _user;
        private readonly IUserViewManager _userViewManager;
        private readonly ITVSeriesManager _tvSeriesManager;

        private const string NS_DC = "http://purl.org/dc/elements/1.1/";
        private const string NS_DIDL = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
        private const string NS_DLNA = "urn:schemas-dlna-org:metadata-1-0/";
        private const string NS_UPNP = "urn:schemas-upnp-org:metadata-1-0/upnp/";

        private readonly int _systemUpdateId;
        private readonly CultureInfo _usCulture = new CultureInfo("en-US");

        private readonly DidlBuilder _didlBuilder;

        private readonly DeviceProfile _profile;

        public ControlHandler(ILogger logger, ILibraryManager libraryManager, DeviceProfile profile, string serverAddress, string accessToken, IImageProcessor imageProcessor, IUserDataManager userDataManager, User user, int systemUpdateId, IServerConfigurationManager config, ILocalizationManager localization, IMediaSourceManager mediaSourceManager, IUserViewManager userViewManager, IMediaEncoder mediaEncoder, IXmlReaderSettingsFactory xmlReaderSettingsFactory, ITVSeriesManager tvSeriesManager)
            : base(config, logger, xmlReaderSettingsFactory)
        {
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
            _user = user;
            _systemUpdateId = systemUpdateId;
            _userViewManager = userViewManager;
            _tvSeriesManager = tvSeriesManager;
            _profile = profile;
            _config = config;

            _didlBuilder = new DidlBuilder(profile, user, imageProcessor, serverAddress, accessToken, userDataManager, localization, mediaSourceManager, Logger, libraryManager, mediaEncoder);
        }

        protected override IEnumerable<KeyValuePair<string, string>> GetResult(string methodName, IDictionary<string, string> methodParams)
        {
            var deviceId = "test";

            var user = _user;

            if (string.Equals(methodName, "GetSearchCapabilities", StringComparison.OrdinalIgnoreCase))
                return HandleGetSearchCapabilities();

            if (string.Equals(methodName, "GetSortCapabilities", StringComparison.OrdinalIgnoreCase))
                return HandleGetSortCapabilities();

            if (string.Equals(methodName, "GetSortExtensionCapabilities", StringComparison.OrdinalIgnoreCase))
                return HandleGetSortExtensionCapabilities();

            if (string.Equals(methodName, "GetSystemUpdateID", StringComparison.OrdinalIgnoreCase))
                return HandleGetSystemUpdateID();

            if (string.Equals(methodName, "Browse", StringComparison.OrdinalIgnoreCase))
                return HandleBrowse(methodParams, user, deviceId);

            if (string.Equals(methodName, "X_GetFeatureList", StringComparison.OrdinalIgnoreCase))
                return HandleXGetFeatureList();

            if (string.Equals(methodName, "GetFeatureList", StringComparison.OrdinalIgnoreCase))
                return HandleGetFeatureList();

            if (string.Equals(methodName, "X_SetBookmark", StringComparison.OrdinalIgnoreCase))
                return HandleXSetBookmark(methodParams, user);

            if (string.Equals(methodName, "Search", StringComparison.OrdinalIgnoreCase))
                return HandleSearch(methodParams, user, deviceId);

            if (string.Equals(methodName, "X_BrowseByLetter", StringComparison.OrdinalIgnoreCase))
                return HandleX_BrowseByLetter(methodParams, user, deviceId);

            throw new ResourceNotFoundException("Unexpected control request name: " + methodName);
        }

        private IEnumerable<KeyValuePair<string, string>> HandleXSetBookmark(IDictionary<string, string> sparams, User user)
        {
            var id = sparams["ObjectID"];

            var serverItem = GetItemFromObjectId(id, user);

            var item = serverItem.Item;

            var newbookmark = int.Parse(sparams["PosSecond"], _usCulture);

            var userdata = _userDataManager.GetUserData(user, item);

            userdata.PlaybackPositionTicks = TimeSpan.FromSeconds(newbookmark).Ticks;

            _userDataManager.SaveUserData(user, item, userdata, UserDataSaveReason.TogglePlayed,
                CancellationToken.None);

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<KeyValuePair<string, string>> HandleGetSearchCapabilities()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "SearchCaps", "res@resolution,res@size,res@duration,dc:title,dc:creator,upnp:actor,upnp:artist,upnp:genre,upnp:album,dc:date,upnp:class,@id,@refID,@protocolInfo,upnp:author,dc:description,pv:avKeywords" }
            };
        }

        private IEnumerable<KeyValuePair<string, string>> HandleGetSortCapabilities()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "SortCaps", "res@duration,res@size,res@bitrate,dc:date,dc:title,dc:size,upnp:album,upnp:artist,upnp:albumArtist,upnp:episodeNumber,upnp:genre,upnp:originalTrackNumber,upnp:rating" }
            };
        }

        private IEnumerable<KeyValuePair<string, string>> HandleGetSortExtensionCapabilities()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "SortExtensionCaps", "res@duration,res@size,res@bitrate,dc:date,dc:title,dc:size,upnp:album,upnp:artist,upnp:albumArtist,upnp:episodeNumber,upnp:genre,upnp:originalTrackNumber,upnp:rating" }
            };
        }

        private IEnumerable<KeyValuePair<string, string>> HandleGetSystemUpdateID()
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers.Add("Id", _systemUpdateId.ToString(_usCulture));
            return headers;
        }

        private IEnumerable<KeyValuePair<string, string>> HandleGetFeatureList()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "FeatureList", GetFeatureListXml() }
            };
        }

        private IEnumerable<KeyValuePair<string, string>> HandleXGetFeatureList()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "FeatureList", GetFeatureListXml() }
            };
        }

        private string GetFeatureListXml()
        {
            var builder = new StringBuilder();

            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            builder.Append("<Features xmlns=\"urn:schemas-upnp-org:av:avs\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"urn:schemas-upnp-org:av:avs http://www.upnp.org/schemas/av/avs.xsd\">");

            builder.Append("<Feature name=\"samsung.com_BASICVIEW\" version=\"1\">");
            builder.Append("<container id=\"I\" type=\"object.item.imageItem\"/>");
            builder.Append("<container id=\"A\" type=\"object.item.audioItem\"/>");
            builder.Append("<container id=\"V\" type=\"object.item.videoItem\"/>");
            builder.Append("</Feature>");

            builder.Append("</Features>");

            return builder.ToString();
        }

        public string GetValueOrDefault(IDictionary<string, string> sparams, string key, string defaultValue)
        {
            string val;

            if (sparams.TryGetValue(key, out val))
            {
                return val;
            }

            return defaultValue;
        }

        private IEnumerable<KeyValuePair<string, string>> HandleBrowse(IDictionary<string, string> sparams, User user, string deviceId)
        {
            var id = sparams["ObjectID"];
            var flag = sparams["BrowseFlag"];
            var filter = new Filter(GetValueOrDefault(sparams, "Filter", "*"));
            var sortCriteria = new SortCriteria(GetValueOrDefault(sparams, "SortCriteria", ""));

            var provided = 0;

            // Default to null instead of 0
            // Upnp inspector sends 0 as requestedCount when it wants everything
            int? requestedCount = null;
            int? start = 0;

            int requestedVal;
            if (sparams.ContainsKey("RequestedCount") && int.TryParse(sparams["RequestedCount"], out requestedVal) && requestedVal > 0)
            {
                requestedCount = requestedVal;
            }

            int startVal;
            if (sparams.ContainsKey("StartingIndex") && int.TryParse(sparams["StartingIndex"], out startVal) && startVal > 0)
            {
                start = startVal;
            }

            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                CloseOutput = false,
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment
            };

            StringWriter builder = new StringWriterWithEncoding(Encoding.UTF8);

            int totalCount;

            var dlnaOptions = _config.GetDlnaConfiguration();

            using (XmlWriter writer = XmlWriter.Create(builder, settings))
            {
                //writer.WriteStartDocument();

                writer.WriteStartElement(string.Empty, "DIDL-Lite", NS_DIDL);

                writer.WriteAttributeString("xmlns", "dc", null, NS_DC);
                writer.WriteAttributeString("xmlns", "dlna", null, NS_DLNA);
                writer.WriteAttributeString("xmlns", "upnp", null, NS_UPNP);
                //didl.SetAttribute("xmlns:sec", NS_SEC);

                DidlBuilder.WriteXmlRootAttributes(_profile, writer);

                var serverItem = GetItemFromObjectId(id, user);
                var item = serverItem.Item;

                if (string.Equals(flag, "BrowseMetadata"))
                {
                    totalCount = 1;

                    if (item.IsDisplayedAsFolder || serverItem.StubType.HasValue)
                    {
                        var childrenResult = (GetUserItems(item, serverItem.StubType, user, sortCriteria, start, requestedCount));

                        _didlBuilder.WriteFolderElement(writer, item, serverItem.StubType, null, childrenResult.TotalRecordCount, filter, id);
                    }
                    else
                    {
                        _didlBuilder.WriteItemElement(dlnaOptions, writer, item, user, null, null, deviceId, filter);
                    }

                    provided++;
                }
                else
                {
                    var childrenResult = (GetUserItems(item, serverItem.StubType, user, sortCriteria, start, requestedCount));
                    totalCount = childrenResult.TotalRecordCount;

                    provided = childrenResult.Items.Length;

                    foreach (var i in childrenResult.Items)
                    {
                        var childItem = i.Item;
                        var displayStubType = i.StubType;

                        if (childItem.IsDisplayedAsFolder || displayStubType.HasValue)
                        {
                            var childCount = (GetUserItems(childItem, displayStubType, user, sortCriteria, null, 0))
                                .TotalRecordCount;

                            _didlBuilder.WriteFolderElement(writer, childItem, displayStubType, item, childCount, filter);
                        }
                        else
                        {
                            _didlBuilder.WriteItemElement(dlnaOptions, writer, childItem, user, item, serverItem.StubType, deviceId, filter);
                        }
                    }
                }
                writer.WriteFullEndElement();
                //writer.WriteEndDocument();
            }

            var resXML = builder.ToString();

            return new []
                {
                    new KeyValuePair<string,string>("Result", resXML),
                    new KeyValuePair<string,string>("NumberReturned", provided.ToString(_usCulture)),
                    new KeyValuePair<string,string>("TotalMatches", totalCount.ToString(_usCulture)),
                    new KeyValuePair<string,string>("UpdateID", _systemUpdateId.ToString(_usCulture))
                };
        }

        private IEnumerable<KeyValuePair<string, string>> HandleX_BrowseByLetter(IDictionary<string, string> sparams, User user, string deviceId)
        {
            // TODO: Implement this method
            return HandleSearch(sparams, user, deviceId);
        }

        private IEnumerable<KeyValuePair<string, string>> HandleSearch(IDictionary<string, string> sparams, User user, string deviceId)
        {
            var searchCriteria = new SearchCriteria(GetValueOrDefault(sparams, "SearchCriteria", ""));
            var sortCriteria = new SortCriteria(GetValueOrDefault(sparams, "SortCriteria", ""));
            var filter = new Filter(GetValueOrDefault(sparams, "Filter", "*"));

            // sort example: dc:title, dc:date

            // Default to null instead of 0
            // Upnp inspector sends 0 as requestedCount when it wants everything
            int? requestedCount = null;
            int? start = 0;

            int requestedVal;
            if (sparams.ContainsKey("RequestedCount") && int.TryParse(sparams["RequestedCount"], out requestedVal) && requestedVal > 0)
            {
                requestedCount = requestedVal;
            }

            int startVal;
            if (sparams.ContainsKey("StartingIndex") && int.TryParse(sparams["StartingIndex"], out startVal) && startVal > 0)
            {
                start = startVal;
            }

            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                CloseOutput = false,
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment
            };

            StringWriter builder = new StringWriterWithEncoding(Encoding.UTF8);
            int totalCount = 0;
            int provided = 0;

            using (XmlWriter writer = XmlWriter.Create(builder, settings))
            {
                //writer.WriteStartDocument();

                writer.WriteStartElement(string.Empty, "DIDL-Lite", NS_DIDL);

                writer.WriteAttributeString("xmlns", "dc", null, NS_DC);
                writer.WriteAttributeString("xmlns", "dlna", null, NS_DLNA);
                writer.WriteAttributeString("xmlns", "upnp", null, NS_UPNP);
                //didl.SetAttribute("xmlns:sec", NS_SEC);

                DidlBuilder.WriteXmlRootAttributes(_profile, writer);

                var serverItem = GetItemFromObjectId(sparams["ContainerID"], user);

                var item = serverItem.Item;

                var childrenResult = (GetChildrenSorted(item, user, searchCriteria, sortCriteria, start, requestedCount));

                totalCount = childrenResult.TotalRecordCount;

                provided = childrenResult.Items.Length;

                var dlnaOptions = _config.GetDlnaConfiguration();

                foreach (var i in childrenResult.Items)
                {
                    if (i.IsDisplayedAsFolder)
                    {
                        var childCount = (GetChildrenSorted(i, user, searchCriteria, sortCriteria, null, 0))
                            .TotalRecordCount;

                        _didlBuilder.WriteFolderElement(writer, i, null, item, childCount, filter);
                    }
                    else
                    {
                        _didlBuilder.WriteItemElement(dlnaOptions, writer, i, user, item, serverItem.StubType, deviceId, filter);
                    }
                }

                writer.WriteFullEndElement();
                //writer.WriteEndDocument();
            }

            var resXML = builder.ToString();

            return new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string,string>("Result", resXML),
                    new KeyValuePair<string,string>("NumberReturned", provided.ToString(_usCulture)),
                    new KeyValuePair<string,string>("TotalMatches", totalCount.ToString(_usCulture)),
                    new KeyValuePair<string,string>("UpdateID", _systemUpdateId.ToString(_usCulture))
                };
        }

        private QueryResult<BaseItem> GetChildrenSorted(BaseItem item, User user, SearchCriteria search, SortCriteria sort, int? startIndex, int? limit)
        {
            var folder = (Folder)item;

            var sortOrders = new List<string>();
            if (!folder.IsPreSorted)
            {
                sortOrders.Add(ItemSortBy.SortName);
            }

            var mediaTypes = new List<string>();
            bool? isFolder = null;

            if (search.SearchType == SearchType.Audio)
            {
                mediaTypes.Add(MediaType.Audio);
                isFolder = false;
            }
            else if (search.SearchType == SearchType.Video)
            {
                mediaTypes.Add(MediaType.Video);
                isFolder = false;
            }
            else if (search.SearchType == SearchType.Image)
            {
                mediaTypes.Add(MediaType.Photo);
                isFolder = false;
            }
            else if (search.SearchType == SearchType.Playlist)
            {
                //items = items.OfType<Playlist>();
                isFolder = true;
            }
            else if (search.SearchType == SearchType.MusicAlbum)
            {
                //items = items.OfType<MusicAlbum>();
                isFolder = true;
            }

            return folder.GetItems(new InternalItemsQuery
            {
                Limit = limit,
                StartIndex = startIndex,
                OrderBy = sortOrders.Select(i => new ValueTuple<string, SortOrder>(i, sort.SortOrder)).ToArray(),
                User = user,
                Recursive = true,
                IsMissing = false,
                ExcludeItemTypes = new[] { typeof(Game).Name, typeof(Book).Name },
                IsFolder = isFolder,
                MediaTypes = mediaTypes.ToArray(mediaTypes.Count),
                DtoOptions = GetDtoOptions()
            });
        }

        private DtoOptions GetDtoOptions()
        {
            return new DtoOptions(true);
        }

        private QueryResult<ServerItem> GetUserItems(BaseItem item, StubType? stubType, User user, SortCriteria sort, int? startIndex, int? limit)
        {
            if (item is MusicGenre)
            {
                return GetMusicGenreItems(item, Guid.Empty, user, sort, startIndex, limit);
            }

            if (item is MusicArtist)
            {
                return GetMusicArtistItems(item, Guid.Empty, user, sort, startIndex, limit);
            }

            if (item is Genre)
            {
                return GetGenreItems(item, Guid.Empty, user, sort, startIndex, limit);
            }

            if (!stubType.HasValue || stubType.Value != StubType.Folder)
            {
                var collectionFolder = item as IHasCollectionType;
                if (collectionFolder != null && string.Equals(CollectionType.Music, collectionFolder.CollectionType, StringComparison.OrdinalIgnoreCase))
                {
                    return GetMusicFolders(item, user, stubType, sort, startIndex, limit);
                }
                if (collectionFolder != null && string.Equals(CollectionType.Movies, collectionFolder.CollectionType, StringComparison.OrdinalIgnoreCase))
                {
                    return GetMovieFolders(item, user, stubType, sort, startIndex, limit);
                }
                if (collectionFolder != null && string.Equals(CollectionType.TvShows, collectionFolder.CollectionType, StringComparison.OrdinalIgnoreCase))
                {
                    return GetTvFolders(item, user, stubType, sort, startIndex, limit);
                }

                if (collectionFolder != null && string.Equals(CollectionType.Folders, collectionFolder.CollectionType, StringComparison.OrdinalIgnoreCase))
                {
                    return GetFolders(item, user, stubType, sort, startIndex, limit);
                }
                if (collectionFolder != null && string.Equals(CollectionType.LiveTv, collectionFolder.CollectionType, StringComparison.OrdinalIgnoreCase))
                {
                    return GetLiveTvChannels(item, user, stubType, sort, startIndex, limit);
                }
            }

            if (stubType.HasValue)
            {
                if (stubType.Value != StubType.Folder)
                {
                    return ApplyPaging(new QueryResult<ServerItem>(), startIndex, limit);
                }
            }

            var folder = (Folder)item;

            var query = new InternalItemsQuery(user)
            {
                Limit = limit,
                StartIndex = startIndex,
                IsVirtualItem = false,
                ExcludeItemTypes = new[] { typeof(Game).Name, typeof(Book).Name },
                IsPlaceHolder = false,
                DtoOptions = GetDtoOptions()
            };

            SetSorting(query, sort, folder.IsPreSorted);

            var queryResult = folder.GetItems(query);

            return ToResult(queryResult);
        }

        private QueryResult<ServerItem> GetLiveTvChannels(BaseItem item, User user, StubType? stubType, SortCriteria sort, int? startIndex, int? limit)
        {
            var query = new InternalItemsQuery(user)
            {
                StartIndex = startIndex,
                Limit = limit,
            };
            query.IncludeItemTypes = new[] { typeof(LiveTvChannel).Name };

            SetSorting(query, sort, false);

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMusicFolders(BaseItem item, User user, StubType? stubType, SortCriteria sort, int? startIndex, int? limit)
        {
            var query = new InternalItemsQuery(user)
            {
                StartIndex = startIndex,
                Limit = limit
            };
            SetSorting(query, sort, false);

            if (stubType.HasValue && stubType.Value == StubType.Latest)
            {
                return GetMusicLatest(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Playlists)
            {
                return GetMusicPlaylists(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Albums)
            {
                return GetMusicAlbums(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Artists)
            {
                return GetMusicArtists(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.AlbumArtists)
            {
                return GetMusicAlbumArtists(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.FavoriteAlbums)
            {
                return GetFavoriteAlbums(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.FavoriteArtists)
            {
                return GetFavoriteArtists(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.FavoriteSongs)
            {
                return GetFavoriteSongs(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Songs)
            {
                return GetMusicSongs(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Genres)
            {
                return GetMusicGenres(item, user, query);
            }

            var list = new List<ServerItem>();

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Latest
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Playlists
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Albums
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.AlbumArtists
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Artists
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Songs
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Genres
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.FavoriteArtists
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.FavoriteAlbums
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.FavoriteSongs
            });

            return new QueryResult<ServerItem>
            {
                Items = list.ToArray(list.Count),
                TotalRecordCount = list.Count
            };
        }

        private QueryResult<ServerItem> GetMovieFolders(BaseItem item, User user, StubType? stubType, SortCriteria sort, int? startIndex, int? limit)
        {
            var query = new InternalItemsQuery(user)
            {
                StartIndex = startIndex,
                Limit = limit
            };
            SetSorting(query, sort, false);

            if (stubType.HasValue && stubType.Value == StubType.ContinueWatching)
            {
                return GetMovieContinueWatching(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Latest)
            {
                return GetMovieLatest(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Movies)
            {
                return GetMovieMovies(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Collections)
            {
                return GetMovieCollections(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Favorites)
            {
                return GetMovieFavorites(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Genres)
            {
                return GetGenres(item, user, query);
            }

            var list = new List<ServerItem>();

            list.Add(new ServerItem(item)
            {
                StubType = StubType.ContinueWatching
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Latest
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Movies
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Collections
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Favorites
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Genres
            });

            return new QueryResult<ServerItem>
            {
                Items = list.ToArray(list.Count),
                TotalRecordCount = list.Count
            };
        }

        private QueryResult<ServerItem> GetFolders(BaseItem item, User user, StubType? stubType, SortCriteria sort, int? startIndex, int? limit)
        {
            var folders = _libraryManager.GetUserRootFolder().GetChildren(user, true)
                .OrderBy(i => i.SortName)
                .Select(i => new ServerItem(i)
                {
                    StubType = StubType.Folder
                })
                .ToArray();

            return new QueryResult<ServerItem>
            {
                Items = folders,
                TotalRecordCount = folders.Length
            };
        }

        private QueryResult<ServerItem> GetTvFolders(BaseItem item, User user, StubType? stubType, SortCriteria sort, int? startIndex, int? limit)
        {
            var query = new InternalItemsQuery(user)
            {
                StartIndex = startIndex,
                Limit = limit
            };
            SetSorting(query, sort, false);

            if (stubType.HasValue && stubType.Value == StubType.ContinueWatching)
            {
                return GetMovieContinueWatching(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.NextUp)
            {
                return GetNextUp(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Latest)
            {
                return GetTvLatest(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Series)
            {
                return GetSeries(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.FavoriteSeries)
            {
                return GetFavoriteSeries(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.FavoriteEpisodes)
            {
                return GetFavoriteEpisodes(item, user, query);
            }

            if (stubType.HasValue && stubType.Value == StubType.Genres)
            {
                return GetGenres(item, user, query);
            }

            var list = new List<ServerItem>();

            list.Add(new ServerItem(item)
            {
                StubType = StubType.ContinueWatching
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.NextUp
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Latest
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Series
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.FavoriteSeries
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.FavoriteEpisodes
            });

            list.Add(new ServerItem(item)
            {
                StubType = StubType.Genres
            });

            return new QueryResult<ServerItem>
            {
                Items = list.ToArray(list.Count),
                TotalRecordCount = list.Count
            };
        }

        private QueryResult<ServerItem> GetMovieContinueWatching(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);

            query.OrderBy = new ValueTuple<string, SortOrder>[]
            {
                new ValueTuple<string, SortOrder> (ItemSortBy.DatePlayed, SortOrder.Descending),
                new ValueTuple<string, SortOrder> (ItemSortBy.SortName, SortOrder.Ascending)
            };

            query.IsResumable = true;
            query.Limit = 10;

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetSeries(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);

            query.IncludeItemTypes = new[] { typeof(Series).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMovieMovies(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);

            query.IncludeItemTypes = new[] { typeof(Movie).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMovieCollections(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            //query.Parent = parent;
            query.SetUser(user);

            query.IncludeItemTypes = new[] { typeof(BoxSet).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMusicAlbums(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);

            query.IncludeItemTypes = new[] { typeof(MusicAlbum).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMusicSongs(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);

            query.IncludeItemTypes = new[] { typeof(Audio).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetFavoriteSongs(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);
            query.IsFavorite = true;
            query.IncludeItemTypes = new[] { typeof(Audio).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetFavoriteSeries(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);
            query.IsFavorite = true;
            query.IncludeItemTypes = new[] { typeof(Series).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetFavoriteEpisodes(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);
            query.IsFavorite = true;
            query.IncludeItemTypes = new[] { typeof(Episode).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMovieFavorites(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);
            query.IsFavorite = true;
            query.IncludeItemTypes = new[] { typeof(Movie).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetFavoriteAlbums(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Recursive = true;
            query.Parent = parent;
            query.SetUser(user);
            query.IsFavorite = true;
            query.IncludeItemTypes = new[] { typeof(MusicAlbum).Name };

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetGenres(BaseItem parent, User user, InternalItemsQuery query)
        {
            var genresResult = _libraryManager.GetGenres(new InternalItemsQuery(user)
            {
                AncestorIds = new[] { parent.Id },
                StartIndex = query.StartIndex,
                Limit = query.Limit
            });

            var result = new QueryResult<BaseItem>
            {
                TotalRecordCount = genresResult.TotalRecordCount,
                Items = genresResult.Items.Select(i => i.Item1).ToArray(genresResult.Items.Length)
            };

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMusicGenres(BaseItem parent, User user, InternalItemsQuery query)
        {
            var genresResult = _libraryManager.GetMusicGenres(new InternalItemsQuery(user)
            {
                AncestorIds = new[] { parent.Id },
                StartIndex = query.StartIndex,
                Limit = query.Limit
            });

            var result = new QueryResult<BaseItem>
            {
                TotalRecordCount = genresResult.TotalRecordCount,
                Items = genresResult.Items.Select(i => i.Item1).ToArray(genresResult.Items.Length)
            };

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMusicAlbumArtists(BaseItem parent, User user, InternalItemsQuery query)
        {
            var artists = _libraryManager.GetAlbumArtists(new InternalItemsQuery(user)
            {
                AncestorIds = new[] { parent.Id },
                StartIndex = query.StartIndex,
                Limit = query.Limit
            });

            var result = new QueryResult<BaseItem>
            {
                TotalRecordCount = artists.TotalRecordCount,
                Items = artists.Items.Select(i => i.Item1).ToArray(artists.Items.Length)
            };

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMusicArtists(BaseItem parent, User user, InternalItemsQuery query)
        {
            var artists = _libraryManager.GetArtists(new InternalItemsQuery(user)
            {
                AncestorIds = new[] { parent.Id },
                StartIndex = query.StartIndex,
                Limit = query.Limit
            });

            var result = new QueryResult<BaseItem>
            {
                TotalRecordCount = artists.TotalRecordCount,
                Items = artists.Items.Select(i => i.Item1).ToArray(artists.Items.Length)
            };

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetFavoriteArtists(BaseItem parent, User user, InternalItemsQuery query)
        {
            var artists = _libraryManager.GetArtists(new InternalItemsQuery(user)
            {
                AncestorIds = new[] { parent.Id },
                StartIndex = query.StartIndex,
                Limit = query.Limit,
                IsFavorite = true
            });

            var result = new QueryResult<BaseItem>
            {
                TotalRecordCount = artists.TotalRecordCount,
                Items = artists.Items.Select(i => i.Item1).ToArray(artists.Items.Length)
            };

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMusicPlaylists(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.Parent = null;
            query.IncludeItemTypes = new[] { typeof(Playlist).Name };
            query.SetUser(user);
            query.Recursive = true;

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMusicLatest(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.OrderBy = new ValueTuple<string, SortOrder>[] { };

            var items = _userViewManager.GetLatestItems(new LatestItemsQuery
            {
                UserId = user.Id,
                Limit = 50,
                IncludeItemTypes = new[] { typeof(Audio).Name },
                ParentId = parent == null ? Guid.Empty : parent.Id,
                GroupItems = true

            }, query.DtoOptions).Select(i => i.Item1 ?? i.Item2.FirstOrDefault()).Where(i => i != null).ToArray();

            return ToResult(items);
        }

        private QueryResult<ServerItem> GetNextUp(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.OrderBy = new ValueTuple<string, SortOrder>[] { };

            var result = _tvSeriesManager.GetNextUp(new NextUpQuery
            {
                Limit = query.Limit,
                StartIndex = query.StartIndex,
                UserId = query.User.Id

            }, new [] { parent }, query.DtoOptions);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetTvLatest(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.OrderBy = new ValueTuple<string, SortOrder>[] { };

            var items = _userViewManager.GetLatestItems(new LatestItemsQuery
            {
                UserId = user.Id,
                Limit = 50,
                IncludeItemTypes = new[] { typeof(Episode).Name },
                ParentId = parent == null ? Guid.Empty : parent.Id,
                GroupItems = false

            }, query.DtoOptions).Select(i => i.Item1 ?? i.Item2.FirstOrDefault()).Where(i => i != null).ToArray();

            return ToResult(items);
        }

        private QueryResult<ServerItem> GetMovieLatest(BaseItem parent, User user, InternalItemsQuery query)
        {
            query.OrderBy = new ValueTuple<string, SortOrder>[] { };

            var items = _userViewManager.GetLatestItems(new LatestItemsQuery
            {
                UserId = user.Id,
                Limit = 50,
                IncludeItemTypes = new[] { typeof(Movie).Name },
                ParentId = parent == null ? Guid.Empty : parent.Id,
                GroupItems = true

            }, query.DtoOptions).Select(i => i.Item1 ?? i.Item2.FirstOrDefault()).Where(i => i != null).ToArray();

            return ToResult(items);
        }

        private QueryResult<ServerItem> GetMusicArtistItems(BaseItem item, Guid parentId, User user, SortCriteria sort, int? startIndex, int? limit)
        {
            var query = new InternalItemsQuery(user)
            {
                Recursive = true,
                ParentId = parentId,
                ArtistIds = new[] { item.Id },
                IncludeItemTypes = new[] { typeof(MusicAlbum).Name },
                Limit = limit,
                StartIndex = startIndex,
                DtoOptions = GetDtoOptions()
            };

            SetSorting(query, sort, false);

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetGenreItems(BaseItem item, Guid parentId, User user, SortCriteria sort, int? startIndex, int? limit)
        {
            var query = new InternalItemsQuery(user)
            {
                Recursive = true,
                ParentId = parentId,
                GenreIds = new[] { item.Id },
                IncludeItemTypes = new[] { typeof(Movie).Name, typeof(Series).Name },
                Limit = limit,
                StartIndex = startIndex,
                DtoOptions = GetDtoOptions()
            };

            SetSorting(query, sort, false);

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> GetMusicGenreItems(BaseItem item, Guid parentId, User user, SortCriteria sort, int? startIndex, int? limit)
        {
            var query = new InternalItemsQuery(user)
            {
                Recursive = true,
                ParentId = parentId,
                GenreIds = new[] { item.Id },
                IncludeItemTypes = new[] { typeof(MusicAlbum).Name },
                Limit = limit,
                StartIndex = startIndex,
                DtoOptions = GetDtoOptions()
            };

            SetSorting(query, sort, false);

            var result = _libraryManager.GetItemsResult(query);

            return ToResult(result);
        }

        private QueryResult<ServerItem> ToResult(BaseItem[] result)
        {
            var serverItems = result
                .Select(i => new ServerItem(i))
                .ToArray(result.Length);

            return new QueryResult<ServerItem>
            {
                TotalRecordCount = result.Length,
                Items = serverItems
            };
        }

        private QueryResult<ServerItem> ToResult(QueryResult<BaseItem> result)
        {
            var serverItems = result
                .Items
                .Select(i => new ServerItem(i))
                .ToArray();

            return new QueryResult<ServerItem>
            {
                TotalRecordCount = result.TotalRecordCount,
                Items = serverItems
            };
        }

        private void SetSorting(InternalItemsQuery query, SortCriteria sort, bool isPreSorted)
        {
            var sortOrders = new List<string>();
            if (!isPreSorted)
            {
                sortOrders.Add(ItemSortBy.SortName);
            }

            query.OrderBy = sortOrders.Select(i => new ValueTuple<string, SortOrder>(i, sort.SortOrder)).ToArray();
        }

        private QueryResult<ServerItem> ApplyPaging(QueryResult<ServerItem> result, int? startIndex, int? limit)
        {
            result.Items = result.Items.Skip(startIndex ?? 0).Take(limit ?? int.MaxValue).ToArray();

            return result;
        }

        private ServerItem GetItemFromObjectId(string id, User user)
        {
            return DidlBuilder.IsIdRoot(id)

                 ? new ServerItem(_libraryManager.GetUserRootFolder())
                 : ParseItemId(id, user);
        }

        private ServerItem ParseItemId(string id, User user)
        {
            Guid itemId;
            StubType? stubType = null;

            // After using PlayTo, MediaMonkey sends a request to the server trying to get item info
            const string paramsSrch = "Params=";
            var paramsIndex = id.IndexOf(paramsSrch, StringComparison.OrdinalIgnoreCase);
            if (paramsIndex != -1)
            {
                id = id.Substring(paramsIndex + paramsSrch.Length);

                var parts = id.Split(';');
                id = parts[23];
            }

            var enumNames = Enum.GetNames(typeof(StubType));
            foreach (var name in enumNames)
            {
                if (id.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase))
                {
                    stubType = (StubType)Enum.Parse(typeof(StubType), name, true);
                    id = id.Split(new[] { '_' }, 2)[1];

                    break;
                }
            }

            if (Guid.TryParse(id, out itemId))
            {
                var item = _libraryManager.GetItemById(itemId);

                return new ServerItem(item)
                {
                    StubType = stubType
                };
            }

            Logger.Error("Error parsing item Id: {0}. Returning user root folder.", id);

            return new ServerItem(_libraryManager.GetUserRootFolder());
        }
    }

    internal class ServerItem
    {
        public BaseItem Item { get; set; }
        public StubType? StubType { get; set; }

        public ServerItem(BaseItem item)
        {
            Item = item;

            if (item is IItemByName && !(item is Folder))
            {
                StubType = Dlna.ContentDirectory.StubType.Folder;
            }
        }
    }

    public enum StubType
    {
        Folder = 0,
        Latest = 2,
        Playlists = 3,
        Albums = 4,
        AlbumArtists = 5,
        Artists = 6,
        Songs = 7,
        Genres = 8,
        FavoriteSongs = 9,
        FavoriteArtists = 10,
        FavoriteAlbums = 11,
        ContinueWatching = 12,
        Movies = 13,
        Collections = 14,
        Favorites = 15,
        NextUp = 16,
        Series = 17,
        FavoriteSeries = 18,
        FavoriteEpisodes = 19
    }
}
