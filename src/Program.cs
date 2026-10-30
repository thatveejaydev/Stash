using System;
using System.Windows.Forms;

namespace MediaDownloader
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                AppPaths.Current.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Stash could not prepare its settings and engines in Local AppData.\n\n" + ex.Message,
                    "Storage Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.Run(new MainForm());
        }
    }
}
