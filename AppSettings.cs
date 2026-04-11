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
        public bool EnableOutline { get; set; } = false; // Simulated outline

        public int PositionMode { get; set; } = 0; // 0 = RIght (Tray), 1 = Left
        public int OffsetX { get; set; } = 10; 
        public bool IsDoubleLine { get; set; } = true; 
        public double LyricOffsetSeconds { get; set; } = 0; 
        public System.Collections.Generic.List<string> IncludedAppIds { get; set; } = new System.Collections.Generic.List<string>();  
        public bool EnableFloatingLyrics { get; set; } = false; 
        public bool FloatingLyricsLocked { get; set; } = false; 
        public bool FloatingLyricsClickThrough { get; set; } = false; 
        
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

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        public AppSettings Clone()
        {
            return (AppSettings)this.MemberwiseClone();
        }
    }
}
