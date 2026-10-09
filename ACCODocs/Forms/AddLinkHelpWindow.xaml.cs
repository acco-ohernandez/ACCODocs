using System.Windows;

namespace ACCODocs.Forms
{
    /// <summary>
    /// Dedicated help for the Add Link dialog: a field-by-field reference (location forms,
    /// type behavior, categories, tags, command ids). Modeless singleton and deliberately
    /// NOT owned by the modal Add Link dialog, so it stays readable and interactive beside
    /// the form while the user fills it in.
    /// </summary>
    public partial class AddLinkHelpWindow : Window
    {
        private static AddLinkHelpWindow _instance;

        public AddLinkHelpWindow()
        {
            InitializeComponent();
            HelpZoom.Attach(this, ContentPanel, TxtZoom, BtnZoomOut, BtnZoomIn);
        }

        public static void ShowHelp()
        {
            if (_instance == null)
            {
                _instance = new AddLinkHelpWindow();
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
