namespace MediaDownloader
{
    public enum DownloadMode
    {
        BestVideoAndAudioMp4,
        BestVideoAndAudioMkv,
        BestAudioOnlyMp3,
        BestAudioOnlyM4a,
        BestAudioOnlyFlac
    }

    public enum VideoResolution
    {
        Best,
        Uhd2160p,
        Qhd1440p,
        Fhd1080p,
        Hd720p,
        Sd480p,
        Low360p
    }

    public enum QueueItemStatus
    {
        Queued,
        FetchingInfo,
        Downloading,
        Paused,
        Merging,
        Completed,
        Cancelled,
        Error
    }
}
