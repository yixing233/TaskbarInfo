using System;
using System.IO;
using System.Text.Json;

namespace TaskbarInfo
{
    public class AppSettings
    {
        public double Width { get; set; } = 400;
        public double FontSize { get; set; } = 12;
        public string FontFamily { get; set; } = "Microsoft YaHei";
        public string TextColor { get; set; } = "#FFFFFF"; // Hex code
        public string ActiveTextColor { get; set; } = "#FF33BBFF"; // Highlight color for played lyrics
        public string BackgroundColor { get; set; } = "#33000000"; // Hex code
        public bool EnableShadow { get; set; } = false;
        public string FontWeight { get; set; } = "SemiBold"; // Normal, SemiBold, Bold etc.
        public bool EnableOutline { get; set; } = false; // Simulated outline

        public int OffsetX { get; set; } = 10; 
        public bool IsDoubleLine { get; set; } = true; 
        public double LyricOffsetSeconds { get; set; } = 0; 
        public System.Collections.Generic.List<string> IncludedAppIds { get; set; } = new System.Collections.Generic.List<string>();  
        public bool EnableFloatingLyrics { get; set; } = false; 
        public bool FloatingLyricsLocked { get; set; } = false; 
        public bool FloatingLyricsClickThrough { get; set; } = false; 
        public string FloatingLyricsFontFamily { get; set; } = "Microsoft YaHei";
        public double FloatingLyricsFontSize { get; set; } = 20;
        public string FloatingLyricsFontWeight { get; set; } = "Bold";
        public string FloatingLyricsTextColor { get; set; } = "#FF1F2937";
        public string FloatingLyricsBackgroundColor { get; set; } = "#FFFFFFFF";
        public bool FloatingLyricsUseAcrylic { get; set; } = false;
        public bool FloatingLyricsEnableShadow { get; set; } = true;
        public double? FloatingLyricsLeft { get; set; } = null;
        public double? FloatingLyricsTop { get; set; } = null;
        
        public double NextLyricFontSizeDiff { get; set; } = 2.0;
        public string NextLyricFontWeight { get; set; } = "Normal"; // Normal, Light, Bold etc. 

        public bool RunOnlyWithMusicApp { get; set; } = false;
        public string MusicAppProcessNames { get; set; } = "QQMusic,cloudmusic,Spotify,YesPlayMusic,Foobar2000"; 
        public bool AutoCheckUpdates { get; set; } = true;

        private static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public bool Save()
        {
            return Save(out _);
        }

        public bool Save(out string? errorMessage)
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public AppSettings Clone()
        {
            var clone = (AppSettings)this.MemberwiseClone();
            clone.IncludedAppIds = new System.Collections.Generic.List<string>(IncludedAppIds);
            return clone;
        }
    }
}
