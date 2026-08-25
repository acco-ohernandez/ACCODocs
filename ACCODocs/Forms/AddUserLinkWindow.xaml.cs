using System.Windows;
// WinForms is enabled alongside WPF — disambiguate.
using MessageBox = System.Windows.MessageBox;

namespace ACCODocs.Forms
{
    /// <summary>
    /// Manual "add a link" dialog for the My Links tab (spec section 7 — a secondary
    /// action, not the headline). Modal over the pane; touches no Revit API.
    /// </summary>
    public partial class AddUserLinkWindow : Window
    {
        public AddUserLinkWindow()
        {
            InitializeComponent();
            TxtTitle.Focus();
        }

        public string LinkTitle => TxtTitle.Text.Trim();
        public string LinkUrl => TxtUrl.Text.Trim();

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (LinkTitle.Length == 0 || LinkUrl.Length == 0 || LinkUrl == "https://")
            {
                MessageBox.Show(this, "Both a title and a URL are required.", "Add Link",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        }
    }
}
