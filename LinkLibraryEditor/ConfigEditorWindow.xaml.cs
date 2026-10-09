using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ACCODocs.Logic.LinkLibrary;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace LinkLibraryEditor
{
    /// <summary>
    /// Typed form editor for LinkLibrary.config.json — admins never hand-edit the JSON.
    /// Loads any config copy (old PascalCase files included — reading is case-insensitive),
    /// validates, and writes camelCase atomically. "Check Paths" probes the configured
    /// files/folders on demand; unreachable paths are saveable by design (shares go down —
    /// that's what the fallback is for).
    /// </summary>
    public partial class ConfigEditorWindow : Window
    {
        private LinkLibraryConfig _config;
        private string _path;
        private bool _dirty;
        private bool _loading;   // suppress dirty-tracking while fields are being filled

        public ConfigEditorWindow()
        {
            InitializeComponent();
            Closing += (sender, args) =>
            {
                if (_dirty &&
                    MessageBox.Show(this, "There are unsaved config changes. Close anyway?", "Config Editor",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                {
                    args.Cancel = true;
                }
            };
        }

        // ------------------------------------------------------------------ open / new

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscard())
                return;

            var dialog = new OpenFileDialog
            {
                Title = "Open config",
                Filter = "Link Library config (*.json)|*.json",
                FileName = LinkLibraryConfig.ConfigFileName,
                InitialDirectory = Directory.Exists(LinkLibraryConfig.SharedConfigFolder)
                    ? LinkLibraryConfig.SharedConfigFolder : ""
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                var config = JsonConvert.DeserializeObject<LinkLibraryConfig>(File.ReadAllText(dialog.FileName));
                if (config == null)
                    throw new InvalidDataException("File parsed to nothing.");
                LoadIntoFields(config, dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open the config:\n{ex.Message}", "Config Editor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscard())
                return;
            LoadIntoFields(new LinkLibraryConfig(), null);
            TxtStatus.Text = "New config from compiled defaults — Save As... to write it.";
        }

        private bool ConfirmDiscard()
        {
            return !_dirty ||
                MessageBox.Show(this, "There are unsaved config changes. Discard them?", "Config Editor",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private void LoadIntoFields(LinkLibraryConfig config, string path)
        {
            _loading = true;
            _config = config;
            _path = path;

            TxtMaster.Text = config.MasterLibraryPath ?? "";
            TxtFallback.Text = config.FallbackLibraryPath ?? "";
            TxtCache.Text = config.LocalCacheFolder ?? "";
            TxtUserLib.Text = config.UserLibraryFolder ?? "";
            TxtTelemetry.Text = config.TelemetryFolder ?? "";
            TxtRecipient.Text = config.SuggestionRecipient ?? "";
            foreach (ComboBoxItem item in CmbMailMethod.Items)
                item.IsSelected = string.Equals((string)item.Content, config.SuggestionMailMethod, StringComparison.OrdinalIgnoreCase);
            if (CmbMailMethod.SelectedItem == null)
                CmbMailMethod.SelectedIndex = 1;   // mailto, the compiled default
            TxtSubjectPrefix.Text = config.SuggestionSubjectPrefix ?? "";
            TxtRefresh.Text = config.RefreshCheckMinutes.ToString();
            TxtBadgeDays.Text = config.NewBadgeDays.ToString();
            TxtRecentsStored.Text = config.MaxRecentsStored.ToString();
            TxtRecentsShown.Text = config.MaxRecentsShown.ToString();
            ChkTelemetry.IsChecked = config.EnableTelemetry;
            TxtVersionInfo.Text = $"configVersion: {config.ConfigVersion}";
            TxtCheckResults.Visibility = Visibility.Collapsed;

            PanelFields.IsEnabled = true;
            _dirty = false;
            _loading = false;
            TxtStatus.Text = path == null ? TxtStatus.Text : $"{path}";
        }

        private void AnyField_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                _dirty = true;
        }

        // ------------------------------------------------------------------ save

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_path == null)
            {
                BtnSaveAs_Click(sender, e);
                return;
            }
            SaveTo(_path);
        }

        private void BtnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (_config == null)
                return;
            var dialog = new SaveFileDialog
            {
                Title = "Save config",
                Filter = "Link Library config (*.json)|*.json",
                FileName = LinkLibraryConfig.ConfigFileName,
                InitialDirectory = Directory.Exists(LinkLibraryConfig.SharedConfigFolder)
                    ? LinkLibraryConfig.SharedConfigFolder : ""
            };
            if (dialog.ShowDialog(this) == true)
                SaveTo(dialog.FileName);
        }

        private void SaveTo(string path)
        {
            if (!CollectFields(out string error))
            {
                MessageBox.Show(this, error, "Config Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string json = JsonConvert.SerializeObject(_config, Formatting.Indented,
                    new JsonSerializerSettings
                    {
                        ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                    });

                string temp = path + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);

                _path = path;
                _dirty = false;
                TxtStatus.Text = $"Saved {path} — Revit reads the config at startup, so users need a Revit restart to pick changes up.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Save failed:\n{ex.Message}", "Config Editor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Pulls fields into _config; false + message when something doesn't validate.</summary>
        private bool CollectFields(out string error)
        {
            error = null;

            if (TxtMaster.Text.Trim().Length == 0)
            {
                error = "The master library path is required — without it the pane can never update.";
                return false;
            }
            if (!int.TryParse(TxtRefresh.Text.Trim(), out int refresh) || refresh < 1)
            {
                error = "Refresh check must be a whole number of minutes, 1 or more.";
                return false;
            }
            if (!int.TryParse(TxtBadgeDays.Text.Trim(), out int badgeDays) || badgeDays < 0)
            {
                error = "NEW badge days must be a whole number, 0 or more (0 disables the badge).";
                return false;
            }
            if (!int.TryParse(TxtRecentsStored.Text.Trim(), out int recentsStored) || recentsStored < 0 || recentsStored > 500)
            {
                error = "Recents stored must be between 0 and 500 (0 disables recents).";
                return false;
            }
            if (!int.TryParse(TxtRecentsShown.Text.Trim(), out int recentsShown) || recentsShown < 0 || recentsShown > 500)
            {
                error = "Recents shown must be between 0 and 500.";
                return false;
            }

            _config.MasterLibraryPath = TxtMaster.Text.Trim();
            _config.FallbackLibraryPath = TxtFallback.Text.Trim();
            _config.LocalCacheFolder = TxtCache.Text.Trim();
            _config.UserLibraryFolder = TxtUserLib.Text.Trim();
            _config.TelemetryFolder = TxtTelemetry.Text.Trim();
            _config.SuggestionRecipient = TxtRecipient.Text.Trim();
            _config.SuggestionMailMethod = (CmbMailMethod.SelectedItem as ComboBoxItem)?.Content as string ?? "mailto";
            _config.SuggestionSubjectPrefix = TxtSubjectPrefix.Text.Trim();
            _config.RefreshCheckMinutes = refresh;
            _config.NewBadgeDays = badgeDays;
            _config.MaxRecentsStored = recentsStored;
            _config.MaxRecentsShown = recentsShown;
            _config.EnableTelemetry = ChkTelemetry.IsChecked == true;
            return true;
        }

        // ------------------------------------------------------------------ browse / check

        private void BtnBrowseMaster_Click(object sender, RoutedEventArgs e) => BrowseJsonInto(TxtMaster);
        private void BtnBrowseFallback_Click(object sender, RoutedEventArgs e) => BrowseJsonInto(TxtFallback);

        private void BrowseJsonInto(TextBox target)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pick the master library file",
                Filter = "Master library (*.json)|*.json",
                CheckFileExists = false   // the share may be offline right now — that's fine
            };
            if (dialog.ShowDialog(this) == true)
                target.Text = dialog.FileName;
        }

        private void BtnCheckPaths_Click(object sender, RoutedEventArgs e)
        {
            if (_config == null)
                return;

            string Expand(string value) => Environment.ExpandEnvironmentVariables(value ?? "");
            var report = new StringBuilder();

            void CheckFile(string label, string rawPath)
            {
                string expanded = Expand(rawPath);
                string verdict = expanded.Length == 0 ? "—  (not set)"
                    : File.Exists(expanded) ? "OK  reachable"
                    : "X   NOT reachable right now";
                report.AppendLine($"{verdict,-26} {label}: {expanded}");
            }
            void CheckFolder(string label, string rawPath)
            {
                string expanded = Expand(rawPath);
                string verdict = expanded.Length == 0 ? "—  (not set)"
                    : Directory.Exists(expanded) ? "OK  exists"
                    : "X   missing (created on first use)";
                report.AppendLine($"{verdict,-26} {label}: {expanded}");
            }

            CheckFile("master", TxtMaster.Text);
            CheckFile("fallback", TxtFallback.Text);
            CheckFolder("cache", TxtCache.Text);
            CheckFolder("user library", TxtUserLib.Text);
            CheckFolder("telemetry", TxtTelemetry.Text);

            TxtCheckResults.Text = report.ToString().TrimEnd();
            TxtCheckResults.Visibility = Visibility.Visible;
            TxtStatus.Text = "Path check done — an unreachable master is fine to save (that's what the fallback and cache are for).";
        }

        private void Help_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            EditorHelpWindow.ShowHelp();
        }
    }
}
