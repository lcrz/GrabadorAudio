using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace GrabadorAudio
{
    public static class GlobalHotkey
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        public const int HOTKEY_ID = 9000;

        public static bool Register(IntPtr hWnd, ModifierKeys modifier, Key key)
        {
            uint modifiers = 0;
            if ((modifier & ModifierKeys.Alt) == ModifierKeys.Alt) modifiers |= MOD_ALT;
            if ((modifier & ModifierKeys.Control) == ModifierKeys.Control) modifiers |= MOD_CONTROL;
            if ((modifier & ModifierKeys.Shift) == ModifierKeys.Shift) modifiers |= MOD_SHIFT;
            if ((modifier & ModifierKeys.Windows) == ModifierKeys.Windows) modifiers |= MOD_WIN;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            return RegisterHotKey(hWnd, HOTKEY_ID, modifiers, vk);
        }

        public static bool Unregister(IntPtr hWnd)
        {
            return UnregisterHotKey(hWnd, HOTKEY_ID);
        }
    }
}
