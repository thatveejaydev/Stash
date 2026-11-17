using System.Drawing;
using System.Windows.Forms;

namespace Stash
{
    // A popup whose whole content is composed off-screen and shown in one frame.
    // Without this, each row of a menu paints separately and the menu visibly builds up.
    internal sealed class BufferedDropDown : ToolStripDropDown
    {
        private const int WsExComposited = 0x02000000;

        private BufferedDropDown(Control content, bool shadow)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            AutoSize = false;
            Size = content.Size;
            BackColor = Theme.BgSurface;
            DropShadowEnabled = shadow;
            Items.Add(new ToolStripControlHost(content)
            {
                Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = content.Size
            });
        }

        internal static BufferedDropDown Create(Control content, bool shadow)
        {
            return new BufferedDropDown(content, shadow);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExComposited;
                return parameters;
            }
        }

        // Resizes the popup and its hosted content together (for content that grows while open).
        internal void FitContent(Size size)
        {
            Items[0].Size = size;
            Size = size;
        }
    }

    // A panel that paints itself and all children in one composited pass.
    internal sealed class BufferedPanel : Panel
    {
        private const int WsExComposited = 0x02000000;

        public BufferedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExComposited;
                return parameters;
            }
        }
    }
}
