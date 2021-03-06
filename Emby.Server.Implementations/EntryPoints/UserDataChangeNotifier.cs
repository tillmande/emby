using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Threading;

namespace Emby.Server.Implementations.EntryPoints
{
    class UserDataChangeNotifier : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;
        private readonly ILogger _logger;
        private readonly IUserDataManager _userDataManager;
        private readonly IUserManager _userManager;

        private readonly object _syncLock = new object();
        private ITimer UpdateTimer { get; set; }
        private readonly ITimerFactory _timerFactory;
        private const int UpdateDuration = 500;

        private readonly Dictionary<Guid, List<BaseItem>> _changedItems = new Dictionary<Guid, List<BaseItem>>();

        public UserDataChangeNotifier(IUserDataManager userDataManager, ISessionManager sessionManager, ILogger logger, IUserManager userManager, ITimerFactory timerFactory)
        {
            _userDataManager = userDataManager;
            _sessionManager = sessionManager;
            _logger = logger;
            _userManager = userManager;
            _timerFactory = timerFactory;
        }

        public void Run()
        {
            _userDataManager.UserDataSaved += _userDataManager_UserDataSaved;
        }

        void _userDataManager_UserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            if (e.SaveReason == UserDataSaveReason.PlaybackProgress)
            {
                return;
            }

            lock (_syncLock)
            {
                if (UpdateTimer == null)
                {
                    UpdateTimer = _timerFactory.Create(UpdateTimerCallback, null, UpdateDuration,
                                                   Timeout.Infinite);
                }
                else
                {
                    UpdateTimer.Change(UpdateDuration, Timeout.Infinite);
                }

                List<BaseItem> keys;

                if (!_changedItems.TryGetValue(e.UserId, out keys))
                {
                    keys = new List<BaseItem>();
                    _changedItems[e.UserId] = keys;
                }

                keys.Add(e.Item);

                var baseItem = e.Item;

                // Go up one level for indicators
                if (baseItem != null)
                {
                    var parent = baseItem.GetOwner() ?? baseItem.GetParent();

                    if (parent != null)
                    {
                        keys.Add(parent);
                    }
                }
            }
        }

        private void UpdateTimerCallback(object state)
        {
            lock (_syncLock)
            {
                // Remove dupes in case some were saved multiple times
                var changes = _changedItems.ToList();
                _changedItems.Clear();

                var task = SendNotifications(changes, CancellationToken.None);

                if (UpdateTimer != null)
                {
                    UpdateTimer.Dispose();
                    UpdateTimer = null;
                }
            }
        }

        private async Task SendNotifications(List<KeyValuePair<Guid, List<BaseItem>>> changes, CancellationToken cancellationToken)
        {
            foreach (var pair in changes)
            {
                await SendNotifications(pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        private Task SendNotifications(Guid userId, List<BaseItem> changedItems, CancellationToken cancellationToken)
        {
            return _sessionManager.SendMessageToUserSessions(new List<Guid> { userId }, "UserDataChanged", () => GetUserDataChangeInfo(userId, changedItems), cancellationToken);
        }

        private UserDataChangeInfo GetUserDataChangeInfo(Guid userId, List<BaseItem> changedItems)
        {
            var user = _userManager.GetUserById(userId);

            var dtoList = changedItems
                .DistinctBy(i => i.Id)
                .Select(i =>
                {
                    var dto = _userDataManager.GetUserDataDto(i, user);
                    dto.ItemId = i.Id.ToString("N");
                    return dto;
                })
                .ToArray();

            var userIdString = userId.ToString("N");

            return new UserDataChangeInfo
            {
                UserId = userIdString,

                UserDataList = dtoList
            };
        }

        public void Dispose()
        {
            if (UpdateTimer != null)
            {
                UpdateTimer.Dispose();
                UpdateTimer = null;
            }

            _userDataManager.UserDataSaved -= _userDataManager_UserDataSaved;
        }
    }
}
