using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HookComm
{
    public abstract class CommandHandlerBase<TRequest, TResponse> : ICommandHandler, ICommandHandler<TRequest, TResponse>
    {
        async Task<JToken> ICommandHandler.HandleRequestAsync(JToken requestPayload, ICommandResponder commandResponder)
        {
            var typedRequest = requestPayload.ToObject<TRequest>();
            var typedResponse = await HandleRequest(typedRequest, commandResponder);
            var jsonResponse = JToken.FromObject(typedResponse);
            return jsonResponse;
        }

        public abstract Task<TResponse> HandleRequest(TRequest requestPayload, ICommandResponder commandResponder);
    }
}