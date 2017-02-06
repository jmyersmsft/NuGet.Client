namespace NuGet.Protocol.AfpHookPrototype
{
    public class AfpDownloaderHookRequest
    {
        public string MediaType { get; set; }
        public string AfpFile { get; set; }
        public string DownloadDir { get; set; }
    }
}