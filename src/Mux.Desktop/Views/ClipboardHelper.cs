namespace Mux.Desktop.Views
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Input.Platform;

    /// <summary>
    /// Reliable clipboard text copy for the desktop app. Avalonia's <see cref="IClipboard"/> has proven
    /// unreliable in some Windows setups (the call completes but nothing lands on the clipboard), so on
    /// Windows this writes the text through the Win32 clipboard API directly, falling back to Avalonia's
    /// clipboard on other platforms (or if the Win32 path fails).
    /// </summary>
    public static class ClipboardHelper
    {
        private const uint CfUnicodeText = 13;
        private const uint GmemMoveable = 0x0002;

        /// <summary>
        /// Copies <paramref name="text"/> to the clipboard. A no-op for null/empty text (so the clipboard is
        /// never cleared by an empty copy).
        /// </summary>
        /// <param name="owner">The window whose Avalonia clipboard is used on non-Windows platforms.</param>
        /// <param name="text">The text to copy.</param>
        /// <returns>A task that completes when the copy has been attempted.</returns>
        public static async Task CopyAsync(TopLevel? owner, string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && TrySetWindowsClipboard(text!))
            {
                return;
            }

            try
            {
                IClipboard? clipboard = owner?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text!);
                }
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        private static bool TrySetWindowsClipboard(string text)
        {
            try
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    return false;
                }

                try
                {
                    EmptyClipboard();

                    byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                    IntPtr hGlobal = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
                    if (hGlobal == IntPtr.Zero)
                    {
                        return false;
                    }

                    IntPtr target = GlobalLock(hGlobal);
                    if (target == IntPtr.Zero)
                    {
                        GlobalFree(hGlobal);
                        return false;
                    }

                    try
                    {
                        Marshal.Copy(bytes, 0, target, bytes.Length);
                    }
                    finally
                    {
                        GlobalUnlock(hGlobal);
                    }

                    if (SetClipboardData(CfUnicodeText, hGlobal) == IntPtr.Zero)
                    {
                        // Ownership was not transferred to the clipboard; free the buffer.
                        GlobalFree(hGlobal);
                        return false;
                    }

                    // On success the system owns hGlobal; do not free it.
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);
    }
}
