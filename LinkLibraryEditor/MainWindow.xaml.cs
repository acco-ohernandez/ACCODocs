using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using ACCODocs.Logic.LinkLibrary;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace LinkLibraryEditor
{
    /// <summary>
    /// Admin GUI for the master Link Library JSON (LinkLibrary_DEV_Plan.md section 7b):
    /// nobody hand-edits JSON text. Enforces the spec rules the format can't enforce
    /// itself — ids are auto-generated and never editable, tags come from the controlled
    /// vocabulary via checkboxes, revision bumps automatically, writes are atomic.
    /// </summary>
    public partial class MainWindow : Window
    {
        public class TagOption
        {
            public string Name { get; set; }
            public bool IsChecked { get; set; }
        }

        private LinkLibraryDocument _doc;
        private string _path;
        private LibraryNode _selected;
        private ObservableCollection<TagOption> _tagOptions = new ObservableCollection<TagOption>();
        private bool _dirty;

        public MainWindow()
        {
            InitializeComponent();
            Closing += (s, e) =>
            {
                if (_dirty &&
                    MessageBox.Show(this, "There are unsaved changes. Close anyway?", "Link Library Editor",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
            };
        }

        // ------------------------------------------------------------------ file

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open master library",
                Filter = "Link Library master (*.json)|*.json",
                FileName = "LinkLibrary.master.json"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                var doc = JsonConvert.DeserializeObject<LinkLibraryDocument>(File.ReadAllText(dialog.FileName));
                if (doc == null)
                    throw new InvalidDataException("File parsed to nothing.");

                _doc = doc;
                _doc.Groups ??= new List<LibraryNode>();
                _path = dialog.FileName;
                _dirty = false;
                _selected = null;

                BuildTagOptions();
                RefreshTree();
                ShowNode(null);
                TxtStatus.Text = $"{_path} — revision {_doc.Revision} ({CountLinks(_doc.Groups)} links)";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open the file:\n{ex.Message}", "Link Library Editor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null)
                return;

            var issues = Validate();
            if (issues.Count > 0 &&
                MessageBox.Show(this,
                    "Validation found issues:\n\n - " + string.Join("\n - ", issues) + "\n\nSave anyway?",
                    "Link Library Editor", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
            {
                return;
            }

            try
            {
                // Revision is the version authority (spec section 5) — bump on every save.
                _doc.Revision++;
                _doc.RevisionDate = DateTime.Now.ToString("yyyy-MM-dd");

                // Atomic write: temp then replace — a killed process never leaves a half file.
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_doc, Formatting.Indented,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
                if (File.Exists(_path))
                    File.Replace(temp, _path, null);
                else
                    File.Move(temp, _path);

                _dirty = false;
                TxtStatus.Text = $"Saved revision {_doc.Revision} — {_path}";
            }
            catch (Exception ex)
            {
                _doc.Revision--;   // the write failed; don't burn the revision number
                MessageBox.Show(this, $"Save failed:\n{ex.Message}", "Link Library Editor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------------ tree

        private void RefreshTree()
        {
            TreeMain.ItemsSource = null;
            TreeMain.ItemsSource = _doc?.Groups;
        }

        private void TreeMain_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            ShowNode(e.NewValue as LibraryNode);
        }

        private void ShowNode(LibraryNode node)
        {
            _selected = node;
            PanelDetails.IsEnabled = node != null;

            TxtId.Text = node?.Id ?? "";
            TxtTitle.Text = node?.Title ?? "";
            TxtDescription.Text = node?.Description ?? "";

            bool isLink = node != null && !node.IsGroup;
            PanelLinkFields.Visibility = isLink ? Visibility.Visible : Visibility.Collapsed;

            if (isLink)
            {
                CmbKind.Text = node.Kind ?? "url";
                foreach (ComboBoxItem item in CmbKind.Items)
                    item.IsSelected = (string)item.Content == (node.Kind ?? "url");
                TxtTarget.Text = node.Target ?? "";
                TxtVersions.Text = node.RevitVersions == null ? "" : string.Join(", ", node.RevitVersions);
                TxtOwner.Text = node.Owner ?? "";

                foreach (TagOption option in _tagOptions)
                    option.IsChecked = node.Tags != null && node.Tags.Contains(option.Name);
                ListTags.ItemsSource = null;
                ListTags.ItemsSource = _tagOptions;

                // Tags the node carries that the vocabulary doesn't know yet show up here
                // (e.g. from a hand-edited file); applying folds them into the vocabulary.
                var vocabulary = new HashSet<string>(AllVocabularyTags());
                TxtCustomTags.Text = node.Tags == null
                    ? ""
                    : string.Join(", ", node.Tags.Where(tag => !vocabulary.Contains(tag)));
            }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            _selected.Title = TxtTitle.Text.Trim();
            _selected.Description = string.IsNullOrWhiteSpace(TxtDescription.Text) ? null : TxtDescription.Text.Trim();

            if (!_selected.IsGroup)
            {
                _selected.Kind = (CmbKind.SelectedItem as ComboBoxItem)?.Content as string ?? "url";
                _selected.Target = TxtTarget.Text.Trim();
                _selected.Owner = string.IsNullOrWhiteSpace(TxtOwner.Text) ? null : TxtOwner.Text.Trim();
                _selected.Updated = DateTime.Now.ToString("yyyy-MM-dd");

                // Checked vocabulary tags + free-form custom tags. Customs are folded into
                // the vocabulary's "custom" group so the master stays validation-clean —
                // the controlled list remains the authority, extended consciously here.
                var customTags = TxtCustomTags.Text
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(tag => tag.Trim())
                    .Where(tag => tag.Length > 0)
                    .ToList();

                var tags = _tagOptions.Where(option => option.IsChecked).Select(option => option.Name)
                    .Concat(customTags)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _selected.Tags = tags.Count > 0 ? tags : null;

                if (customTags.Count > 0)
                {
                    _doc.TagVocabulary ??= new Dictionary<string, List<string>>();
                    if (!_doc.TagVocabulary.TryGetValue("custom", out List<string> customGroup))
                    {
                        customGroup = new List<string>();
                        _doc.TagVocabulary["custom"] = customGroup;
                    }
                    var known = new HashSet<string>(AllVocabularyTags(), StringComparer.OrdinalIgnoreCase);
                    customGroup.AddRange(customTags.Where(tag => !known.Contains(tag)));
                    BuildTagOptions();   // new tags become checkboxes immediately
                }

                var versions = new List<int>();
                foreach (string part in TxtVersions.Text.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(part.Trim(), out int v))
                        versions.Add(v);
                _selected.RevitVersions = versions.Count > 0 ? versions : null;
            }

            _dirty = true;
            RefreshTree();
            ShowNode(_selected);   // new custom tags now render as checked checkboxes
            TxtStatus.Text = $"Applied changes to \"{_selected.Title}\" (unsaved).";
        }

        // ------------------------------------------------------------------ add / delete

        private void BtnAddGroup_Click(object sender, RoutedEventArgs e) => AddNode(isGroup: true);
        private void BtnAddLink_Click(object sender, RoutedEventArgs e) => AddNode(isGroup: false);

        private void AddNode(bool isGroup)
        {
            if (_doc == null)
                return;

            // New nodes land under the selected group; under the selected link's parent;
            // or at the root when nothing is selected.
            LibraryNode parent = _selected != null && _selected.IsGroup
                ? _selected
                : FindParent(_doc.Groups, _selected);

            var list = parent?.Children ?? _doc.Groups;
            string parentId = parent?.Id ?? "acco";

            string title = isGroup ? "New Group" : "New Link";
            var node = new LibraryNode
            {
                Id = MakeUniqueId(parentId, title),
                Title = title,
                Added = DateTime.Now.ToString("yyyy-MM-dd")
            };
            if (isGroup)
                node.Children = new List<LibraryNode>();
            else
            {
                node.Kind = "url";
                node.Target = "https://";
            }

            list.Add(node);
            _dirty = true;
            RefreshTree();
            TxtStatus.Text = $"Added {(isGroup ? "group" : "link")} \"{title}\" under \"{parent?.Title ?? "root"}\" — rename it, then Apply.";
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null || _selected == null)
                return;

            string warning = _selected.IsGroup
                ? $"Delete group \"{_selected.Title}\" AND everything inside it?"
                : $"Delete link \"{_selected.Title}\"?\n\nAnyone who favorited it will lose it — deleting is worse than fixing.";
            if (MessageBox.Show(this, warning, "Link Library Editor",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            RemoveNode(_doc.Groups, _selected);
            _selected = null;
            _dirty = true;
            RefreshTree();
            ShowNode(null);
            TxtStatus.Text = "Deleted (unsaved).";
        }

        // ------------------------------------------------------------------ validation

        private void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null)
                return;
            var issues = Validate();
            MessageBox.Show(this,
                issues.Count == 0 ? "No issues found." : " - " + string.Join("\n - ", issues),
                "Validation", MessageBoxButton.OK,
                issues.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private List<string> Validate()
        {
            var issues = new List<string>();
            var seenIds = new HashSet<string>();
            var vocabulary = new HashSet<string>(AllVocabularyTags());

            void Walk(IEnumerable<LibraryNode> nodes, string path)
            {
                foreach (LibraryNode node in nodes)
                {
                    string label = $"{path}/{node.Title}";
                    if (string.IsNullOrWhiteSpace(node.Id))
                        issues.Add($"Missing id: {label}");
                    else if (!seenIds.Add(node.Id))
                        issues.Add($"Duplicate id \"{node.Id}\": {label}");

                    if (node.IsGroup)
                    {
                        Walk(node.Children, label);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(node.Target))
                            issues.Add($"Empty target: {label}");
                        if (string.IsNullOrWhiteSpace(node.Kind))
                            issues.Add($"Missing kind: {label}");
                        foreach (string tag in node.Tags ?? new List<string>())
                            if (!vocabulary.Contains(tag))
                                issues.Add($"Tag \"{tag}\" is not in the vocabulary: {label}");
                    }
                }
            }
            Walk(_doc.Groups, "");
            return issues;
        }

        // ------------------------------------------------------------------ helpers

        private void BuildTagOptions()
        {
            _tagOptions = new ObservableCollection<TagOption>(
                AllVocabularyTags().Select(t => new TagOption { Name = t }));
            ListTags.ItemsSource = _tagOptions;
        }

        private IEnumerable<string> AllVocabularyTags()
        {
            if (_doc?.TagVocabulary == null)
                return Enumerable.Empty<string>();
            return _doc.TagVocabulary.Values.SelectMany(v => v).Distinct();
        }

        /// <summary>Dotted id from the parent id + slugified title, suffixed until unique (spec section 5: ids are permanent).</summary>
        private string MakeUniqueId(string parentId, string title)
        {
            string slug = Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (slug.Length == 0)
                slug = "item";

            var existing = new HashSet<string>();
            void Collect(IEnumerable<LibraryNode> nodes)
            {
                foreach (LibraryNode n in nodes)
                {
                    existing.Add(n.Id);
                    if (n.Children != null) Collect(n.Children);
                }
            }
            Collect(_doc.Groups);

            string candidate = $"{parentId}.{slug}";
            int suffix = 2;
            while (existing.Contains(candidate))
                candidate = $"{parentId}.{slug}-{suffix++}";
            return candidate;
        }

        private static LibraryNode FindParent(List<LibraryNode> nodes, LibraryNode child)
        {
            if (child == null)
                return null;
            foreach (LibraryNode node in nodes)
            {
                if (node.Children == null)
                    continue;
                if (node.Children.Contains(child))
                    return node;
                LibraryNode hit = FindParent(node.Children, child);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        private static bool RemoveNode(List<LibraryNode> nodes, LibraryNode target)
        {
            if (nodes.Remove(target))
                return true;
            foreach (LibraryNode node in nodes)
                if (node.Children != null && RemoveNode(node.Children, target))
                    return true;
            return false;
        }

        private static int CountLinks(IEnumerable<LibraryNode> nodes)
        {
            int count = 0;
            foreach (LibraryNode node in nodes)
                count += node.IsGroup ? CountLinks(node.Children) : 1;
            return count;
        }
    }
}
