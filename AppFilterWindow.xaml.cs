using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TaskbarInfo
{
    public partial class AppFilterWindow : Window
    {
        private AppSettings _settings;
        private MediaManager _mediaManager;

        public class AppItem
        {
            public string AppId { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public bool IsSelected { get; set; }
        }

        public AppFilterWindow(AppSettings settings, MediaManager mediaManager)
        {
            InitializeComponent();
            _settings = settings;
            _mediaManager = mediaManager;
            
            // Set Icon
            this.Icon = App.GetAppIcon();

            LoadApps();
            
            CheckRunOnly.IsChecked = _settings.RunOnlyWithMusicApp;
        }

        private void LoadApps()
        {
            var savedIds = new HashSet<string>(_settings.IncludedAppIds);
            var runningIds = _mediaManager.GetCurrentSourceIds();
            
            // Combine: Running + Saved (Saved might not be running but we keep them so user can uncheck)
            var allIds = new HashSet<string>(savedIds);
            foreach(var id in runningIds) allIds.Add(id);
            
            var list = new List<AppItem>();
            foreach(var id in allIds)
            {
                list.Add(new AppItem
                {
                    AppId = id,
                    DisplayName = id.Contains("!") ? id.Split('!')[0] : id.Replace(".exe", ""), // Simple verify
                    IsSelected = savedIds.Contains(id)
                });
            }
            
            // Sort
            list.Sort((a,b) => a.DisplayName.CompareTo(b.DisplayName));
            
            AppList.ItemsSource = list;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadApps();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var list = AppList.ItemsSource as List<AppItem>;
            if (list != null)
            {
                _settings.IncludedAppIds.Clear();
                var processNames = new List<string>();

                foreach(var item in list)
                {
                    if (item.IsSelected)
                    {
                        _settings.IncludedAppIds.Add(item.AppId);
                        
                        // Use DisplayName as Process Name guess, or parse AppId
                        // DisplayName is currently: id.Replace(".exe", "") or split('!')
                        processNames.Add(item.DisplayName);
                    }
                }
                
                _settings.RunOnlyWithMusicApp = CheckRunOnly.IsChecked == true;
                _settings.MusicAppProcessNames = string.Join(",", processNames);

                _settings.Save(); 
            }
            
            DialogResult = true;
            Close();
        }
    }
}
