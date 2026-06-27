using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace GrabadorAudio
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;
        private bool _isRecordingHotkey = false;

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            _settings = currentSettings;
            UpdateHotkeyDisplay();
            
            FolderPathTextBox.Text = string.IsNullOrEmpty(_settings.DefaultOutputFolder) 
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GrabacionesAudio")
                : _settings.DefaultOutputFolder;
        }

        private void UpdateHotkeyDisplay()
        {
            string modifierText = "";
            if ((_settings.ScreenshotModifier & ModifierKeys.Control) == ModifierKeys.Control) modifierText += "Ctrl + ";
            if ((_settings.ScreenshotModifier & ModifierKeys.Shift) == ModifierKeys.Shift) modifierText += "Shift + ";
            if ((_settings.ScreenshotModifier & ModifierKeys.Alt) == ModifierKeys.Alt) modifierText += "Alt + ";
            if ((_settings.ScreenshotModifier & ModifierKeys.Windows) == ModifierKeys.Windows) modifierText += "Win + ";

            CurrentHotkeyText.Text = $"{modifierText}{_settings.ScreenshotKey}";
        }

        private void RecordHotkeyBtn_Click(object sender, RoutedEventArgs e)
        {
            _isRecordingHotkey = true;
            ListeningText.Visibility = Visibility.Visible;
            RecordHotkeyBtn.IsEnabled = false;
            this.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_isRecordingHotkey)
            {
                // Ignorar si solo se presiona un modificador
                if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                    e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                    e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                    e.Key == Key.System)
                {
                    return;
                }

                _settings.ScreenshotModifier = Keyboard.Modifiers;
                _settings.ScreenshotKey = e.Key == Key.System ? e.SystemKey : e.Key;

                UpdateHotkeyDisplay();
                
                _isRecordingHotkey = false;
                ListeningText.Visibility = Visibility.Collapsed;
                RecordHotkeyBtn.IsEnabled = true;
                
                // Guardar la configuración
                SettingsManager.Save(_settings);
                
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BrowseFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string initialDir = string.IsNullOrEmpty(_settings.DefaultOutputFolder)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GrabacionesAudio")
                    : _settings.DefaultOutputFolder;

                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Seleccionar carpeta por defecto",
                    InitialDirectory = initialDir
                };

                if (dialog.ShowDialog() == true)
                {
                    _settings.DefaultOutputFolder = dialog.FolderName;
                    FolderPathTextBox.Text = _settings.DefaultOutputFolder;
                    SettingsManager.Save(_settings);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al seleccionar carpeta: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
