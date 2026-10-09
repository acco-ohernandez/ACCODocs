using System.Windows;

namespace LinkLibraryEditor
{
    /// <summary>
    /// Built-in help for the editor: Apply-vs-Save, revision semantics, permanent ids,
    /// vocabulary discipline. Modeless singleton, same zoom behavior as the add-in's
    /// help windows (shared HelpZoom).
    /// </summary>
    public partial class EditorHelpWindow : Window
    {
        private static EditorHelpWindow _instance;

        public EditorHelpWindow()
        {
            InitializeComponent();
            ACCODocs.Forms.HelpZoom.Attach(this, ContentPanel, TxtZoom, BtnZoomOut, BtnZoomIn);
        }

        public static void ShowHelp()
        {
            if (_instance == null)
            {
                _instance = new EditorHelpWindow();
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
