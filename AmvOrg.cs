using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AMVOrg
{
    public class AmvOrg : BasePlugin<AmvOrgPluginConfiguration>
    {
        public AmvOrg(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths,
            xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        // ReSharper disable once MemberCanBePrivate.Global
        public static AmvOrg? Instance { get; private set; }

        public override string Name => AmvOrgConstants.Name;

        public override Guid Id => Guid.Parse(AmvOrgConstants.Guid);

        public override PluginInfo GetPluginInfo()
        {
            return new PluginInfo(AmvOrgConstants.Name, new Version(0, 0, 1, 2), "AMV metadata provider", Id, true);
        }
    }
}