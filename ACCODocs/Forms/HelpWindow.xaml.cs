using System.Windows;

namespace ACCODocs.Forms
{
    /// <summary>
    /// Built-in help for the Link Library pane (option A: ships with the add-in, works
    /// offline). Modeless singleton so Help stays open while the user works the pane,
    /// and repeated clicks refocus instead of stacking. The Add Link dialog has its own
    /// dedicated help (AddLinkHelpWindow).
    /// </summary>
    public partial class HelpWindow : Window
    {
        private static HelpWindow _instance;

        public HelpWindow()
        {
            InitializeComponent();
            HelpZoom.Attach(this, ContentPanel, TxtZoom, BtnZoomOut, BtnZoomIn);
        }

        /// <summary>Opens (or refocuses) the pane help window.</summary>
        public static void ShowHelp()
        {
            if (_instance == null)
            {
                _instance = new HelpWindow();
                _instance.Closed += (sender, args) => _instance = null;
                _instance.Show();
            }
            else
            {
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;
                _instance.Activate();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
