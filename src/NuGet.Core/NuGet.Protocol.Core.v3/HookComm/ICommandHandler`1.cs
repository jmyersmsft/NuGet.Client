using System.Threading.Tasks;

namespace HookComm
{
    public interface ICommandHandler<TRequest, TResponse> : ICommandHandler
    {
        Task<TResponse> HandleRequest(TRequest requestPayload, ICommandResponder commandResponder);
    }
}