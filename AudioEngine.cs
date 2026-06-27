using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GrabadorAudio
{
    public class AudioEngine
    {
        private WasapiCapture? _micCapture;
        private WasapiLoopbackCapture? _sysCapture;
        private WaveFileWriter? _micWriter;
        private WaveFileWriter? _sysWriter;
        
        private string? _tempMicFile;
        private string? _tempSysFile;
        private bool _isRecording;

        private TaskCompletionSource<bool>? _micStoppedTcs;
        private TaskCompletionSource<bool>? _sysStoppedTcs;

        // Eventos para medidores de volumen en tiempo real
        public event Action<float>? MicrophoneVolumeCalculated;
        public event Action<float>? SystemVolumeCalculated;

        // Variables para reproducción
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioReader;

        static AudioEngine()
        {
            try
            {
                MediaFoundationApi.Startup();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"No se pudo iniciar MediaFoundationApi: {ex.Message}");
            }
        }

        public bool IsRecording => _isRecording;

        public float MicVolume { get; set; } = 1.0f;
        public float SysVolume { get; set; } = 0.7f;

        /// <summary>
        /// Obtiene los dispositivos de entrada de audio (micrófonos) disponibles.
        /// </summary>
        public static List<MMDevice> GetInputDevices()
        {
            var devices = new List<MMDevice>();
            try
            {
                var enumerator = new MMDeviceEnumerator();
                devices.AddRange(enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al listar dispositivos de entrada: {ex.Message}");
            }
            return devices;
        }

        /// <summary>
        /// Inicia la grabación del micrófono y/o del audio del sistema.
        /// </summary>
        public void StartRecording(MMDevice? selectedMic, bool recordMic, bool recordSys)
        {
            if (_isRecording) return;
            if (!recordMic && !recordSys)
            {
                throw new ArgumentException("Debe seleccionar al menos una fuente de audio (Micrófono o Sistema).");
            }

            _isRecording = true;
            CleanUpTempFiles();

            if (recordMic)
            {
                try
                {
                    var enumerator = new MMDeviceEnumerator();
                    var device = selectedMic ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                    
                    _micCapture = new WasapiCapture(device);
                    _tempMicFile = Path.Combine(Path.GetTempPath(), $"temp_mic_{Guid.NewGuid()}.wav");
                    _micWriter = new WaveFileWriter(_tempMicFile, _micCapture.WaveFormat);

                    _micCapture.DataAvailable += (s, a) =>
                    {
                        _micWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                        float maxVolume = CalculatePeakVolume(a.Buffer, a.BytesRecorded, _micCapture.WaveFormat.BitsPerSample);
                        MicrophoneVolumeCalculated?.Invoke(maxVolume);
                    };

                    _micCapture.RecordingStopped += (s, a) =>
                    {
                        _micWriter?.Dispose();
                        _micWriter = null;
                        _micStoppedTcs?.TrySetResult(true);
                    };

                    _micCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    StopRecordingAndCleanUp();
                    throw new InvalidOperationException($"Error al iniciar la grabación del micrófono: {ex.Message}", ex);
                }
            }

            if (recordSys)
            {
                try
                {
                    _sysCapture = new WasapiLoopbackCapture();
                    _tempSysFile = Path.Combine(Path.GetTempPath(), $"temp_sys_{Guid.NewGuid()}.wav");
                    _sysWriter = new WaveFileWriter(_tempSysFile, _sysCapture.WaveFormat);

                    _sysCapture.DataAvailable += (s, a) =>
                    {
                        _sysWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                        float maxVolume = CalculatePeakVolume(a.Buffer, a.BytesRecorded, _sysCapture.WaveFormat.BitsPerSample);
                        SystemVolumeCalculated?.Invoke(maxVolume);
                    };

                    _sysCapture.RecordingStopped += (s, a) =>
                    {
                        _sysWriter?.Dispose();
                        _sysWriter = null;
                        _sysStoppedTcs?.TrySetResult(true);
                    };

                    _sysCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    StopRecordingAndCleanUp();
                    throw new InvalidOperationException($"Error al iniciar la grabación del audio del sistema: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Detiene la grabación y procesa los archivos para generar el archivo final (mezclado y/o codificado).
        /// </summary>
        public async Task<string> StopRecordingAsync(string outputDirectory, string format, string label = "")
        {
            if (!_isRecording) return string.Empty;
            _isRecording = false;

            var stopTasks = new List<Task>();

            if (_micCapture != null)
            {
                _micStoppedTcs = new TaskCompletionSource<bool>();
                stopTasks.Add(_micStoppedTcs.Task);
                _micCapture.StopRecording();
            }

            if (_sysCapture != null)
            {
                _sysStoppedTcs = new TaskCompletionSource<bool>();
                stopTasks.Add(_sysStoppedTcs.Task);
                _sysCapture.StopRecording();
            }

            // Esperar a que se detengan las grabaciones y se cierren los escritores
            await Task.WhenAll(stopTasks);

            // Liberar dispositivos de captura
            _micCapture?.Dispose();
            _micCapture = null;
            _sysCapture?.Dispose();
            _sysCapture = null;

            // Procesar y mezclar
            try
            {
                string finalPath = CombineAndFormatFiles(outputDirectory, format, label);
                return finalPath;
            }
            catch (Exception ex)
            {
                CleanUpTempFiles();
                throw new InvalidOperationException($"Error al procesar el archivo final: {ex.Message}", ex);
            }
        }

        private void StopRecordingAndCleanUp()
        {
            _isRecording = false;
            try { _micCapture?.StopRecording(); } catch { }
            try { _sysCapture?.StopRecording(); } catch { }
            try { _micWriter?.Dispose(); } catch { }
            try { _sysWriter?.Dispose(); } catch { }
            _micWriter = null;
            _sysWriter = null;
            _micCapture?.Dispose();
            _micCapture = null;
            _sysCapture?.Dispose();
            _sysCapture = null;
            CleanUpTempFiles();
        }

        private float CalculatePeakVolume(byte[] buffer, int bytesRecorded, int bitsPerSample)
        {
            if (bytesRecorded == 0) return 0;
            float max = 0;

            if (bitsPerSample == 32)
            {
                // IEEE Float (estándar en WASAPI)
                int samples = bytesRecorded / 4;
                var waveBuffer = new WaveBuffer(buffer);
                for (int i = 0; i < samples; i++)
                {
                    float val = Math.Abs(waveBuffer.FloatBuffer[i]);
                    if (val > max) max = val;
                }
            }
            else if (bitsPerSample == 16)
            {
                // 16-bit PCM
                int samples = bytesRecorded / 2;
                var waveBuffer = new WaveBuffer(buffer);
                for (int i = 0; i < samples; i++)
                {
                    float val = Math.Abs(waveBuffer.ShortBuffer[i]) / 32768f;
                    if (val > max) max = val;
                }
            }

            return Math.Min(max, 1.0f);
        }

        private string CombineAndFormatFiles(string outputDirectory, string format, string label = "")
        {
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string extension = format.ToLower() == "mp3" ? "mp3" : "wav";
            string fileName = string.IsNullOrEmpty(label)
                ? $"Grabacion_{timestamp}.{extension}"
                : $"{label}_Grabacion_{timestamp}.{extension}";
            string finalPath = Path.Combine(outputDirectory, fileName);

            bool hasMic = _tempMicFile != null && File.Exists(_tempMicFile) && new FileInfo(_tempMicFile).Length > 1000;
            bool hasSys = _tempSysFile != null && File.Exists(_tempSysFile) && new FileInfo(_tempSysFile).Length > 1000;

            if (hasMic && hasSys)
            {
                // Mezclar ambas fuentes
                string tempMixedWav = Path.Combine(Path.GetTempPath(), $"mixed_{Guid.NewGuid()}.wav");

                var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

                using (var readerMic = new WaveFileReader(_tempMicFile))
                using (var readerSys = new WaveFileReader(_tempSysFile))
                using (var resamplerMic = new MediaFoundationResampler(readerMic, targetFormat) { ResamplerQuality = 60 })
                using (var resamplerSys = new MediaFoundationResampler(readerSys, targetFormat) { ResamplerQuality = 60 })
                {
                    var micSampleProvider = new VolumeSampleProvider(resamplerMic.ToSampleProvider()) { Volume = MicVolume };
                    var sysSampleProvider = new VolumeSampleProvider(resamplerSys.ToSampleProvider()) { Volume = SysVolume };

                    var mixer = new MixingSampleProvider(targetFormat);
                    mixer.AddMixerInput(micSampleProvider);
                    mixer.AddMixerInput(sysSampleProvider);

                    // Escribir mezcla temporal en WAV de 16 bits
                    WaveFileWriter.CreateWaveFile16(tempMixedWav, mixer);
                }

                if (format.ToLower() == "mp3")
                {
                    ConvertToMp3(tempMixedWav, finalPath);
                    try { File.Delete(tempMixedWav); } catch { }
                }
                else
                {
                    File.Move(tempMixedWav, finalPath, true);
                }
            }
            else if (hasMic)
            {
                if (format.ToLower() == "mp3")
                {
                    ConvertToMp3(_tempMicFile!, finalPath);
                }
                else
                {
                    File.Move(_tempMicFile!, finalPath, true);
                }
            }
            else if (hasSys)
            {
                if (format.ToLower() == "mp3")
                {
                    ConvertToMp3(_tempSysFile!, finalPath);
                }
                else
                {
                    File.Move(_tempSysFile!, finalPath, true);
                }
            }
            else
            {
                throw new InvalidOperationException("No se detectó audio en ninguna de las fuentes seleccionadas.");
            }

            CleanUpTempFiles();
            return finalPath;
        }

        private void ConvertToMp3(string wavPath, string mp3Path)
        {
            using (var reader = new WaveFileReader(wavPath))
            {
                var outFormat = new WaveFormat(44100, reader.WaveFormat.Channels);
                using (var resampler = new MediaFoundationResampler(reader, outFormat))
                {
                    // Codificar a MP3 (192 kbps = 192000 bps)
                    MediaFoundationEncoder.EncodeToMp3(resampler, mp3Path, 192000);
                }
            }
        }

        private void CleanUpTempFiles()
        {
            try
            {
                if (_tempMicFile != null && File.Exists(_tempMicFile))
                {
                    File.Delete(_tempMicFile);
                }
            }
            catch { }
            _tempMicFile = null;

            try
            {
                if (_tempSysFile != null && File.Exists(_tempSysFile))
                {
                    File.Delete(_tempSysFile);
                }
            }
            catch { }
            _tempSysFile = null;
        }

        // ==========================================
        // FUNCIONALIDAD DEL REPRODUCTOR INTEGRADO
        // ==========================================

        public void Play(string filePath, Action onPlaybackStopped)
        {
            StopPlayback();
            try
            {
                _audioReader = new AudioFileReader(filePath);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioReader);
                _waveOut.PlaybackStopped += (s, a) =>
                {
                    StopPlayback();
                    onPlaybackStopped();
                };
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                StopPlayback();
                throw new InvalidOperationException($"Error al reproducir el audio: {ex.Message}", ex);
            }
        }

        public void StopPlayback()
        {
            try
            {
                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }
            }
            catch { }

            try
            {
                if (_audioReader != null)
                {
                    _audioReader.Dispose();
                    _audioReader = null;
                }
            }
            catch { }
        }
    }
}
