using System.Collections.Generic;

namespace NuGet.Protocol.AfpHookPrototype
{
    public class AfpDownloaderHookResponse
    {
        public IEnumerable<string> FileList { get; set; }
    }
}