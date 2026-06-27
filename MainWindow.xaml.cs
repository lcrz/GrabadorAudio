using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace GrabadorAudio
{
    public partial class MainWindow : Window
    {
        private readonly AudioEngine _audioEngine;
        private readonly DispatcherTimer _recordTimer;
        private DateTime _recordStartTime;
        private string _activeFolder = string.Empty;
        private AppSettings _appSettings;
        private IntPtr _windowHandle;
        
        // Colección observable para la lista lateral
        public ObservableCollection<RecordingItem> RecordingList { get; }

        public MainWindow()
        {
            InitializeComponent();
            
            _appSettings = SettingsManager.Load();
            _audioEngine = new AudioEngine();
            RecordingList = new ObservableCollection<RecordingItem>();
            RecordingsListBox.ItemsSource = RecordingList;

            // Timer para el cronómetro de grabación (actualización cada 500 ms para mayor precisión)
            _recordTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _recordTimer.Tick += RecordTimer_Tick;

            // Registrar eventos de volumen
            _audioEngine.MicrophoneVolumeCalculated += OnMicrophoneVolumeCalculated;
            _audioEngine.SystemVolumeCalculated += OnSystemVolumeCalculated;

            // Cargar estado inicial
            Loaded += MainWindow_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;
            HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312 && wParam.ToInt32() == GlobalHotkey.HOTKEY_ID) // WM_HOTKEY
            {
                TakeScreenshot();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_appSettings.DefaultOutputFolder))
            {
                _activeFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                    "GrabacionesAudio"
                );
            }
            else
            {
                _activeFolder = _appSettings.DefaultOutputFolder;
            }
            OutputFolderTextBox.Text = _activeFolder;

            // Listar micrófonos
            LoadMicrophoneDevices();

            // Suscribir al evento de cambio de selección después de la carga inicial
            MicComboBox.SelectionChanged += MicComboBox_SelectionChanged;

            // Listar historial de grabaciones
            LoadRecordingsHistory();

            // Inicializar combo de captura de ventana
            RefreshWindowList();
        }

        private void LoadMicrophoneDevices()
        {
            MicComboBox.Items.Clear();
            var devices = AudioEngine.GetInputDevices();
            
            foreach (var device in devices)
            {
                MicComboBox.Items.Add(new ComboBoxDeviceItem(device));
            }

            if (MicComboBox.Items.Count > 0)
            {
                bool selectedSaved = false;
                if (!string.IsNullOrEmpty(_appSettings.LastMicrophoneId))
                {
                    for (int i = 0; i < MicComboBox.Items.Count; i++)
                    {
                        if (MicComboBox.Items[i] is ComboBoxDeviceItem item && item.Device.ID == _appSettings.LastMicrophoneId)
                        {
                            MicComboBox.SelectedIndex = i;
                            selectedSaved = true;
                            break;
                        }
                    }
                }

                if (!selectedSaved)
                {
                    MicComboBox.SelectedIndex = 0;
                }
            }
            else
            {
                MicComboBox.Items.Add("No se detectaron micrófonos");
                MicComboBox.SelectedIndex = 0;
                MicComboBox.IsEnabled = false;
                MicCheckBox.IsChecked = false;
                MicCheckBox.IsEnabled = false;
            }
        }

        private void MicComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MicComboBox.SelectedItem is ComboBoxDeviceItem selectedItem)
            {
                _appSettings.LastMicrophoneId = selectedItem.Device.ID;
                SettingsManager.Save(_appSettings);
            }
        }

        private void LoadRecordingsHistory()
        {
            RecordingList.Clear();
            if (!Directory.Exists(_activeFolder))
            {
                return;
            }

            try
            {
                var directoryInfo = new DirectoryInfo(_activeFolder);
                var files = directoryInfo.GetFiles("*.*", SearchOption.AllDirectories)
                    .Where(f => f.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || 
                                 f.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.CreationTime);

                foreach (var file in files)
                {
                    double sizeInMb = file.Length / (1024.0 * 1024.0);
                    string details = $"{file.CreationTime:dd/MM/yyyy HH:mm} - {sizeInMb:F2} MB";
                    
                    RecordingList.Add(new RecordingItem
                    {
                        Name = file.Name,
                        Path = file.FullName,
                        Details = details
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar el historial: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // MANEJO DE EVENTOS DE AUDIO EN TIEMPO REAL
        // ==========================================

        private void OnMicrophoneVolumeCalculated(float volume)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MicVolumeMeter.Value = volume * 100;
                MicLevelText.Text = $"{(int)(volume * 100)}%";
            }));
        }

        private void OnSystemVolumeCalculated(float volume)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SysVolumeMeter.Value = volume * 100;
                SysLevelText.Text = $"{(int)(volume * 100)}%";
            }));
        }

        // ==========================================
        // CONTROL DE VENTANA (CHROMELESS)
        // ==========================================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            GlobalHotkey.Unregister(_windowHandle);
            _audioEngine.StopPlayback();
            Close();
        }

        // ==========================================
        // EVENTOS DEL CRONÓMETRO DE GRABACIÓN
        // ==========================================

        private void RecordTimer_Tick(object? sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _recordStartTime;
            TimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
        }

        // ==========================================
        // BOTONES E INTERACCIÓN DE GRABACIÓN
        // ==========================================

        private async void RecordBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_audioEngine.IsRecording) return;

            bool recordMic = MicCheckBox.IsChecked == true;
            bool recordSys = SystemAudioCheckBox.IsChecked == true;

            if (!recordMic && !recordSys)
            {
                MessageBox.Show("Debes seleccionar al menos una fuente de audio (Micrófono o Sistema).", 
                                "Configuración requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MMDevice? selectedMic = null;
            if (recordMic && MicComboBox.SelectedItem is ComboBoxDeviceItem selectedItem)
            {
                selectedMic = selectedItem.Device;
            }

            try
            {
                // Detener cualquier reproducción en curso
                _audioEngine.StopPlayback();
                PlaySelectedButton.Content = "Reproducir Selección";
                StopPlayButton.IsEnabled = false;

                // Configurar controles UI durante grabación
                SetUiEnabled(false);
                StatusText.Text = "Grabando...";
                
                // Cambiar el icono del botón de grabar a uno de pausa/activo
                // (usaremos un circulo pequeño parpadeante o lo dejaremos claro)
                RecordBtnIcon.Data = Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M9,9H15V15H9V9Z"); // Icono de Stop/Pausa cuadrado en círculo
                StopBtn.IsEnabled = true;

                // Iniciar motor de audio
                _audioEngine.MicVolume = (float)MicVolumeSlider.Value;
                _audioEngine.SysVolume = (float)SysVolumeSlider.Value;
                _audioEngine.StartRecording(selectedMic, recordMic, recordSys);

                // Iniciar temporizador
                _recordStartTime = DateTime.Now;
                TimerText.Text = "00:00:00";
                _recordTimer.Start();
                
                // Registrar atajo global
                GlobalHotkey.Register(_windowHandle, _appSettings.ScreenshotModifier, _appSettings.ScreenshotKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error al iniciar grabación", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetUiState();
            }
        }

        private async void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_audioEngine.IsRecording) return;

            StatusText.Text = "Procesando y mezclando audio...";
            StopBtn.IsEnabled = false;

            try
            {
                string format = Mp3Radio.IsChecked == true ? "mp3" : "wav";
                string cleanLabel = GetCleanLabel();
                string targetFolder = _activeFolder;
                if (!string.IsNullOrEmpty(cleanLabel))
                {
                    targetFolder = Path.Combine(_activeFolder, cleanLabel);
                }
                string finalFile = await _audioEngine.StopRecordingAsync(targetFolder, format, cleanLabel);

                StatusText.Text = "Grabación finalizada correctamente.";
                
                // Recargar historial
                LoadRecordingsHistory();

                // Seleccionar automáticamente la nueva grabación
                var newItem = RecordingList.FirstOrDefault(r => r.Path == finalFile);
                if (newItem != null)
                {
                    RecordingsListBox.SelectedItem = newItem;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error al guardar grabación", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error al procesar la grabación.";
            }
            finally
            {
                ResetUiState();
            }
        }

        private void ResetUiState()
        {
            _recordTimer.Stop();
            GlobalHotkey.Unregister(_windowHandle);
            
            // Restablecer medidores
            MicVolumeMeter.Value = 0;
            MicLevelText.Text = "0%";
            SysVolumeMeter.Value = 0;
            SysLevelText.Text = "0%";
            
            // Restablecer icono de grabar
            RecordBtnIcon.Data = Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,9A3,3 0 0,1 15,12A3,3 0 0,1 12,15A3,3 0 0,1 9,12A3,3 0 0,1 12,9Z");
            StopBtn.IsEnabled = false;

            SetUiEnabled(true);
        }

        private void SetUiEnabled(bool enabled)
        {
            RecordBtn.IsEnabled = enabled;
            MicCheckBox.IsEnabled = enabled && MicComboBox.Items.Count > 0 && MicComboBox.IsEnabled;
            MicComboBox.IsEnabled = enabled && MicCheckBox.IsChecked == true;
            SystemAudioCheckBox.IsEnabled = enabled;
            Mp3Radio.IsEnabled = enabled;
            WavRadio.IsEnabled = enabled;
            BrowseButton.IsEnabled = enabled;
            RecordingsListBox.IsEnabled = enabled;
            PlaySelectedButton.IsEnabled = enabled && RecordingsListBox.SelectedItem != null;
            OpenFolderButton.IsEnabled = enabled;
            if (MicVolumeSlider != null) MicVolumeSlider.IsEnabled = enabled;
            if (SysVolumeSlider != null) SysVolumeSlider.IsEnabled = enabled;
            if (LabelTextBox != null) LabelTextBox.IsEnabled = enabled;
            if (WindowCaptureComboBox != null) WindowCaptureComboBox.IsEnabled = enabled;
        }

        private void SourceCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (MicComboBox != null && MicCheckBox != null)
            {
                MicComboBox.IsEnabled = MicCheckBox.IsChecked == true;
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioEngine == null) return;

            if (sender == MicVolumeSlider && MicVolumeSliderText != null)
            {
                _audioEngine.MicVolume = (float)MicVolumeSlider.Value;
                MicVolumeSliderText.Text = $"{(int)(MicVolumeSlider.Value * 100)}%";
            }
            else if (sender == SysVolumeSlider && SysVolumeSliderText != null)
            {
                _audioEngine.SysVolume = (float)SysVolumeSlider.Value;
                SysVolumeSliderText.Text = $"{(int)(SysVolumeSlider.Value * 100)}%";
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Seleccionar carpeta para guardar grabaciones",
                    InitialDirectory = _activeFolder
                };

                if (dialog.ShowDialog() == true)
                {
                    _activeFolder = dialog.FolderName;
                    OutputFolderTextBox.Text = _activeFolder;
                    
                    _appSettings.DefaultOutputFolder = _activeFolder;
                    SettingsManager.Save(_appSettings);
                    
                    LoadRecordingsHistory();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al seleccionar carpeta: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // REPRODUCCIÓN DEL HISTORIAL
        // ==========================================

        private void RecordingsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_audioEngine.IsRecording)
            {
                PlaySelectedButton.IsEnabled = RecordingsListBox.SelectedItem != null;
            }
        }

        private void PlaySelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (RecordingsListBox.SelectedItem is not RecordingItem selectedItem) return;

            if (PlaySelectedButton.Content.ToString() == "Reproducir Selección")
            {
                try
                {
                    StatusText.Text = $"Reproduciendo: {selectedItem.Name}";
                    PlaySelectedButton.Content = "Detener Reproducción";
                    StopPlayButton.IsEnabled = true;

                    _audioEngine.Play(selectedItem.Path, () =>
                    {
                        // Callback cuando termina la reproducción en hilo secundario
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            PlaySelectedButton.Content = "Reproducir Selección";
                            StopPlayButton.IsEnabled = false;
                            StatusText.Text = "Reproducción finalizada.";
                        }));
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error de reproducción", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = "Error al intentar reproducir.";
                    PlaySelectedButton.Content = "Reproducir Selección";
                    StopPlayButton.IsEnabled = false;
                }
            }
            else
            {
                _audioEngine.StopPlayback();
                PlaySelectedButton.Content = "Reproducir Selección";
                StopPlayButton.IsEnabled = false;
                StatusText.Text = "Reproducción detenida.";
            }
        }

        private void StopPlayButton_Click(object sender, RoutedEventArgs e)
        {
            _audioEngine.StopPlayback();
            PlaySelectedButton.Content = "Reproducir Selección";
            StopPlayButton.IsEnabled = false;
            StatusText.Text = "Reproducción detenida.";
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(_activeFolder))
                {
                    Directory.CreateDirectory(_activeFolder);
                }
                Process.Start("explorer.exe", _activeFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir la carpeta: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new SettingsWindow(_appSettings);
            settingsWin.Owner = this;
            settingsWin.ShowDialog();
            
            // Re-cargar configuraciones
            _appSettings = SettingsManager.Load();
            
            // Si cambió el directorio por defecto, actualizar _activeFolder y la UI
            string newFolder = string.IsNullOrEmpty(_appSettings.DefaultOutputFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GrabacionesAudio")
                : _appSettings.DefaultOutputFolder;

            if (newFolder != _activeFolder)
            {
                _activeFolder = newFolder;
                OutputFolderTextBox.Text = _activeFolder;
                LoadRecordingsHistory();
            }
            
            // Si está grabando, actualizamos el atajo en tiempo real
            if (_audioEngine.IsRecording)
            {
                GlobalHotkey.Unregister(_windowHandle);
                GlobalHotkey.Register(_windowHandle, _appSettings.ScreenshotModifier, _appSettings.ScreenshotKey);
            }
        }

        private string GetCleanLabel()
        {
            if (LabelTextBox == null) return string.Empty;
            string text = LabelTextBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return string.Empty;

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                text = text.Replace(c, '_');
            }
            char[] invalidPathChars = Path.GetInvalidPathChars();
            foreach (char c in invalidPathChars)
            {
                text = text.Replace(c, '_');
            }
            return text.Trim();
        }

        private void TakeScreenshot()
        {
            if (!_audioEngine.IsRecording) return;

            var elapsed = DateTime.Now - _recordStartTime;
            string timeString = elapsed.ToString(@"mm\_ss");

            string cleanLabel = GetCleanLabel();
            string targetFolder = _activeFolder;
            if (!string.IsNullOrEmpty(cleanLabel))
            {
                targetFolder = Path.Combine(_activeFolder, cleanLabel);
            }

            IntPtr targetHwnd = IntPtr.Zero;
            if (WindowCaptureComboBox != null && WindowCaptureComboBox.SelectedItem is WindowItem selectedWindow)
            {
                targetHwnd = selectedWindow.Handle;
            }

            string savedPath = ScreenshotHelper.CaptureScreen(targetFolder, timeString, cleanLabel, targetHwnd);

            if (!string.IsNullOrEmpty(savedPath))
            {
                // Mostrar notificación
                ScreenshotNotification.Opacity = 1;
                
                // Animación de desvanecimiento
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    BeginTime = TimeSpan.FromSeconds(2),
                    Duration = new Duration(TimeSpan.FromSeconds(1))
                };
                ScreenshotNotification.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }

        // Win32 API para enumeración y gestión de ventanas
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        private delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);

        private void WindowCaptureComboBox_DropDownOpened(object? sender, EventArgs e)
        {
            RefreshWindowList();
        }

        private void RefreshWindowList()
        {
            if (WindowCaptureComboBox == null) return;

            var selectedHandle = IntPtr.Zero;
            if (WindowCaptureComboBox.SelectedItem is WindowItem selectedItem)
            {
                selectedHandle = selectedItem.Handle;
            }

            WindowCaptureComboBox.Items.Clear();

            // Agregar la opción de pantalla completa por defecto
            var fullScreenItem = new WindowItem { Handle = IntPtr.Zero, Title = "Pantalla Completa" };
            WindowCaptureComboBox.Items.Add(fullScreenItem);
            WindowCaptureComboBox.SelectedItem = fullScreenItem;

            try
            {
                var windows = GetOpenWindows();
                foreach (var win in windows)
                {
                    // Evitar que la aplicación se grabe a sí misma
                    if (win.Handle == _windowHandle) continue;

                    WindowCaptureComboBox.Items.Add(win);
                    if (win.Handle == selectedHandle)
                    {
                        WindowCaptureComboBox.SelectedItem = win;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al listar ventanas: {ex.Message}");
            }
        }

        private static List<WindowItem> GetOpenWindows()
        {
            IntPtr shellWindow = GetShellWindow();
            List<WindowItem> windows = new List<WindowItem>();

            EnumWindows(delegate(IntPtr hWnd, int lParam)
            {
                if (hWnd == shellWindow) return true;
                if (!IsWindowVisible(hWnd)) return true;

                int length = GetWindowTextLength(hWnd);
                if (length == 0) return true;

                var builder = new System.Text.StringBuilder(length);
                GetWindowText(hWnd, builder, length + 1);

                string title = builder.ToString();

                // Filtrar ventanas especiales invisibles del sistema
                if (title == "Program Manager" || title == "Start" || string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                windows.Add(new WindowItem { Handle = hWnd, Title = title });
                return true;
            }, 0);

            return windows.OrderBy(w => w.Title).ToList();
        }
    }

    // ==========================================
    // MODELOS AUXILIARES
    // ==========================================

    public class RecordingItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public class WindowItem
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;

        public override string ToString()
        {
            return Title;
        }
    }

    public class ComboBoxDeviceItem
    {
        public MMDevice Device { get; }
        
        public ComboBoxDeviceItem(MMDevice device)
        {
            Device = device;
        }

        public override string ToString()
        {
            return Device.FriendlyName;
        }
    }
}