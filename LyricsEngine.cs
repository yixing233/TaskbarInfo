using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Parsers;
using Lyricify.Lyrics.Searchers.Helpers;
using LyricsSearcherKind = Lyricify.Lyrics.Searchers.Searchers;

namespace TaskbarInfo
{
    public class LyricsEngine
    {
        private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(7);
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LyricsX",
            "lyrics-cache");
        private readonly SemaphoreSlim _searchGate = new(1, 1);

        public enum SearchStatus
        {
            Loaded,
            NotFound,
            Timeout,
            NetworkError,
            Cancelled
        }

        public sealed record SearchResult(SearchStatus Status, string Source = "")
        {
            public bool IsSuccess => Status == SearchStatus.Loaded;

            public string StatusText => Status switch
            {
                SearchStatus.NotFound => "未找到歌词",
                SearchStatus.Timeout => "歌词服务超时",
                SearchStatus.NetworkError => "歌词服务暂时不可用",
                SearchStatus.Cancelled => "",
                _ => ""
            };
        }

        private enum RawLyricsFormat
        {
            Lrc,
            Qrc,
            Krc,
            Yrc
        }

        private sealed record ProviderLyrics(string Source, string Lyrics, RawLyricsFormat Format);
        private sealed record ProviderAttempt(ProviderLyrics? Result, bool TimedOut = false, bool Failed = false);
        private sealed record CachedLyrics(string Source, string Lyrics, RawLyricsFormat Format);

        public class SyllableInfo
        {
            public string Text { get; set; } = "";
            public int StartMs { get; set; }
            public int DurationMs { get; set; }
            public int EndMs => StartMs + DurationMs;
        }

        public class LyricLine
        {
            public int StartMs { get; set; }
            public int EndMs { get; set; }
            public string Text { get; set; } = "";
            public List<SyllableInfo> Syllables { get; set; } = new();
            
            // 是否有逐字信息
            public bool HasSyllables => Syllables != null && Syllables.Count > 0;
        }

        public List<LyricLine> CurrentLyrics { get; private set; } = new();

        public async Task<SearchResult> SearchAndLoadLyricsAsync(MediaTrackInfo track, CancellationToken cancellationToken)
        {
            bool lockTaken = false;

            try
            {
                await _searchGate.WaitAsync(cancellationToken);
                lockTaken = true;
                CurrentLyrics = new List<LyricLine>();
                if (!track.HasTrack)
                {
                    return new SearchResult(SearchStatus.NotFound);
                }

                var cached = await ReadCacheAsync(track, cancellationToken);
                if (cached != null && TryLoadLyrics(cached.Lyrics, cached.Format))
                {
                    return new SearchResult(SearchStatus.Loaded, cached.Source);
                }

                var metadata = new TrackMultiArtistMetadata
                {
                    Title = track.Title,
                    Artists = SplitArtists(track.Artist),
                    Album = track.Album,
                    DurationMs = track.DurationMs
                };

                var pending = new List<Task<ProviderAttempt>>
                {
                    RunProviderAsync(() => SearchQQAsync(metadata), cancellationToken),
                    RunProviderAsync(() => SearchNeteaseAsync(metadata), cancellationToken),
                    RunProviderAsync(() => SearchKugouAsync(metadata), cancellationToken),
                    RunProviderAsync(() => SearchLrclibAsync(metadata), cancellationToken)
                };

                bool anyTimeout = false;
                bool anyFailure = false;
                bool anyCompletedProvider = false;

                while (pending.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var completed = await Task.WhenAny(pending);
                    pending.Remove(completed);
                    var attempt = await completed;
                    anyTimeout |= attempt.TimedOut;
                    anyFailure |= attempt.Failed;
                    anyCompletedProvider |= !attempt.TimedOut && !attempt.Failed;

                    if (attempt.Result != null && TryLoadLyrics(attempt.Result.Lyrics, attempt.Result.Format))
                    {
                        await WriteCacheAsync(track, attempt.Result, cancellationToken);
                        return new SearchResult(SearchStatus.Loaded, attempt.Result.Source);
                    }
                }

                if (anyTimeout && !anyCompletedProvider)
                {
                    return new SearchResult(SearchStatus.Timeout);
                }

                if (anyFailure && !anyCompletedProvider)
                {
                    return new SearchResult(SearchStatus.NetworkError);
                }

                return new SearchResult(SearchStatus.NotFound);
            }
            catch (OperationCanceledException)
            {
                if (lockTaken)
                {
                    CurrentLyrics = new List<LyricLine>();
                }
                return new SearchResult(SearchStatus.Cancelled);
            }
            catch
            {
                if (lockTaken)
                {
                    CurrentLyrics = new List<LyricLine>();
                }
                return new SearchResult(SearchStatus.NetworkError);
            }
            finally
            {
                if (lockTaken)
                {
                    _searchGate.Release();
                }
            }
        }

