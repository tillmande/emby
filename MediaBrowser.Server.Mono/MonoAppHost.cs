using System;
using System.Collections.Generic;
using System.Reflection;
using Emby.Server.Connect;
using Emby.Server.Implementations;
using MediaBrowser.Controller.Connect;
using MediaBrowser.Controller.Sync;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.System;
using Emby.Server.Implementations.HttpServer;

namespace MediaBrowser.Server.Mono
{
    public class MonoAppHost : ApplicationHost
    {
        public MonoAppHost(ServerApplicationPaths applicationPaths, ILogManager logManager, StartupOptions options, IFileSystem fileSystem, IPowerManagement powerManagement, string releaseAssetFilename, IEnvironmentInfo environmentInfo, MediaBrowser.Controller.Drawing.IImageEncoder imageEncoder, ISystemEvents systemEvents, MediaBrowser.Common.Net.INetworkManager networkManager) : base(applicationPaths, logManager, options, fileSystem, powerManagement, releaseAssetFilename, environmentInfo, imageEncoder, systemEvents, networkManager)
        {
        }

        public override bool CanSelfRestart
        {
            get
            {
                // A restart script must be provided
                return StartupOptions.ContainsOption("-restartpath");
            }
        }

        protected override IConnectManager CreateConnectManager()
        {
            return new ConnectManager();
        }

        protected override void RestartInternal()
        {
            MainClass.Restart();
        }

        protected override List<Assembly> GetAssembliesWithPartsInternal()
        {
            var list = new List<Assembly>();

            list.Add(GetType().Assembly);
            list.Add(typeof(ConnectManager).Assembly);
            list.Add(typeof(Emby.Server.Sync.SyncManager).Assembly);

            return list;
        }

        protected override void ShutdownInternal()
        {
            MainClass.Shutdown();
        }

        protected override bool SupportsDualModeSockets
        {
            get
            {
                return true;
            }
        }

        protected override IHttpListener CreateHttpListener()
        {
            return new EmbyServer.SocketSharp.WebSocketSharpListener(LogManager.GetLogger("HttpServer"),
                Certificate,
                StreamHelper,
                TextEncoding,
                NetworkManager,
                SocketFactory,
                CryptographyProvider,
                SupportsDualModeSockets,
                FileSystemManager,
                EnvironmentInfo);
        }
    }
}
