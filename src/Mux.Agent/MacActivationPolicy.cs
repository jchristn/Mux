namespace Mux.Agent
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Switches the macOS activation policy so the tray agent lives only in the menu bar, and appears in the Dock
    /// (with its own menu bar) only while one of its windows is open. No-op on other platforms. Call on the UI thread.
    /// </summary>
    public static class MacActivationPolicy
    {
        #region Private-Members

        private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
        private const long PolicyRegular = 0;
        private const long PolicyAccessory = 1;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Shows the app in the Dock and lets it own the menu bar while active.
        /// </summary>
        /// <returns>True when the policy changed.</returns>
        public static bool ShowInDock()
        {
            return Apply(PolicyRegular);
        }

        /// <summary>
        /// Hides the app from the Dock, leaving only its menu-bar status item.
        /// </summary>
        /// <returns>True when the policy changed.</returns>
        public static bool HideFromDock()
        {
            return Apply(PolicyAccessory);
        }

        #endregion

        #region Private-Methods

        private static bool Apply(long policy)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return false;
            }

            try
            {
                IntPtr application = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                return application != IntPtr.Zero && SendLong(application, sel_registerName("setActivationPolicy:"), policy);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is MarshalDirectiveException)
            {
                return false;
            }
        }

        [DllImport(ObjCLibrary)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjCLibrary)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SendLong(IntPtr receiver, IntPtr selector, long argument);

        #endregion
    }
}
