using Newtonsoft.Json;

namespace SimpleDataUploader
{
    public class UploadDataEntity
    {
        public class BeatmapInfoEntity
        {
            public string Artist { get; set; }
            public string ArtistUnicode { get; set; }
            public string Title { get; set; }
            public string TitleUnicode { get; set; }
            public string Version { get; set; }
            public string Creator { get; set; }
        }

        public BeatmapInfoEntity BeatmapInfo { get; set; }
        public double PP { get; set; }
        public double StarRatio { get; set; }
        public double? ACC { get; set; }
        public double UR { get; set; }
        public string OsuFileHash { get; set; }
        public string PlayerName { get; set; }
        public int? BeatmapID { get; set; }
        public int? BeatmapSetID { get; set; }

        /// <summary>
        /// 仅仅拿来对比录像分数
        /// </summary>
        [JsonIgnore]
        public int Score { get; set; }

        public override string ToString() => $"({PlayerName}) {BeatmapSetID}/{BeatmapID} {BeatmapInfo.ArtistUnicode} - {BeatmapInfo.TitleUnicode} [{BeatmapInfo.Version}]";
    }
}