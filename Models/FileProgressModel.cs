namespace ListenLense.Models
{
    public class FileProgressModel
    {
        public double LastAudioPosition { get; set; } // in seconds
        public DateTime LastOpened { get; set; }
        // You could store more data (last highlighted “sentence index,” etc.)
    }
}
