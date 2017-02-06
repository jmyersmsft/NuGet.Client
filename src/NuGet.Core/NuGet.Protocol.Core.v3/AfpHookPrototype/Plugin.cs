using HookComm;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NuGet.Protocol.AfpHookPrototype
{
    public class Plugin : IDisposable
    {
        public string Name { get; }
        public string Path { get; }

        public Connection Connection { get; }

        public PluginProxy Proxy { get; }


        public Plugin(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);


            var logger = new StdErrLogger("H");
            var processStartInfo = new ProcessStartInfo(Path)
            {
                UseShellExecute = false,
                RedirectStandardError = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            };


            var process = Process.Start(processStartInfo);

            var sender = new Sender(process.StandardInput, logger);
            var receiver = new Receiver(process.StandardOutput, logger);

            var handlers = new Dictionary<string, ICommandHandler>
            {
                {CommandNames.Log, new LogHandler(Name)}
            }.ToImmutableDictionary();

            Connection = new Connection(sender, receiver, handlers, logger);

            Connection.ConnectAsync().GetAwaiter().GetResult();

            Proxy = new PluginProxy(Connection);
        }

        public void Dispose()
        {
            Connection.CloseAsync().GetAwaiter().GetResult();
        }
    }
}
