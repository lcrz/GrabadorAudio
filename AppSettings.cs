using System.Windows.Input;

namespace GrabadorAudio
{
    public class AppSettings
    {
        // Por defecto: Ctrl + Shift + S
        public ModifierKeys ScreenshotModifier { get; set; } = ModifierKeys.Control | ModifierKeys.Shift;
        public Key ScreenshotKey { get; set; } = Key.S;
        public string LastMicrophoneId { get; set; } = string.Empty;
        public string DefaultOutputFolder { get; set; } = string.Empty;
    }
}
