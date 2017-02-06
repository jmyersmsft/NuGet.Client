using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HookComm
{
    public interface ICommandHandler
    {
        Task<JToken> HandleRequestAsync(JToken requestPayload, ICommandResponder commandResponder);
    }
}