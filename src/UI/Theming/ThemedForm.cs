using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stash
{
    public abstract class ThemedForm : Form
    {
        private const int WsExComposited = 0x02000000;
        private const int DwmTransitionsForceDisabled = 3;
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeLegacy = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        protected ThemedForm()
        {
            BackColor = Theme.BgWindow;
            ForeColor = Theme.TextPrimary;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                // Buffer the window and its child controls together for the first paint.
                parameters.ExStyle |= WsExComposited;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(Handle, DwmTransitionsForceDisabled, ref enabled, sizeof(int));
                if (DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            base.OnHandleCreated(e);
        }
    }
}
