namespace TaskbarInfo
{
    public sealed class FloatingLyricsResizeCoordinator
    {
        private int _marqueeUpdateVersion;

        public bool IsNativeWidthResizing { get; private set; }

        public void BeginNativeWidthResize()
        {
            IsNativeWidthResizing = true;
            _marqueeUpdateVersion++;
        }

        public void EndNativeWidthResize()
        {
            IsNativeWidthResizing = false;
            _marqueeUpdateVersion++;
        }

        public int ScheduleMarqueeUpdate()
        {
            return ++_marqueeUpdateVersion;
        }

        public bool CanApplyMarqueeUpdate(int version)
        {
            return !IsNativeWidthResizing && version == _marqueeUpdateVersion;
        }
    }
}
