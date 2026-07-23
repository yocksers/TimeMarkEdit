namespace TimeMarkEdit.Api
{
    public static class Routes
    {
        public const string GetEpisodeChapters = "/TimeMarkEdit/GetEpisodeChapters";
        public const string SaveEpisodeChapters = "/TimeMarkEdit/SaveEpisodeChapters";
        public const string FilterEpisodes = "/TimeMarkEdit/FilterEpisodes";
        public const string ApplySeasonMarks = "/TimeMarkEdit/ApplySeasonMarks";
        public const string GetSummary = "/TimeMarkEdit/GetSummary";
        public const string GetMkvChapters = "/TimeMarkEdit/GetMkvChapters";
        public const string ImportMkvChapters = "/TimeMarkEdit/ImportMkvChapters";
        public const string ImportMkvChaptersBulk = "/TimeMarkEdit/ImportMkvChaptersBulk";
        public const string DownloadIntroDbTimestamps = "/TimeMarkEdit/DownloadIntroDbTimestamps";
        public const string DownloadIntroDbTimestampsBulk = "/TimeMarkEdit/DownloadIntroDbTimestampsBulk";
        public const string TestIntroDbConnection = "/TimeMarkEdit/TestIntroDbConnection";
        public const string GetIntroDbConfig = "/TimeMarkEdit/GetIntroDbConfig";
        public const string SetIntroDbConfig = "/TimeMarkEdit/SetIntroDbConfig";
        public const string UploadIntroDbTimestamps = "/TimeMarkEdit/UploadIntroDbTimestamps";
    }
}