        private static List<string> SplitArtists(string artist)
        {
            if (string.IsNullOrWhiteSpace(artist)) return new List<string>();

            return artist.Split(
                    new[] { ",", "，", "、", " & ", " feat. ", " ft. ", ";" },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<ProviderAttempt> RunProviderAsync(
            Func<Task<ProviderLyrics?>> provider,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await provider().WaitAsync(ProviderTimeout, cancellationToken);
                return new ProviderAttempt(result);
            }
            catch (TimeoutException)
            {
                return new ProviderAttempt(null, TimedOut: true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new ProviderAttempt(null, Failed: true);
            }
        }

        private static async Task<ProviderLyrics?> SearchQQAsync(TrackMultiArtistMetadata metadata)
        {
            var result = await SearchHelper.Search(
                metadata,
                LyricsSearcherKind.QQMusic,
                CompareHelper.MatchType.Medium);
            if (result is not QQMusicSearchResult qq) return null;

            var response = await ProviderHelper.QQMusicApi.GetLyricsAsync(qq.Id);
            return !string.IsNullOrWhiteSpace(response?.Lyrics)
                ? new ProviderLyrics("QQ音乐", response.Lyrics, RawLyricsFormat.Qrc)
                : null;
        }

        private static async Task<ProviderLyrics?> SearchNeteaseAsync(TrackMultiArtistMetadata metadata)
        {
            var result = await SearchHelper.Search(
                metadata,
                LyricsSearcherKind.Netease,
                CompareHelper.MatchType.Medium);
            if (result is not NeteaseSearchResult netease) return null;

            var response = await ProviderHelper.NeteaseApi.GetLyric(netease.Id);
            if (!string.IsNullOrWhiteSpace(response?.Yrc?.Lyric))
            {
                return new ProviderLyrics("网易云音乐", response.Yrc.Lyric, RawLyricsFormat.Yrc);
            }

            return !string.IsNullOrWhiteSpace(response?.Lrc?.Lyric)
                ? new ProviderLyrics("网易云音乐", response.Lrc.Lyric, RawLyricsFormat.Lrc)
                : null;
        }

        private static async Task<ProviderLyrics?> SearchKugouAsync(TrackMultiArtistMetadata metadata)
        {
            var result = await SearchHelper.Search(
                metadata,
                LyricsSearcherKind.Kugou,
                CompareHelper.MatchType.Medium);
            if (result is not KugouSearchResult kugou) return null;

            var response = await ProviderHelper.KugouApi.GetSearchLyrics(hash: kugou.Hash);
            var candidate = response?.Candidates?.OrderByDescending(item => item.Score).FirstOrDefault();
            if (candidate == null) return null;

            var raw = await Lyricify.Lyrics.Decrypter.Krc.Helper.GetLyricsAsync(
                candidate.Id,
                candidate.AccessKey);
            return !string.IsNullOrWhiteSpace(raw)
                ? new ProviderLyrics("酷狗音乐", raw, RawLyricsFormat.Krc)
                : null;
        }

        private static async Task<ProviderLyrics?> SearchLrclibAsync(TrackMultiArtistMetadata metadata)
        {
            var result = await SearchHelper.Search(
                metadata,
                LyricsSearcherKind.LRCLIB,
                CompareHelper.MatchType.Medium);
            if (result is not LRCLIBSearchResult lrclib) return null;

            var response = await ProviderHelper.LRCLIBApi.GetById(lrclib.Id);
            return !string.IsNullOrWhiteSpace(response?.SyncedLyrics)
                ? new ProviderLyrics("LRCLIB", response.SyncedLyrics, RawLyricsFormat.Lrc)
                : null;
        }

        private bool TryLoadLyrics(string raw, RawLyricsFormat format)
        {
            try
            {
                LyricsData? data = format switch
                {
                    RawLyricsFormat.Qrc => QrcParser.Parse(raw),
                    RawLyricsFormat.Krc => KrcParser.Parse(raw),
                    RawLyricsFormat.Yrc => YrcParser.Parse(raw),
                    _ => LrcParser.Parse(raw)
                };

                if (data?.Lines == null || data.Lines.Count == 0)
                {
                    return false;
                }

                var lines = ConvertLyricsData(data);
                if (lines.Count == 0) return false;
                CurrentLyrics = lines;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<CachedLyrics?> ReadCacheAsync(MediaTrackInfo track, CancellationToken token)
        {
            string path = GetCachePath(track);
            if (!File.Exists(path)) return null;

            try
            {
                string json = await File.ReadAllTextAsync(path, token);
                return JsonSerializer.Deserialize<CachedLyrics>(json);
            }
            catch
            {
                return null;
            }
        }

        private static async Task WriteCacheAsync(
            MediaTrackInfo track,
            ProviderLyrics lyrics,
            CancellationToken token)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                var cached = new CachedLyrics(lyrics.Source, lyrics.Lyrics, lyrics.Format);
                string json = JsonSerializer.Serialize(cached);
                await File.WriteAllTextAsync(GetCachePath(track), json, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }

        private static string GetCachePath(MediaTrackInfo track)
        {
            string key = $"{track.Title.Trim().ToLowerInvariant()}\n{track.Artist.Trim().ToLowerInvariant()}\n{track.Album.Trim().ToLowerInvariant()}";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            return Path.Combine(CacheDirectory, hash + ".json");
        }

        private static List<LyricLine> ConvertLyricsData(LyricsData data)
        {
            var lines = new List<LyricLine>();
            if (data.Lines == null) return lines;

            foreach (var iLine in data.Lines)
            {
                var line = new LyricLine
                {
                    StartMs = iLine.StartTime ?? 0,
                    EndMs = iLine.EndTime ?? 0,
                    Text = iLine.Text
                };

                // 处理音节信息
                if (iLine is SyllableLineInfo syllableLine)
                {
                    foreach (var s in syllableLine.Syllables)
                    {
                        line.Syllables.Add(new SyllableInfo
                        {
                            Text = s.Text,
                            StartMs = s.StartTime,
                            DurationMs = s.Duration
                        });
                    }
                }
                
                if (!string.IsNullOrWhiteSpace(line.Text))
                    lines.Add(line);
            }
            lines = lines.OrderBy(l => l.StartMs).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.EndMs <= line.StartMs)
                {
                    line.EndMs = i + 1 < lines.Count
                        ? lines[i + 1].StartMs
                        : line.StartMs + Math.Max(1, line.Syllables.Sum(s => s.DurationMs));
                }
            }
            return lines;
        }

        public (LyricLine? current, LyricLine? next) GetLyricsForTime(TimeSpan time)
        {
            if (CurrentLyrics.Count == 0) return (null, null);

            int ms = (int)time.TotalMilliseconds;
            var index = CurrentLyrics.FindLastIndex(l => l.StartMs <= ms);
            if (index == -1) return (null, CurrentLyrics[0]);

            var current = CurrentLyrics[index];
            var next = (index + 1 < CurrentLyrics.Count) ? CurrentLyrics[index + 1] : null;
            
            return (current, next);
        }

        // 获取当前行中某个字的进度百分比 (0-1)
        public double GetLineProgress(LyricLine line, TimeSpan time)
        {
            if (line == null || !line.HasSyllables) return 0;
            
            int ms = (int)time.TotalMilliseconds;
            if (ms < line.StartMs) return 0;
            if (ms >= line.EndMs) return 1;

            int durationMs = line.EndMs - line.StartMs;
            if (durationMs <= 0) return 0;

            // 查找当前进行到哪个音节了
            // 这种方式可以支持逐字高亮所需的百分比计算
            var lastSyllable = line.Syllables.LastOrDefault();
            if (lastSyllable != null && ms >= lastSyllable.EndMs) return 1;

            // 这里简单计算：已过音节数 / 总音节数 
            // 完美的实现需要计算像素宽度，但由于 WPF TextBlock 限制，
            // 我们先提供基于时间的音节定位。
            return (double)(ms - line.StartMs) / durationMs;
        }
    }
}
