using System;

namespace TaskbarInfo
{
    public sealed class MediaTrackInfo : EventArgs
    {
        public string Title { get; init; } = "";
        public string Artist { get; init; } = "";
        public string Album { get; init; } = "";
        public int? DurationMs { get; init; }
        public string SourceAppId { get; init; } = "";
        public string StatusText { get; init; } = "";
        public byte[]? AlbumArtBytes { get; init; }

        public bool HasTrack => !string.IsNullOrWhiteSpace(Title);

        public string DisplayText => HasTrack
            ? (string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}")
            : (string.IsNullOrWhiteSpace(StatusText) ? "开始播放音乐吧" : StatusText);
    }
}
