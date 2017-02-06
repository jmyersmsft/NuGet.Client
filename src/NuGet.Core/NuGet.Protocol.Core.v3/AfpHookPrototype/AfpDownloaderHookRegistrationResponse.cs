using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Protocol.AfpHookPrototype
{
    public class AfpDownloaderHookRegistrationResponse
    {
        public IEnumerable<string> MediaTypes { get; set; }
    }
}
