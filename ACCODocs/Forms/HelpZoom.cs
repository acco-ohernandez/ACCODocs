using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
// WinForms is enabled alongside WPF — disambiguate.
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace ACCODocs.Forms
{
    /// <summary>
    /// Zoom for the help windows: A− / A+ buttons and Ctrl+mouse-wheel scale the content
    /// via a LayoutTransform (so the scrollbar tracks the scaled size). The level is
    /// remembered for the Revit session and shared by both help windows, so a user who
    /// bumps one gets the same size in the other.
    /// </summary>
    internal static class HelpZoom
    {
        private const double MinZoom = 0.8;
        private const double MaxZoom = 2.2;
        private const double Step = 0.1;

        private static double _zoom = 1.0;   // session-wide, shared across help windows

        public static void Attach(Window window, FrameworkElement content, TextBlock zoomLabel, Button zoomOut, Button zoomIn)
        {
            void Apply()
            {
                content.LayoutTransform = new ScaleTransform(_zoom, _zoom);
                zoomLabel.Text = $"{Math.Round(_zoom * 100)}%";
            }

            zoomOut.Click += (sender, args) => { _zoom = Math.Max(MinZoom, Math.Round(_zoom - Step, 2)); Apply(); };
            zoomIn.Click += (sender, args) => { _zoom = Math.Min(MaxZoom, Math.Round(_zoom + Step, 2)); Apply(); };

            window.PreviewMouseWheel += (sender, args) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                    return;
                _zoom = args.Delta > 0
                    ? Math.Min(MaxZoom, Math.Round(_zoom + Step, 2))
                    : Math.Max(MinZoom, Math.Round(_zoom - Step, 2));
                Apply();
                args.Handled = true;
            };

            Apply();   // pick up the session level when a help window (re)opens
        }
    }
}
