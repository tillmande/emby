using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Xml;

namespace MediaBrowser.LocalMetadata.Savers
{
    /// <summary>
    /// Saves game.xml for games
    /// </summary>
    public class GameXmlSaver : BaseXmlSaver
    {
        private readonly CultureInfo UsCulture = new CultureInfo("en-US");

        public override bool IsEnabledFor(BaseItem item, ItemUpdateType updateType)
        {
            if (!item.SupportsLocalMetadata)
            {
                return false;
            }

            return item is Game && updateType >= ItemUpdateType.MetadataDownload;
        }

        protected override void WriteCustomElements(BaseItem item, XmlWriter writer)
        {
            var game = (Game)item;

            if (!string.IsNullOrEmpty(game.GameSystem))
            {
                writer.WriteElementString("GameSystem", game.GameSystem);
            }
            if (game.PlayersSupported.HasValue)
            {
                writer.WriteElementString("Players", game.PlayersSupported.Value.ToString(UsCulture));
            }
        }

        protected override string GetLocalSavePath(BaseItem item)
        {
            return GetGameSavePath((Game)item);
        }

        protected override string GetRootElementName(BaseItem item)
        {
            return "Item";
        }

        public static string GetGameSavePath(Game item)
        {
            if (item.IsInMixedFolder)
            {
                return Path.ChangeExtension(item.Path, ".xml");
            }

            return Path.Combine(item.ContainingFolderPath, "game.xml");
        }

        public GameXmlSaver(IFileSystem fileSystem, IServerConfigurationManager configurationManager, ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager, ILogger logger, IXmlReaderSettingsFactory xmlReaderSettingsFactory) : base(fileSystem, configurationManager, libraryManager, userManager, userDataManager, logger, xmlReaderSettingsFactory)
        {
        }
    }
}
