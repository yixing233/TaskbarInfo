using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Parsers;

namespace TaskbarInfo
{
    public class LyricsEngine
    {
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

        public async Task<bool> SearchAndLoadLyricsAsync(string artist, string title)
        {
            CurrentLyrics = new List<LyricLine>();

            // 1. Try QQ Music (QRC focus)
            if (await SearchAndLoadQQ(artist, title)) return true;

            // 2. Try Netease (LRC focus)
            if (await SearchAndLoadNetease(artist, title)) return true;

            return false;
        }

        private async Task<bool> SearchAndLoadQQ(string artist, string title)
        {
            try
            {
                var searcher = new QQMusicSearcher();
                var results = await searcher.SearchForResults($"{title} {artist}");
                if (results != null && results.Count > 0)
                {
                    var firstResult = (QQMusicSearchResult)results[0];
                    var response = await ProviderHelper.QQMusicApi.GetLyricsAsync(firstResult.Id);
                    if (response != null && !string.IsNullOrEmpty(response.Lyrics))
                    {
                        ParseLyrics(response.Lyrics);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private async Task<bool> SearchAndLoadNetease(string artist, string title)
        {
            try
            {
                var searcher = new NeteaseSearcher();
                var results = await searcher.SearchForResults($"{title} {artist}");
                if (results != null && results.Count > 0)
                {
                    var firstResult = (NeteaseSearchResult)results[0];
                    var response = await ProviderHelper.NeteaseApi.GetLyric(firstResult.Id);
                    if (response != null && response.Lrc != null && !string.IsNullOrEmpty(response.Lrc.Lyric))
                    {
                        ParseLyrics(response.Lrc.Lyric);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void ParseLyrics(string raw)
        {
            LyricsData? data = null;
            
            // 模仿 BetterLyrics 的检测逻辑
            if (raw.Contains("[mq:")) // QRC 特征
            {
                data = QrcParser.Parse(raw);
            }
            else if (raw.Contains("(") && raw.Contains(")")) // 常见 QRC/KRC 特征
            {
                // 尝试用 QrcParser 或 KrcParser
                data = QrcParser.Parse(raw);
                if (data?.Lines == null || data.Lines.Count == 0 || data.Lines.All(l => l.Text.Contains("(")))
                {
                    data = KrcParser.Parse(raw);
                }
            }
            
            // 兜底使用标准 LRC
            if (data?.Lines == null || data.Lines.Count == 0)
            {
                data = LrcParser.Parse(raw);
            }

            if (data != null)
            {
                LoadFromLyricsData(data);
            }
        }

        private void LoadFromLyricsData(LyricsData data)
        {
            var lines = new List<LyricLine>();
            if (data.Lines == null) return;

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
            CurrentLyrics = lines.OrderBy(l => l.StartMs).ToList();
            for (int i = 0; i < CurrentLyrics.Count; i++)
            {
                var line = CurrentLyrics[i];
                if (line.EndMs <= line.StartMs)
                {
                    line.EndMs = i + 1 < CurrentLyrics.Count
                        ? CurrentLyrics[i + 1].StartMs
                        : line.StartMs + Math.Max(1, line.Syllables.Sum(s => s.DurationMs));
                }
            }
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
