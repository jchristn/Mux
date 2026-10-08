namespace Mux.Agent
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Sets the macOS Dock icon at runtime from a PNG. Inside an .app bundle macOS uses the bundle's .icns; this
    /// covers `dotnet run` and the bare executable, which macOS otherwise shows with a generic icon. No-op on other
    /// platforms. Must be called on the UI thread after the application has started.
    /// </summary>
    public static class MacDockIcon
    {
        #region Private-Members

        private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Applies a PNG as the application's Dock icon.
        /// </summary>
        /// <param name="png">The PNG image stream. Null returns false.</param>
        /// <returns>True when the icon was applied; false on other platforms or on any failure.</returns>
        public static bool TryApply(Stream? png)
        {
            if (!OperatingSystem.IsMacOS() || png == null)
            {
                return false;
            }

            try
            {
                byte[] bytes;
                using (MemoryStream buffer = new MemoryStream())
                {
                    png.CopyTo(buffer);
                    bytes = buffer.ToArray();
                }

                if (bytes.Length < 1)
                {
                    return false;
                }

                IntPtr nsData;
                GCHandle pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    nsData = SendPointerLength(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), pinned.AddrOfPinnedObject(), (nuint)bytes.Length);
                }
                finally
                {
                    pinned.Free();
                }

                if (nsData == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr image = SendPointer(Send(objc_getClass("NSImage"), sel_registerName("alloc")), sel_registerName("initWithData:"), nsData);
                IntPtr application = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                if (image == IntPtr.Zero || application == IntPtr.Zero)
                {
                    return false;
                }

                SendPointer(application, sel_registerName("setApplicationIconImage:"), image);
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is IOException || ex is MarshalDirectiveException)
            {
                return false;
            }
        }

        /// <summary>
        /// Whether the process is running from inside a macOS .app bundle, where the bundle icon applies.
        /// </summary>
        /// <returns>True inside a bundle.</returns>
        public static bool IsRunningFromBundle()
        {
            string? path = Environment.ProcessPath;
            return OperatingSystem.IsMacOS() && !string.IsNullOrEmpty(path) && path.Contains(".app/Contents/MacOS/", StringComparison.Ordinal);
        }

        #endregion

        #region Private-Methods

        [DllImport(ObjCLibrary)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjCLibrary)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendPointerLength(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

        #endregion
    }
}
