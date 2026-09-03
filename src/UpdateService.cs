using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal static class UpdateService
    {
        public const string ProjectUrl = "https://github.com/OnjLouis/NVDASync";

        private const string UserAgent = "NVDA Sync updater";
        private const string ContactUrl = "https://onj.me/contact";
        private const string DonateUrl = "https://onj.me/donate";
        private const string PackageAssetName = "NVDASync.zip";
        private const string PublicKeyXml = "<RSAKeyValue><Modulus>1cgrS/hqq/Tv17lL5WNKVYNXGR2+qribsuWehlHsxA8l0wEv508GQX6LwZQjjByTu1b3lu9GEtXrezXg76rLKt7/X4+sDRQnILH83IW8pRoaPURBqJsX/3wtHd1QEihgWmcgKcoaAk7phBzJCioMH/d2QKqghgydEKYShEITCPkNZ64ArzXhlw6aZ6FFu58uXf89u1m+B9jiBeCgBatH3OgXN7hRc3BCJm5HuLAAswAYUO0yv3hsZLDiqB8+9U8Dxv/Zzq1f5NJu+vpkYXQl5Y7RP3DETGijo/7zkFlTFSIZ8vFGyawhK4VP9TzMnpxooQPpPhOotbNHUvDeGBIz0l6MoTjhdrVXTt6YPFwr+JJidOjlvMyItZMdWtxQoWJAQFsDAEBrgx3R1jsYZGBWqNRIxrRD1xmYUSxMIyc49Jn0tpLuD2jV192O8m0+DAQNCZNij6acNu6zl6FNibVS8oHbK9HOmpwxTRwXU7KKQY7xSflvjIp8w6PLyfeBW9Cl</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public static void OpenProjectPage()
        {
            OpenUrl(ProjectUrl);
        }

        public static void OpenContactPage()
        {
            OpenUrl(ContactUrl);
        }

        public static void OpenDonatePage()
        {
            OpenUrl(DonateUrl);
        }

        public static void CheckForUpdates(IWin32Window owner, string currentVersion, Action exitApp)
        {
            try
            {
                var releases = FetchReleases();
                var release = LatestVersionedRelease(releases) ?? FetchLatestRelease();
                var latestVersionText = release == null ? string.Empty : (release.TagName ?? string.Empty).Trim().TrimStart('v', 'V');
                Version current;
                Version remote;
                if (string.IsNullOrWhiteSpace(latestVersionText) ||
                    !Version.TryParse(currentVersion, out current) ||
                    !Version.TryParse(latestVersionText, out remote))
                {
                    MessageBox.Show(owner, "Could not read the latest NVDA Sync release version.", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (remote <= current)
                {
                    MessageBox.Show(owner, "NVDA Sync is up to date. Current version: " + currentVersion + ".", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ShowUpdateAvailableDialog(owner, release, releases, current, remote, exitApp);
            }
            catch (WebException ex)
            {
                MessageBox.Show(owner, "Could not check for updates. GitHub releases may not exist yet, or the network request failed." + Environment.NewLine + Environment.NewLine + ex.Message, "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, ex.Message, "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void CheckForUpdatesAutomatic(IWin32Window owner, string currentVersion, Action exitApp, bool installSilently)
        {
            try
            {
                var releases = FetchReleases();
                var release = LatestVersionedRelease(releases) ?? FetchLatestRelease();
                var latestVersionText = release == null ? string.Empty : (release.TagName ?? string.Empty).Trim().TrimStart('v', 'V');
                Version current;
                Version remote;
                if (string.IsNullOrWhiteSpace(latestVersionText) ||
                    !Version.TryParse(currentVersion, out current) ||
                    !Version.TryParse(latestVersionText, out remote) ||
                    remote <= current)
                {
                    return;
                }

                if (installSilently)
                {
                    var package = FindSignedPackage(release);
                    if (package == null || package.Signature == null) return;
                    StartSelfUpdate(owner, package, remote.ToString(), exitApp, true);
                    return;
                }

                ShowUpdateAvailableDialog(owner, release, releases, current, remote, exitApp);
            }
            catch
            {
            }
        }

        private static void ShowUpdateAvailableDialog(IWin32Window owner, GitHubReleaseInfo release, IEnumerable<GitHubReleaseInfo> releases, Version current, Version remote, Action exitApp)
        {
            var latest = release == null ? remote.ToString() : (release.TagName ?? remote.ToString());
            var releaseUrl = release == null || string.IsNullOrWhiteSpace(release.HtmlUrl) ? ProjectUrl + "/releases" : release.HtmlUrl;
            var package = FindSignedPackage(release);
            var releaseNotes = BuildUpdateReleaseNotes(releases, current, remote);

            using (var dialog = new Form())
            {
                dialog.Text = "Update available";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Width = 720;
                dialog.Height = 520;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowIcon = false;
                dialog.ShowInTaskbar = false;

                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Top, Text = "NVDA Sync " + latest + " is available.", Padding = new Padding(0, 0, 0, 8) }, 0, 0);
                layout.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = releaseNotes, AccessibleName = "Release notes" }, 0, 1);

                var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0) };
                var laterButton = new Button { Text = "&Later", DialogResult = DialogResult.Cancel, AutoSize = true };
                var releaseButton = new Button { Text = "Open &release page", AutoSize = true };
                releaseButton.Click += delegate { OpenUrl(releaseUrl); };

                if (package != null && package.Signature != null)
                {
                    var installButton = new Button { Text = "&Download and install", AutoSize = true };
                    installButton.Click += delegate
                    {
                        dialog.DialogResult = DialogResult.OK;
                        dialog.Close();
                        StartSelfUpdate(owner, package, remote.ToString(), exitApp, false);
                    };
                    buttons.Controls.Add(installButton);
                    dialog.AcceptButton = installButton;
                }

                buttons.Controls.Add(releaseButton);
                buttons.Controls.Add(laterButton);
                if (package == null || package.Signature == null)
                {
                    buttons.Controls.Add(new Label { AutoSize = true, Text = "Automatic installation is unavailable because this release does not include both the ZIP and its signature." });
                }
                dialog.CancelButton = laterButton;
                layout.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(layout);
                dialog.ShowDialog(owner);
            }
        }

        private static void StartSelfUpdate(IWin32Window owner, SignedPackage package, string expectedVersion, Action exitApp, bool silent)
        {
            if (!silent && MessageBox.Show(
                    owner,
                    "NVDA Sync will close, verify the signed update, replace the files in this folder, and restart. Your Settings, Logs, and Backups folders will be kept." + Environment.NewLine + Environment.NewLine + "Do you want to continue?",
                    "Download and install",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var exePath = Application.ExecutablePath;
                var updaterTempDir = GetUpdaterTempDirectory(appDir);
                var updaterRoot = Path.Combine(updaterTempDir, "NVDASyncUpdater-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(updaterRoot);
                var updaterExe = Path.Combine(updaterRoot, "NVDA Sync Updater.exe");
                File.Copy(exePath, updaterExe, true);
                Process.Start(new ProcessStartInfo
                {
                    FileName = updaterExe,
                    Arguments =
                        "--apply-update" +
                        " --update-url " + CommandLineQuote(package.Zip.BrowserDownloadUrl) +
                        " --signature-url " + CommandLineQuote(package.Signature.BrowserDownloadUrl) +
                        " --update-version " + CommandLineQuote(expectedVersion) +
                        " --update-target " + CommandLineQuote(appDir) +
                        " --update-exe " + CommandLineQuote(exePath) +
                        " --update-temp " + CommandLineQuote(updaterTempDir) +
                        " --update-wait-pid " + Process.GetCurrentProcess().Id,
                    WorkingDirectory = updaterRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (exitApp != null) exitApp();
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show(owner, ex.Message, "Could not start updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static GitHubReleaseInfo FetchLatestRelease()
        {
            using (var client = CreateGitHubClient())
            {
                return ParseRelease(client.DownloadString(ApiBaseUrl() + "/releases/latest"));
            }
        }

        private static List<GitHubReleaseInfo> FetchReleases()
        {
            using (var client = CreateGitHubClient())
            {
                return ParseReleases(client.DownloadString(ApiBaseUrl() + "/releases?per_page=100"));
            }
        }

        private static WebClient CreateGitHubClient()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var client = new WebClient();
            client.Headers.Add("User-Agent", UserAgent);
            return client;
        }

        private static string ApiBaseUrl()
        {
            return ProjectUrl.Replace("https://github.com/", "https://api.github.com/repos/");
        }

        private static GitHubReleaseInfo LatestVersionedRelease(IEnumerable<GitHubReleaseInfo> releases)
        {
            return (releases ?? new List<GitHubReleaseInfo>())
                .Select(r => new { Release = r, Version = ReleaseVersion(r) })
                .Where(i => i.Version != null && !i.Release.Draft && !i.Release.Prerelease)
                .OrderByDescending(i => i.Version)
                .Select(i => i.Release)
                .FirstOrDefault();
        }

        private static Version ReleaseVersion(GitHubReleaseInfo release)
        {
            if (release == null || string.IsNullOrWhiteSpace(release.TagName)) return null;
            Version version;
            return Version.TryParse(release.TagName.Trim().TrimStart('v', 'V'), out version) ? version : null;
        }

        private static string BuildUpdateReleaseNotes(IEnumerable<GitHubReleaseInfo> releases, Version current, Version latest)
        {
            var newerReleases = (releases ?? new List<GitHubReleaseInfo>())
                .Select(r => new { Release = r, Version = ReleaseVersion(r) })
                .Where(i => i.Version != null && i.Version > current && i.Version <= latest && !i.Release.Draft && !i.Release.Prerelease)
                .OrderBy(i => i.Version)
                .ToList();

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Your version: " + current);
            builder.AppendLine("New version: " + latest);
            builder.AppendLine();
            builder.AppendLine("Changes between " + current + " and " + latest);
            if (newerReleases.Count == 0)
            {
                builder.AppendLine();
                builder.AppendLine("No release notes were provided for this update.");
                return builder.ToString().TrimEnd();
            }
            foreach (var item in newerReleases)
            {
                builder.AppendLine();
                builder.AppendLine(item.Release.TagName);
                builder.AppendLine(FormatReleaseNotesForDialog(item.Release.Body, "No release notes were provided for this update."));
            }
            return builder.ToString().TrimEnd();
        }

        internal static bool VerifyPackageSignature(string zipPath, string signaturePath)
        {
            var signatureText = File.ReadAllText(signaturePath, Encoding.ASCII).Trim();
            byte[] signature;
            try { signature = Convert.FromBase64String(signatureText); }
            catch (FormatException) { return false; }
            using (var rsa = new RSACryptoServiceProvider())
            {
                try
                {
                    rsa.FromXmlString(PublicKeyXml);
                    return rsa.VerifyData(File.ReadAllBytes(zipPath), CryptoConfig.MapNameToOID("SHA256"), signature);
                }
                finally { rsa.PersistKeyInCsp = false; }
            }
        }

        private static SignedPackage FindSignedPackage(GitHubReleaseInfo release)
        {
            if (release == null || release.Assets == null) return null;
            var zip = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, PackageAssetName, StringComparison.OrdinalIgnoreCase));
            if (zip == null) return null;
            var signature = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, PackageAssetName + ".sig", StringComparison.OrdinalIgnoreCase));
            return new SignedPackage { Zip = zip, Signature = signature };
        }

        private static string FormatReleaseNotesForDialog(string markdown, string emptyText)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return emptyText;
            var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                .Select(line => line.TrimEnd())
                .Select(line => line.StartsWith("#", StringComparison.Ordinal) ? line.TrimStart('#').Trim() : line)
                .Select(line => line.StartsWith("- ", StringComparison.Ordinal) ? "  " + line.Substring(2) : line)
                .Select(line => line.StartsWith("* ", StringComparison.Ordinal) ? "  " + line.Substring(2) : line);
            return string.Join(Environment.NewLine, lines).Trim();
        }

        private static List<GitHubReleaseInfo> ParseReleases(string json)
        {
            var serializer = new JavaScriptSerializer();
            var rows = serializer.DeserializeObject(json) as object[];
            if (rows == null) return new List<GitHubReleaseInfo>();
            return rows.Select(ParseReleaseObject).Where(r => r != null).ToList();
        }

        private static GitHubReleaseInfo ParseRelease(string json)
        {
            var serializer = new JavaScriptSerializer();
            return ParseReleaseObject(serializer.DeserializeObject(json));
        }

        private static GitHubReleaseInfo ParseReleaseObject(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map == null) return null;
            var release = new GitHubReleaseInfo
            {
                TagName = GetString(map, "tag_name"),
                HtmlUrl = GetString(map, "html_url"),
                Body = GetString(map, "body"),
                Draft = GetBool(map, "draft"),
                Prerelease = GetBool(map, "prerelease"),
                Assets = new List<GitHubReleaseAsset>()
            };
            object assetsValue;
            if (map.TryGetValue("assets", out assetsValue))
            {
                var assets = assetsValue as object[];
                if (assets != null)
                {
                    foreach (var assetValue in assets)
                    {
                        var assetMap = assetValue as Dictionary<string, object>;
                        if (assetMap == null) continue;
                        release.Assets.Add(new GitHubReleaseAsset { Name = GetString(assetMap, "name"), BrowserDownloadUrl = GetString(assetMap, "browser_download_url") });
                    }
                }
            }
            return release;
        }

        private static string GetString(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : string.Empty;
        }

        private static bool GetBool(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { }
        }

        private static string GetUpdaterTempDirectory(string appDir)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var candidate in new[] { string.IsNullOrWhiteSpace(localAppData) ? "" : Path.Combine(localAppData, "Temp"), Path.GetTempPath(), Path.Combine(appDir, "Settings", "Update Temp") })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                try
                {
                    var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
                    Directory.CreateDirectory(fullPath);
                    return fullPath;
                }
                catch { }
            }
            throw new InvalidOperationException("Could not create a temporary folder for the updater.");
        }

        private static string CommandLineQuote(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class GitHubReleaseInfo
        {
            public string TagName { get; set; }
            public string HtmlUrl { get; set; }
            public string Body { get; set; }
            public bool Draft { get; set; }
            public bool Prerelease { get; set; }
            public List<GitHubReleaseAsset> Assets { get; set; }
        }

        private sealed class GitHubReleaseAsset
        {
            public string Name { get; set; }
            public string BrowserDownloadUrl { get; set; }
        }

        private sealed class SignedPackage
        {
            public GitHubReleaseAsset Zip { get; set; }
            public GitHubReleaseAsset Signature { get; set; }
        }
    }
}
