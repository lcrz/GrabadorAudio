using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;

namespace GrabadorAudio
{
    public static class TranscriptionService
    {
        private static readonly string ModelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");

        public static async Task TranscribeAndDiarizeAsync(string audioFilePath, IProgress<double> progress)
        {
            string srtPath = Path.ChangeExtension(audioFilePath, ".srt");

            // Ejecutamos en segundo plano para no congelar la UI
            await Task.Run(() =>
            {
                RunInference(audioFilePath, srtPath, progress);
            });
        }

        private static void RunInference(string audioPath, string srtPath, IProgress<double> progress)
        {
            // 1. Validar archivos esenciales de ASR para evitar fallos nativos fatales
            string encoderPath = Path.Combine(ModelDir, "asr_encoder.onnx");
            string decoderPath = Path.Combine(ModelDir, "asr_decoder.onnx");
            string tokensPath = Path.Combine(ModelDir, "tokens.txt");

            if (!File.Exists(encoderPath) || !File.Exists(decoderPath) || !File.Exists(tokensPath))
            {
                throw new FileNotFoundException(
                    "Faltan los archivos de Whisper en la carpeta Models.\n" +
                    "Asegúrate de tener: asr_encoder.onnx, asr_decoder.onnx y tokens.txt."
                );
            }

            // 2. Leer y remuestrear audio a 16000Hz mono float[]
            float[] samples;
            try
            {
                samples = ReadAudioTo16khzMono(audioPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error al procesar el archivo de audio: {ex.Message}", ex);
            }

            // 3. Configurar ASR (Whisper Base)
            var asrConfig = new OfflineRecognizerConfig();
            asrConfig.FeatConfig.SampleRate = 16000;
            asrConfig.FeatConfig.FeatureDim = 80;
            asrConfig.ModelConfig.ModelType = "whisper"; // Crítico para que cargue correctamente
            asrConfig.ModelConfig.Whisper.Encoder = encoderPath;
            asrConfig.ModelConfig.Whisper.Decoder = decoderPath;
            asrConfig.ModelConfig.Whisper.Language = "es";
            asrConfig.ModelConfig.Whisper.Task = "transcribe";
            asrConfig.ModelConfig.Tokens = tokensPath;
            asrConfig.ModelConfig.NumThreads = 8; // Optimizado para los 8 núcleos físicos del Ryzen 7 8700G

            // Intentar usar DirectML (para iGPU AMD), si falla retroceder a CPU
            OfflineRecognizer recognizer;
            try
            {
                asrConfig.ModelConfig.Provider = "directml";
                recognizer = new OfflineRecognizer(asrConfig);
            }
            catch
            {
                asrConfig.ModelConfig.Provider = "cpu";
                recognizer = new OfflineRecognizer(asrConfig);
            }

            // 4. Configurar Diarización (PyAnnote + WeSpeaker)
            // Solo se activa si el usuario colocó tanto el modelo de segmentación como el de embedding de voz.
            string segPath = Path.Combine(ModelDir, "segmentation.onnx");
            string embedPath = Path.Combine(ModelDir, "embedding.onnx"); // Se requiere para extraer las huellas de voz

            OfflineSpeakerDiarization? diarization = null;

            if (File.Exists(segPath) && File.Exists(embedPath))
            {
                var diaConfig = new OfflineSpeakerDiarizationConfig();
                diaConfig.Segmentation.Pyannote.Model = segPath;
                diaConfig.Embedding.Model = embedPath;
                // Usamos -1 para que autodetecte el número de locutores según la voz
                diaConfig.Clustering.NumClusters = -1;
                diaConfig.Clustering.Threshold = 0.5f;
                
                try
                {
                    // Forzamos CPU para diarización. PyAnnote suele fallar en DirectML
                    // por operadores no soportados. Al ser modelos pequeños, en CPU corren en 1 segundo.
                    diaConfig.Segmentation.Provider = "cpu";
                    diaConfig.Embedding.Provider = "cpu";
                    diarization = new OfflineSpeakerDiarization(diaConfig);
                }
                catch (Exception ex)
                {
                    // Si falla, guardamos registro del error e ignoramos diarización
                    try
                    {
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diarization_error.txt"), ex.ToString());
                    }
                    catch { }
                    diarization = null;
                }
            }

            // 5. Procesar Diarización y ASR
            var srtBuilder = new StringBuilder();
            int blockIndex = 1;

            using (recognizer)
            {
                if (diarization != null)
                {
                    progress.Report(5); // Indica inicio de diarización
                    using (diarization)
                    {
                        OfflineSpeakerDiarizationSegment[] segments = null;
                        try
                        {
                            segments = diarization.Process(samples);
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diarization_error.txt"), "Processing error:\n" + ex.ToString());
                            }
                            catch { }
                            // Ignorar error de procesamiento de diarización y forzar fallback
                            segments = null;
                        }

                        progress.Report(15); // Diarización completa

                        if (segments != null && segments.Length > 0)
                        {
                            int segIndex = 0;
                            foreach (var seg in segments)
                            {
                                int startSample = (int)(seg.Start * 16000);
                                int endSample = (int)(seg.End * 16000);

                                if (startSample >= samples.Length) continue;
                                if (endSample > samples.Length) endSample = samples.Length;
                                int length = endSample - startSample;
                                if (length <= 0) continue;

                                float[] segmentSamples = new float[length];
                                Array.Copy(samples, startSample, segmentSamples, 0, length);

                                using var stream = recognizer.CreateStream();
                                stream.AcceptWaveform(16000, segmentSamples);
                                recognizer.Decode(stream);

                                string text = stream.Result.Text.Trim();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    string startTimeStr = TimeSpan.FromSeconds(seg.Start).ToString(@"hh\:mm\:ss");
                                    string endTimeStr = TimeSpan.FromSeconds(seg.End).ToString(@"hh\:mm\:ss");
                                    
                                    srtBuilder.AppendLine(blockIndex.ToString());
                                    srtBuilder.AppendLine($"[{startTimeStr} --> {endTimeStr}] Locutor {seg.Speaker + 1}: {text}");
                                    srtBuilder.AppendLine();
                                    blockIndex++;
                                }

                                segIndex++;
                                double pct = 15.0 + ((double)segIndex / segments.Length) * 85.0;
                                progress.Report(pct);
                            }
                        }
                        else
                        {
                            TranscribeEntireAudio(recognizer, samples, srtBuilder, progress);
                        }
                    }
                }
                else
                {
                    TranscribeEntireAudio(recognizer, samples, srtBuilder, progress);
                }
            }

