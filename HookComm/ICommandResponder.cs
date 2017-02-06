namespace HookComm
{
    public interface ICommandResponder
    {
        Connection Connection { get; }

        //Task SendIntermediateResponse(JToken payload);
        //Task SendProgress(double percentComplete, string status);
    }
}