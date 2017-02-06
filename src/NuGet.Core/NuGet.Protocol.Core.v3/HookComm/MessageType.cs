namespace HookComm
{
    public enum MessageType
    {
        Request,
        ProgressResponse,
        IntermediateResultResponse,
        SuccessResponse,
        ErrorResponse,
        Cancel,
        Close
    }
}