using System.Windows;

namespace ACCODocs.Forms
{
    /// <summary>
    /// Import-mode chooser for My Links import: merge only new items, or replace
    /// everything. Replace requires a second click on the same button (inline warning,
    /// no nested popup).
    /// </summary>
    public partial class ImportLinksModeWindow : Window
    {
        public enum ImportMode { Cancel, Merge, Replace }

        public ImportMode Result { get; private set; } = ImportMode.Cancel;

        private bool _replaceArmed;

        public ImportLinksModeWindow(string fileSummary)
        {
            InitializeComponent();
            if (!string.IsNullOrEmpty(fileSummary))
                TxtSummary.Text = $"How should this file be imported?\n{fileSummary}";
        }

        private void BtnMerge_Click(object sender, RoutedEventArgs e)
        {
            Result = ImportMode.Merge;
            DialogResult = true;
        }

        private void BtnReplace_Click(object sender, RoutedEventArgs e)
        {
            if (!_replaceArmed)
            {
                _replaceArmed = true;
                TxtReplaceWarning.Visibility = System.Windows.Visibility.Visible;
                return;
            }
            Result = ImportMode.Replace;
            DialogResult = true;
        }
    }
}
