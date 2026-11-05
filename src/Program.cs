using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TodoLite
{
    internal static class Program
    {
        private const string MutexName = "TodoLite.SingleInstance";
        private const string WindowTitle = "TodoLite";

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        [STAThread]
        private static void Main()
        {
            bool createdNew = false;
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (Exception)
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                ActivateExistingWindow();
                if (mutex != null) mutex.Dispose();
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("\u7a0b\u5e8f\u542f\u52a8\u5931\u8d25\uff1a" + ex.Message,
                    WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); }
                    catch (Exception) { }
                    mutex.Dispose();
                }
            }
        }

        private static void ActivateExistingWindow()
        {
            for (int i = 0; i < 30; i++)
            {
                IntPtr handle = FindWindow(null, WindowTitle);
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_RESTORE);
                    SetForegroundWindow(handle);
                    return;
                }
                Thread.Sleep(100);
            }
        }
    }
}
