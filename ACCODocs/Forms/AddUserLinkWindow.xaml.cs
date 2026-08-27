using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
// WinForms is enabled alongside WPF — disambiguate (and FolderBrowserDialog is WinForms:
// it works on BOTH net48 and net8, unlike Microsoft.Win32.OpenFolderDialog which is net8-only).
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace ACCODocs.Forms
{
    /// <summary>
    /// "Add a link" dialog for the My Links tab (spec section 7 — a secondary action).
    /// Accepts URLs, files, folders, network shares, and Box/OneDrive paths; the kind is
    /// auto-detected from the location (overridable). Tags come from the master
    /// tagVocabulary as checkboxes. Touches no Revit API.
    /// </summary>
    public partial class AddUserLinkWindow : Window
    {
        public class TagOption
        {
            public string Name { get; set; }
            public bool IsChecked { get; set; }
        }

        private readonly ObservableCollection<TagOption> _tagOptions;
        private bool _kindManuallySet;
        private bool _addAnywayArmed;   // second Add click confirms a not-found path

        public AddUserLinkWindow(IEnumerable<string> vocabularyTags)
        {
            InitializeComponent();

            _tagOptions = new ObservableCollection<TagOption>(
                (vocabularyTags ?? Enumerable.Empty<string>()).Select(tag => new TagOption { Name = tag }));
            if (_tagOptions.Count > 0)
            {
                ListTags.ItemsSource = _tagOptions;
            }
            else
            {
                // No vocabulary loaded (offline, empty master) — hide the section entirely.
                LblTags.Visibility = System.Windows.Visibility.Collapsed;
                ListTags.Visibility = System.Windows.Visibility.Collapsed;
            }

            // Track manual overrides so auto-detect stops fighting the user, but a changed
            // location re-enables detection.
            CmbKind.SelectionChanged += (sender, args) =>
            {
                if (CmbKind.IsDropDownOpen)
                    _kindManuallySet = true;
            };

            SetKind("url");
            TxtTitle.Focus();
        }

        public string LinkTitle => TxtTitle.Text.Trim();
        public string LinkTarget => LinkLibrary_Pane.NormalizeTarget(TxtLocation.Text);
        public string LinkKind => (CmbKind.SelectedItem as ComboBoxItem)?.Content as string ?? "url";
        public string LinkDescription =>
            string.IsNullOrWhiteSpace(TxtDescription.Text) ? null : TxtDescription.Text.Trim();
        public List<string> SelectedTags =>
            _tagOptions.Where(option => option.IsChecked).Select(option => option.Name)
                .Concat(ParseCustomTags(TxtCustomTags.Text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Comma-separated free-form tags: trimmed, deduped, empties dropped.</summary>
        internal static IEnumerable<string> ParseCustomTags(string text)
        {
            return (text ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0);
        }

        // ------------------------------------------------------------------ kind detection

        private void TxtLocation_TextChanged(object sender, TextChangedEventArgs e)
        {
            _kindManuallySet = false;    // a new location restarts auto-detection
            _addAnywayArmed = false;
            TxtValidation.Visibility = System.Windows.Visibility.Collapsed;

            string detected = DetectKind(LinkLibrary_Pane.NormalizeTarget(TxtLocation.Text));
            SetKind(detected);
            TxtDetected.Text = $"(auto-detected: {detected})";
        }

        private static string DetectKind(string target)
        {
            if (target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return "mailto";
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "url";
            if (Directory.Exists(target))
                return "folder";
            if (File.Exists(target))
                return "file";
            if (target.StartsWith("\\\\"))
                return "folder";   // unreachable UNC right now — folder is the likeliest intent
            return "url";
        }

        private void SetKind(string kind)
        {
            if (_kindManuallySet)
                return;
            foreach (ComboBoxItem item in CmbKind.Items)
            {
                if ((string)item.Content == kind)
                {
                    CmbKind.SelectedItem = item;
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ browse

        private void BtnBrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick a file to link",
                Filter = "All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) == true)
                ApplyBrowsedPath(dialog.FileName);
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Pick a folder to link (local, network, or Box)",
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    ApplyBrowsedPath(dialog.SelectedPath);
            }
        }

        private void ApplyBrowsedPath(string path)
        {
            TxtLocation.Text = path;    // fires TextChanged → auto-detect
            if (TxtTitle.Text.Trim().Length == 0)
                TxtTitle.Text = Path.GetFileName(path.TrimEnd('\\', '/'));
        }

        // ------------------------------------------------------------------ add

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            string target = LinkTarget;

            if (LinkTitle.Length == 0 || target.Length == 0 || target == "https://")
            {
                TxtValidation.Text = "Both a title and a location are required.";
                TxtValidation.Visibility = System.Windows.Visibility.Visible;
                return;
            }

            // A rooted/UNC path that isn't reachable right now may still be legitimate
            // (unsynced Box content, disconnected VPN) — warn once, second Add confirms.
            bool looksLikePath = target.StartsWith("\\\\") ||
                (target.Length >= 3 && char.IsLetter(target[0]) && target[1] == ':');
            if (looksLikePath && !Directory.Exists(target) && !File.Exists(target) && !_addAnywayArmed)
            {
                _addAnywayArmed = true;
                TxtValidation.Text = $"\"{target}\" was not found right now (drive disconnected? Box not synced?). " +
                                     "Click Add again to save it anyway.";
                TxtValidation.Visibility = System.Windows.Visibility.Visible;
                return;
            }

            DialogResult = true;
        }
    }
}
