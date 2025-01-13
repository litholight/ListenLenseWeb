namespace ListenLense.Models
{
    public class FileStateModel
    {
        public double LastAudioPosition { get; set; } // in seconds
        public DateTime LastOpened { get; set; }
        public bool Autoscroll { get; set; } = true;
        public bool DarkMode { get; set; } = false;
    }
}
