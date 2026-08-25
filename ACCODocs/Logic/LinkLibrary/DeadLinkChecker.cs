using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ACCODocs.Logic.LinkLibrary
{
    /// <summary>
    /// Background dead-link validation (spec section 10): flags failures TO US, NOT to
    /// the user — one broken link and people quietly go back to their browser. Results
    /// land as JSONL next to the telemetry so the inventory script collects them.
    /// Runs at most once per Revit session, entirely on a background thread.
    /// </summary>
    public static class DeadLinkChecker
    {
        private static bool _ranThisSession;

        public static void RunOnce(LinkLibraryConfig config, IEnumerable<LibrarySearch.SearchEntry> entries)
        {
            if (_ranThisSession || entries == null)
                return;
            _ranThisSession = true;

            var urls = entries
                .Where(e => e.Node.Kind == "url" || e.Node.Kind == "video")
                .Select(e => new { e.Node.Id, e.Node.Target })
                .Where(x => !string.IsNullOrWhiteSpace(x.Target))
                .GroupBy(x => x.Target)
                .Select(g => g.First())
                .ToList();

            if (urls.Count == 0)
                return;

            Task.Run(() =>
            {
                var dead = new List<object>();
                foreach (var link in urls)
                {
                    string failure = Probe(link.Target);
                    if (failure != null)
                        dead.Add(new { ts = DateTime.UtcNow.ToString("o"), linkId = link.Id, target = link.Target, failure });
                }

                Debug.WriteLine($"[LinkLibrary] Dead-link check: {urls.Count} checked, {dead.Count} failed.");
                if (dead.Count == 0)
                    return;

                try
                {
                    string folder = config.ExpandedTelemetryFolder;
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "deadlinks_" + Environment.UserName + ".jsonl");
                    File.AppendAllLines(path, dead.Select(d => JsonConvert.SerializeObject(d)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LinkLibrary] Dead-link report write failed: {ex.Message}");
                }
            });
        }

        /// <summary>Null when the URL responds; otherwise a short failure description.</summary>
        private static string Probe(string url)
        {
            try
            {
                // HttpWebRequest (not HttpClient) so the net48 configs (Revit 2023/2024)
                // need no extra assembly reference. SYSLIB0014 is the net8 obsolescence
                // nudge toward HttpClient — deliberate trade-off here.
#pragma warning disable SYSLIB0014
                var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
                request.Method = "HEAD";
                request.Timeout = 10000;
                request.AllowAutoRedirect = true;
                using (var response = (HttpWebResponse)request.GetResponse())
                    return null;
            }
            catch (WebException ex)
            {
                var status = (ex.Response as HttpWebResponse)?.StatusCode;
                // Some servers reject HEAD (405/501) but serve GET fine — not a dead link.
                if (status == HttpStatusCode.MethodNotAllowed || status == HttpStatusCode.NotImplemented)
                    return null;
                return status.HasValue ? $"HTTP {(int)status}" : ex.Status.ToString();
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
