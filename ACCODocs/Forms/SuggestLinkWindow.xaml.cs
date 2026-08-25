using System.Windows;
using ACCODocs.Logic.LinkLibrary;
// WinForms is enabled alongside WPF — disambiguate.
using Clipboard = System.Windows.Clipboard;

namespace ACCODocs.Forms
{
    /// <summary>
    /// "Suggest a link" (spec section 7): routes user contributions to the master
    /// maintainers. A dialog rather than a bare mailto because machines without a
    /// default mail client would otherwise see the button "do nothing" — the
    /// clipboard path always works.
    ///
    /// The mail method is config-driven (suggestionMailMethod): "gmail" opens a Gmail
    /// compose URL in the browser (ACCO runs Google Workspace), "mailto" uses the OS
    /// default handler. The subject carries a configurable prefix
    /// (suggestionSubjectPrefix) so admins can filter/label suggestion mail.
    /// </summary>
    public partial class SuggestLinkWindow : Window
    {
        private readonly LinkLibraryConfig _config;
        private readonly string _context;     // e.g. extracted element tags

        public SuggestLinkWindow(LinkLibraryConfig config, string context)
        {
            InitializeComponent();
            _config = config;
            _context = context ?? "";
            if (_context.Length > 0)
                TxtStatus.Text = $"Context that will be included: {_context}";
            TxtUrl.Focus();
        }

        /// <summary>Recipient address without any mailto: prefix; empty when unconfigured.</summary>
        private string RecipientAddress()
        {
            string recipient = _config.SuggestionRecipient ?? "";
            return recipient.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                ? recipient.Substring("mailto:".Length)
                : recipient;
        }

        private string BuildSubject()
        {
            string prefix = (_config.SuggestionSubjectPrefix ?? "").Trim();
            string title = TxtTitle.Text.Trim();
            string subject = title.Length > 0 ? $"Suggestion: {title}" : "Link suggestion";
            return prefix.Length > 0 ? $"{prefix} {subject}" : subject;
        }

        private string BuildBody()
        {
            return "Suggested link for the ACCO Link Library:\r\n\r\n" +
                   $"URL: {TxtUrl.Text.Trim()}\r\n" +
                   $"Title: {TxtTitle.Text.Trim()}\r\n" +
                   $"Suggested category: {TxtCategory.Text.Trim()}\r\n" +
                   $"Why it's useful: {TxtWhy.Text.Trim()}\r\n" +
                   (_context.Length > 0 ? $"Context: {_context}\r\n" : "") +
                   $"Suggested by: {Environment.UserDomainName}\\{Environment.UserName}\r\n";
        }

        private void BtnMail_Click(object sender, RoutedEventArgs e)
        {
            string to = RecipientAddress();
            if (to.Length == 0)
            {
                TxtStatus.Text = "No suggestion recipient is configured (suggestionRecipient in LinkLibrary.config.json). Use Copy to Clipboard instead.";
                return;
            }

            string subject = Uri.EscapeDataString(BuildSubject());
            string body = Uri.EscapeDataString(BuildBody());

            // "gmail" → compose URL in the browser, prefilled To/Subject/Body.
            // "mailto" → whatever the OS default mail handler is.
            string url = string.Equals(_config.SuggestionMailMethod, "gmail", StringComparison.OrdinalIgnoreCase)
                ? $"https://mail.google.com/mail/?view=cm&fs=1&to={Uri.EscapeDataString(to)}&su={subject}&body={body}"
                : $"mailto:{to}?subject={subject}&body={body}";

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                TxtStatus.Text = "Mail draft opened.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkLibrary] Suggest mail failed: {ex.Message}");
                TxtStatus.Text = "No mail app could be opened — use Copy to Clipboard and paste into an email or Teams message.";
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string to = RecipientAddress();
                string text =
                    (to.Length > 0 ? $"To: {to}\r\n" : "") +
                    $"Subject: {BuildSubject()}\r\n\r\n" +
                    BuildBody();
                Clipboard.SetText(text);
                TxtStatus.Text = "Copied — paste into an email or Teams message.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Clipboard failed: {ex.Message}";
            }
        }
    }
}
