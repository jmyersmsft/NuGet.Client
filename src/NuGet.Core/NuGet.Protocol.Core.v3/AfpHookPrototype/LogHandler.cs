using System;
using System.Threading.Tasks;
using HookComm;

namespace NuGet.Protocol.AfpHookPrototype
{
    class LogHandler : CommandHandlerBase<LogRequest, LogResponse>
    {
        private string name;

        public LogHandler(string name)
        {
            this.name = name;
        }

        public override Task<LogResponse> HandleRequest(LogRequest requestPayload, ICommandResponder commandResponder)
        {
            Console.WriteLine($"LOG FROM PLUGIN {name}: {requestPayload.Message}");
            return Task.FromResult(new LogResponse());
        }
    }
}