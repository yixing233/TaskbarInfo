using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace TaskbarInfo
{
    public class MediaManager
    {
        private readonly WindowsMediaController.MediaManager _mediaManager = new();
        private MediaSession? _currentSession;

        public event EventHandler<string>? MediaInfoChanged;
        public event EventHandler<TimeSpan>? PlaybackPositionChanged;
        public event EventHandler<GlobalSystemMediaTransportControlsSessionPlaybackStatus>? PlaybackStatusChanged;
        public event EventHandler<string>? AppIdChanged;

        public string CurrentAppAppUserModelId => _currentSession?.Id ?? "";
        public bool IsPlaying => _currentSession?.ControlSession.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        public System.Collections.Generic.List<string> FilterAppIds { get; set; } = new System.Collections.Generic.List<string>();

        public MediaManager()
        {
            _mediaManager.OnFocusedSessionChanged += MediaManager_OnFocusedSessionChanged;
            _mediaManager.OnAnySessionOpened += MediaManager_OnAnySessionOpened;
            _mediaManager.OnAnySessionClosed += MediaManager_OnAnySessionClosed;
            _mediaManager.OnAnyMediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;
            _mediaManager.OnAnyPlaybackStateChanged += MediaManager_OnAnyPlaybackStateChanged;
            _mediaManager.OnAnyTimelinePropertyChanged += MediaManager_OnAnyTimelinePropertyChanged;
        }

        public void Initialize()
        {
            try
            {
                _mediaManager.Start();
                UpdateCurrentSession();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MediaManager Init failed: {ex.Message}");
            }
        }

        private void MediaManager_OnFocusedSessionChanged(MediaSession? session)
        {
            UpdateCurrentSession();
        }

        private void MediaManager_OnAnySessionOpened(MediaSession session)
        {
            UpdateCurrentSession();
        }

        private void MediaManager_OnAnySessionClosed(MediaSession session)
        {
            UpdateCurrentSession();
        }

        private void MediaManager_OnAnyMediaPropertyChanged(MediaSession session, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
        {
            if (session == _currentSession)
            {
                Task.Run(() => UpdateMediaInfoAsync(session));
            }
        }

        private void MediaManager_OnAnyPlaybackStateChanged(MediaSession session, GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo)
        {
            if (session == _currentSession)
            {
                PlaybackStatusChanged?.Invoke(this, playbackInfo.PlaybackStatus);
            }
        }

        private void MediaManager_OnAnyTimelinePropertyChanged(MediaSession session, GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
        {
            if (session == _currentSession)
            {
                PlaybackPositionChanged?.Invoke(this, timelineProperties.Position);
            }
        }

        public void RefreshSession()
        {
            UpdateCurrentSession();
        }

        private void UpdateCurrentSession()
        {
            var desiredSession = GetDesiredSession();
            
            if (_currentSession != desiredSession)
            {
                _currentSession = desiredSession;
                
                if (_currentSession != null)
                {
                    var session = _currentSession;
                    Task.Run(() => UpdateMediaInfoAsync(session));
                    
                    var playbackInfo = _currentSession.ControlSession.GetPlaybackInfo();
                    if (playbackInfo != null)
                    {
                        PlaybackStatusChanged?.Invoke(this, playbackInfo.PlaybackStatus);
                    }
                    AppIdChanged?.Invoke(this, _currentSession.Id);
                }
                else
                {
                    MediaInfoChanged?.Invoke(this, "开始播放音乐吧");
                    AppIdChanged?.Invoke(this, "");
                }
            }
        }

        private MediaSession? GetDesiredSession()
        {
            var focused = _mediaManager.GetFocusedSession();
            if (focused != null && (FilterAppIds.Count == 0 || FilterAppIds.Contains(focused.Id)))
            {
                return focused;
            }

            foreach (var session in _mediaManager.CurrentMediaSessions.Values)
            {
                if (FilterAppIds.Count == 0 || FilterAppIds.Contains(session.Id))
                {
                    return session;
                }
            }
            return null;
        }

        public System.Collections.Generic.IEnumerable<string> GetCurrentSourceIds()
        {
            return _mediaManager.CurrentMediaSessions.Keys.ToList();
        }

        private async Task UpdateMediaInfoAsync(MediaSession session)
        {
            try
            {
                var info = await session.ControlSession.TryGetMediaPropertiesAsync();
                if (info != null)
                {
                    string artist = info.Artist;
                    string title = info.Title;
                    string display = string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
                    MediaInfoChanged?.Invoke(this, display);
                }
            }
            catch { }
        }

        public async Task PlayPauseAsync()
        {
            if (_currentSession == null) return;
            try
            {
                var status = _currentSession.ControlSession.GetPlaybackInfo().PlaybackStatus;
                if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    await _currentSession.ControlSession.TryPauseAsync();
                else
                    await _currentSession.ControlSession.TryPlayAsync();
            }
            catch { }
        }

        public async Task NextTrackAsync()
        {
            if (_currentSession == null) return;
            await _currentSession.ControlSession.TrySkipNextAsync();
        }

        public async Task PreviousTrackAsync()
        {
            if (_currentSession == null) return;
            await _currentSession.ControlSession.TrySkipPreviousAsync();
        }
    }
}
