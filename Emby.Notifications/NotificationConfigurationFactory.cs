using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Notifications;
using System.Collections.Generic;

namespace Emby.Notifications
{
    public class NotificationConfigurationFactory : IConfigurationFactory
    {
        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return new ConfigurationStore[]
            {
                new ConfigurationStore
                {
                    Key = "notifications",
                    ConfigurationType = typeof (NotificationOptions)
                }
            };
        }
    }
}