            // 6. Guardar archivo SRT
            File.WriteAllText(srtPath, srtBuilder.ToString(), Encoding.UTF8);
        }

        private static void TranscribeEntireAudio(OfflineRecognizer recognizer, float[] samples, StringBuilder srtBuilder, IProgress<double> progress)
        {
            int sampleRate = 16000;
            int chunkLengthSeconds = 20; // 20 segundos por fragmento
            int chunkSize = chunkLengthSeconds * sampleRate;
            int totalSamples = samples.Length;
            int blockIndex = 1;

            int totalChunks = (int)Math.Ceiling((double)totalSamples / chunkSize);
            int currentChunk = 0;

            for (int i = 0; i < totalSamples; i += chunkSize)
            {
                int currentChunkSize = Math.Min(chunkSize, totalSamples - i);
                float[] chunkSamples = new float[currentChunkSize];
                Array.Copy(samples, i, chunkSamples, 0, currentChunkSize);

                using var stream = recognizer.CreateStream();
                stream.AcceptWaveform(sampleRate, chunkSamples);
                recognizer.Decode(stream);

                string text = stream.Result.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    double startSeconds = (double)i / sampleRate;
                    double endSeconds = (double)(i + currentChunkSize) / sampleRate;

                    string startTimeStr = TimeSpan.FromSeconds(startSeconds).ToString(@"hh\:mm\:ss");
                    string endTimeStr = TimeSpan.FromSeconds(endSeconds).ToString(@"hh\:mm\:ss");

                    srtBuilder.AppendLine(blockIndex.ToString());
                    srtBuilder.AppendLine($"[{startTimeStr} --> {endTimeStr}] Locutor 1: {text}");
                    srtBuilder.AppendLine();
                    blockIndex++;
                }

                currentChunk++;
                double pct = ((double)currentChunk / totalChunks) * 100.0;
                progress.Report(pct);
            }
        }

        private static float[] ReadAudioTo16khzMono(string path)
        {
            using (var reader = new AudioFileReader(path))
            {
                var mono = reader.ToMono();
                var resampler = new WdlResamplingSampleProvider(mono, 16000);
                
                var floatBuffer = new List<float>();
                float[] readBuffer = new float[16000];
                int samplesRead;
                
                while ((samplesRead = resampler.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    floatBuffer.AddRange(readBuffer.Take(samplesRead));
                }
                
                return floatBuffer.ToArray();
            }
        }
    }
}
