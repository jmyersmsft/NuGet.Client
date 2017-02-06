using HookComm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NuGet.Protocol.AfpHookPrototype
{
    public class PluginProxy
    {
        public Connection Connection { get; }

        public PluginProxy(Connection connection)
        {
            Connection = connection;
        }

        public async Task<IEnumerable<string>> GetSupportedAfpTypes()
        {
            var response = await Connection
                .SendRequestAndWaitForResponseAsync
                <AfpDownloaderHookRegistrationRequest, AfpDownloaderHookRegistrationResponse>(
                    CommandNames.GetSupportedAfpTypes,
                    new AfpDownloaderHookRegistrationRequest());
            return response.MediaTypes;
        }

        public async Task<AfpDownloaderHookResponse> HandleAfp(string mediaType, string afpFile, string downloadDir)
        {
            return await Connection.SendRequestAndWaitForResponseAsync<AfpDownloaderHookRequest, AfpDownloaderHookResponse>(
                CommandNames.HandleAfp,
                new AfpDownloaderHookRequest { AfpFile = afpFile, MediaType = mediaType, DownloadDir = downloadDir });
        }
    }
}
