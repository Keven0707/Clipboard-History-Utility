using System;
using System.Threading;
using System.Windows.Forms;

namespace QuietClip
{
    internal static class Program
    {
        private const string MutexName = "Local\\QuietClip_4C4B55C3_7F4C_4FC3_8A86_AE2A6A9903E1";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("拾贴已经在运行。\n\n请使用快捷键或点击系统托盘图标唤醒它。", "拾贴",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                NativeMethods.TryEnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                GC.KeepAlive(mutex);
            }
        }
    }
}

