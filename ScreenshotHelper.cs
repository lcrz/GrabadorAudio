using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace GrabadorAudio
{
    public static class ScreenshotHelper
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static string CaptureScreen(string folderPath, string timeString, string label = "", IntPtr hWnd = default)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                int screenLeft, screenTop, screenWidth, screenHeight;

                if (hWnd != IntPtr.Zero)
                {
                    bool isMinimized = IsIconic(hWnd);
                    bool isForeground = GetForegroundWindow() == hWnd;

                    if (isMinimized)
                    {
                        // Aseguramos que la ventana no esté minimizada
                        ShowWindow(hWnd, SW_RESTORE);
                        System.Threading.Thread.Sleep(200); // Esperamos a que termine de restaurarse la ventana
                    }

                    if (!isForeground)
                    {
                        // La traemos al frente si no está activa
                        SetForegroundWindow(hWnd);
                        System.Threading.Thread.Sleep(100); // Esperamos a que se traiga al frente y se renderice
                    }

                    RECT rect;
                    // Obtenemos los límites visibles reales en Win 10/11 sin sombras invisibles
                    int result = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(RECT)));
                    if (result != 0)
                    {
                        GetWindowRect(hWnd, out rect);
                    }

                    screenLeft = rect.Left;
                    screenTop = rect.Top;
                    screenWidth = rect.Right - rect.Left;
                    screenHeight = rect.Bottom - rect.Top;

                    // Fallback a pantalla completa si el tamaño es inválido
                    if (screenWidth <= 0 || screenHeight <= 0)
                    {
                        screenLeft = SystemInformation.VirtualScreen.Left;
                        screenTop = SystemInformation.VirtualScreen.Top;
                        screenWidth = SystemInformation.VirtualScreen.Width;
                        screenHeight = SystemInformation.VirtualScreen.Height;
                    }
                }
                else
                {
                    // Determinamos el área de todas las pantallas
                    screenLeft = SystemInformation.VirtualScreen.Left;
                    screenTop = SystemInformation.VirtualScreen.Top;
                    screenWidth = SystemInformation.VirtualScreen.Width;
                    screenHeight = SystemInformation.VirtualScreen.Height;
                }

                using (Bitmap bitmap = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(screenLeft, screenTop, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                    }

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string fileName = string.IsNullOrEmpty(label)
                        ? $"Captura_{timestamp}_{timeString}.png"
                        : $"{label}_Captura_{timestamp}_{timeString}.png";
                    string filePath = Path.Combine(folderPath, fileName);

                    bitmap.Save(filePath, ImageFormat.Png);
                    return filePath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturando pantalla: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
