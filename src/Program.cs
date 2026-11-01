using System;
using System.Windows.Forms;

namespace MediaDownloader
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (UpdateApplier.TryRun(args)) return;
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
