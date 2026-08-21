using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarInfo
{
    public class AudioPlaybackDevice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsDefault { get; set; }
        public string IconGlyph { get; set; } = "\ue0f1"; // Lucide headphones
    }

    public class AudioVolumeChangedEventArgs : EventArgs
    {
        public float MasterVolume { get; set; }
        public bool IsMuted { get; set; }
    }

    public class AudioDeviceService : IDisposable
    {
        public event EventHandler<AudioVolumeChangedEventArgs>? VolumeChanged;
        public event EventHandler? DefaultDeviceChanged;

        private IMMDeviceEnumerator? _enumerator;
        private IMMDevice? _defaultRenderDevice;
        private IAudioEndpointVolume? _endpointVolume;
        private AudioEndpointVolumeCallback? _volumeCallback;
        private bool _isDisposed;
        private readonly object _lock = new();
        private static readonly Guid _ourEventContext = Guid.NewGuid();

        public AudioDeviceService()
        {
            try
            {
                InitializeEndpointVolume();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AudioDeviceService init failed: {ex.Message}");
            }
        }

        private void InitializeEndpointVolume()
        {
            lock (_lock)
            {
                try
                {
                    if (_endpointVolume != null && _volumeCallback != null)
                    {
                        try { _endpointVolume.UnregisterControlChangeNotify(_volumeCallback); } catch { }
                    }
                    _volumeCallback = null;
                    _endpointVolume = null;
                    _defaultRenderDevice = null;

                    _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
                    int hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out _defaultRenderDevice);
                    if (hr != 0 || _defaultRenderDevice == null) return;

                    var iid = typeof(IAudioEndpointVolume).GUID;
                    hr = _defaultRenderDevice.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var volObj);
                    if (hr == 0 && volObj is IAudioEndpointVolume vol)
                    {
                        _endpointVolume = vol;
                        _volumeCallback = new AudioEndpointVolumeCallback(this);
                        _endpointVolume.RegisterControlChangeNotify(_volumeCallback);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"InitializeEndpointVolume error: {ex.Message}");
                }
            }
        }

        public float GetMasterVolume()
        {
            lock (_lock)
            {
                try
                {
                    if (_endpointVolume == null) InitializeEndpointVolume();
                    if (_endpointVolume != null)
                    {
                        int hr = _endpointVolume.GetMasterVolumeLevelScalar(out float level);
                        if (hr == 0 && !float.IsNaN(level) && !float.IsInfinity(level))
                        {
                            return Math.Clamp(level, 0.0f, 1.0f);
                        }
                    }
                }
                catch
                {
                    InitializeEndpointVolume();
                }
                return 0.5f;
            }
        }

        private float _lastSetVolume = -1f;
        private long _lastSetVolumeTicks = 0;

        public bool SetMasterVolume(float level)
        {
            level = Math.Clamp(level, 0.0f, 1.0f);

            long now = Stopwatch.GetTimestamp();
            if (Math.Abs(level - _lastSetVolume) < 0.003f && (now - _lastSetVolumeTicks) * 1000 / Stopwatch.Frequency < 12)
            {
                return true;
            }

            lock (_lock)
            {
                try
                {
                    if (_endpointVolume == null) InitializeEndpointVolume();
                    if (_endpointVolume != null)
                    {
                        var ctx = _ourEventContext;
                        int hr = _endpointVolume.SetMasterVolumeLevelScalar(level, ref ctx);
                        if (hr == 0)
                        {
                            _lastSetVolume = level;
                            _lastSetVolumeTicks = Stopwatch.GetTimestamp();
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SetMasterVolume error: {ex.Message}");
                }
                return false;
            }
        }

        public bool IsMuted()
        {
            lock (_lock)
            {
                try
                {
                    if (_endpointVolume == null) InitializeEndpointVolume();
                    if (_endpointVolume != null)
                    {
                        int hr = _endpointVolume.GetMute(out bool isMuted);
                        if (hr == 0) return isMuted;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"IsMuted error: {ex.Message}");
                }
                return false;
            }
        }

        public bool SetMute(bool isMuted)
        {
            lock (_lock)
            {
                try
                {
                    if (_endpointVolume == null) InitializeEndpointVolume();
                    if (_endpointVolume != null)
                    {
                        var ctx = _ourEventContext;
                        int hr = _endpointVolume.SetMute(isMuted, ref ctx);
                        return hr == 0;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SetMute error: {ex.Message}");
                }
                return false;
            }
        }

        public bool ToggleMute()
        {
            bool current = IsMuted();
            SetMute(!current);
            return !current;
        }

        public List<AudioPlaybackDevice> GetPlaybackDevices()
        {
            var result = new List<AudioPlaybackDevice>();
            try
            {
                _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
                string currentDefaultId = "";
                if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defDev) == 0 && defDev != null)
                {
                    defDev.GetId(out currentDefaultId);
                }

                int hr = _enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out var collection);
                if (hr == 0 && collection != null)
                {
                    collection.GetCount(out int count);
                    var friendlyNameKey = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

                    for (int i = 0; i < count; i++)
                    {
                        if (collection.Item(i, out var dev) == 0 && dev != null)
                        {
                            dev.GetId(out string devId);
                            string name = "音频输出设备";
                            if (dev.OpenPropertyStore(0 /* STGM_READ */, out var store) == 0 && store != null)
                            {
                                store.GetValue(ref friendlyNameKey, out var pv);
                                if (pv.pwszVal != IntPtr.Zero)
                                {
                                    name = Marshal.PtrToStringUni(pv.pwszVal) ?? name;
                                }
                            }

                            bool isDefault = !string.IsNullOrEmpty(devId) && devId.Equals(currentDefaultId, StringComparison.OrdinalIgnoreCase);
                            string glyph = DetermineIconForDevice(name);

                            result.Add(new AudioPlaybackDevice
                            {
                                Id = devId,
                                Name = name,
                                IsDefault = isDefault,
                                IconGlyph = glyph
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPlaybackDevices error: {ex.Message}");
            }
            return result;
        }

        public bool SetDefaultPlaybackDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            try
            {
                bool success = SetDefaultEndpointInternal(deviceId);
                if (success)
                {
                    InitializeEndpointVolume();
                    DefaultDeviceChanged?.Invoke(this, EventArgs.Empty);
                }
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetDefaultPlaybackDevice error: {ex.Message}");
                return false;
            }
        }

        private static bool SetDefaultEndpointInternal(string deviceId)
        {
            try
            {
                var policyConfig = (IPolicyConfig)new CPolicyConfigClient();
                policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
                policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetDefaultEndpointInternal COM error: {ex.Message}");
                return false;
            }
        }

        public static string DetermineIconForDevice(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("headset") || lower.Contains("头戴"))
            {
                return "\ue5bd"; // Lucide headset
            }
            if (lower.Contains("headphone") || lower.Contains("耳机") || lower.Contains("airpods") || lower.Contains("wh-1000") || lower.Contains("earbuds") || lower.Contains("buds") || lower.Contains("earphone"))
            {
                return "\ue0f1"; // Lucide headphones
            }
            if (lower.Contains("bluetooth") || lower.Contains("蓝牙"))
            {
                return "\ue05c"; // Lucide bluetooth
            }
            if (lower.Contains("monitor") || lower.Contains("display") || lower.Contains("hdmi") || lower.Contains("dp") || lower.Contains("显示器") || lower.Contains("屏幕"))
            {
                return "\ue210"; // Lucide monitor-speaker
            }
            if (lower.Contains("usb") || lower.Contains("type-c") || lower.Contains("dac"))
            {
                return "\ue356"; // Lucide usb
            }
            return "\ue166"; // Lucide speaker (default)
        }

        internal void OnVolumeNotification(float volume, bool muted)
        {
            VolumeChanged?.Invoke(this, new AudioVolumeChangedEventArgs
            {
                MasterVolume = volume,
                IsMuted = muted
            });
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try
            {
                if (_endpointVolume != null && _volumeCallback != null)
                {
                    _endpointVolume.UnregisterControlChangeNotify(_volumeCallback);
                }
            }
            catch { }
        }

        #region COM Interop Definitions

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        private enum EDataFlow { eRender, eCapture, eAll }
        private enum ERole { eConsole, eMultimedia, eCommunications }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
            [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
            [PreserveSig] int GetDevice(string pwstrId, out IMMDevice ppDevice);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int pcDevices);
            [PreserveSig] int Item(int nDevice, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            [PreserveSig] int GetState(out int pdwState);
        }

        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int cProps);
            [PreserveSig] int GetAt(int iProp, out PropertyKey pkey);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant pv);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant propvar);
            [PreserveSig] int Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pwszVal;
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
            [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
            [PreserveSig] int GetChannelCount(out uint pnChannelCount);
            [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
            [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        [Guid("657804FA-D6AD-4496-8A60-522725206673"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolumeCallback
        {
            [PreserveSig]
            int OnNotify(IntPtr pNotifyData);
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct AUDIO_VOLUME_NOTIFICATION_DATA
        {
            [FieldOffset(0)] public Guid guidEventContext;
            [FieldOffset(16)] public int bMuted;
            [FieldOffset(20)] public float fMasterVolume;
            [FieldOffset(24)] public uint nChannels;
        }

        private class AudioEndpointVolumeCallback : IAudioEndpointVolumeCallback
        {
            private readonly AudioDeviceService _service;
            public AudioEndpointVolumeCallback(AudioDeviceService service) => _service = service;

            [PreserveSig]
            public int OnNotify(IntPtr pNotifyData)
            {
                if (pNotifyData == IntPtr.Zero) return 0;
                try
                {
                    var data = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(pNotifyData);
                    if (data.guidEventContext == _ourEventContext)
                    {
                        return 0; // Skip our own echoed notifications
                    }

                    float vol = data.fMasterVolume;
                    if (float.IsNaN(vol) || float.IsInfinity(vol)) vol = 0.5f;
                    bool isMuted = data.bMuted != 0;
                    _service.OnVolumeNotification(Math.Clamp(vol, 0f, 1f), isMuted);
                }
                catch { }
                return 0;
            }
        }

        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        private class CPolicyConfigClient { }

        [Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr ppFormat);
            [PreserveSig] int GetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [In, MarshalAs(UnmanagedType.Bool)] bool bDefault, IntPtr ppFormat);
            [PreserveSig] int ResetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
            [PreserveSig] int SetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
            [PreserveSig] int GetProcessingPeriod([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [In, MarshalAs(UnmanagedType.Bool)] bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
            [PreserveSig] int SetProcessingPeriod([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
            [PreserveSig] int GetShareMode([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
            [PreserveSig] int SetShareMode([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
            [PreserveSig] int GetPropertyValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [In, MarshalAs(UnmanagedType.Bool)] bool bFxStore, ref PropertyKey key, out PropVariant pv);
            [PreserveSig] int SetPropertyValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [In, MarshalAs(UnmanagedType.Bool)] bool bFxStore, ref PropertyKey key, ref PropVariant pv);
            [PreserveSig] int SetDefaultEndpoint([In, MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, [In] ERole eRole);
            [PreserveSig] int SetEndpointVisibility([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [In, MarshalAs(UnmanagedType.Bool)] bool bVisible);
        }

        #endregion
    }
}
