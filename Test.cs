using System;
using System.Threading;
using Jellyfin.Plugin.AMVOrg.Providers;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AMVOrg
{
    internal static class Test
    {
        [STAThread]
        private static void Main()
        {
            Console.Out.WriteLine("Test");
            var l = new NullLoggerFactory().CreateLogger<AmvOrgProvider>();
            var p = new AmvOrgProvider(null!, l);
            var mi = new MusicVideoInfo
            {
                Name = "Iriya no sora, UFO no natsu - Eve 6 - Here's To The Night",
                Path = "/media/iv/Iriya no sora, UFO no natsu - Eve 6 - Here's To The Night.mp4"
            };
            // mi.SetProviderId(AmvOrgConstants.Name, "85834");
            var mr = p.GetMetadata(mi, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}