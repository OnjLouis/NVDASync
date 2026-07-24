using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NvdaAddonSync
{
    internal enum IniSectionOwner
    {
        NvdaCore,
        Addon,
        Unknown
    }

    internal sealed class IniSectionClassification
    {
        public string SectionName { get; set; }
        public IniSectionOwner Owner { get; set; }
    }

    internal sealed class IniSectionClassificationResult
    {
        public List<IniSectionClassification> Sections { get; private set; }
        public bool AddonScanComplete { get; set; }

        public IniSectionClassificationResult()
        {
            Sections = new List<IniSectionClassification>();
            AddonScanComplete = true;
        }

        public int Count(IniSectionOwner owner)
        {
            var count = 0;
            foreach (var section in Sections)
            {
                if (section.Owner == owner)
                {
                    count++;
                }
            }
            return count;
        }

        public HashSet<string> AddonSectionNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in Sections)
            {
                if (section.Owner == IniSectionOwner.Addon)
                {
                    names.Add(section.SectionName);
                }
            }
            return names;
        }

        public HashSet<string> OrphanCandidateSectionNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in Sections)
            {
                if (section.Owner == IniSectionOwner.Unknown)
                {
                    names.Add(section.SectionName);
                }
            }
            return names;
        }
    }

    internal static class AddonIniSectionClassifier
    {
        private const long MaximumPythonFileSize = 8L * 1024L * 1024L;
        private const int MaximumPythonFilesPerAddon = 1000;

        private static readonly HashSet<string> NvdaCoreSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "general",
            "speech",
            "audio",
            "braille",
            "vision",
            "magnifier",
            "presentation",
            "mouse",
            "speechViewer",
            "brailleViewer",
            "keyboard",
            "virtualBuffers",
            "touch",
            "documentFormatting",
            "documentNavigation",
            "reviewCursor",
            "UIA",
            "annotations",
            "terminals",
            "update",
            "inputComposition",
            "debugLog",
            "uwpOcr",
            "windowsOCR",
            "editableText",
            "development",
            "featureFlag",
            "addonStore",
            "remote",
            "math",
            "screenCurtain",
            "upgrade",
            "screenReaderMode"
        };

        private static readonly HashSet<string> SkippedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            "__pycache__",
            "doc",
            "docs",
            "locale",
            "locales",
            "node_modules",
            "chromeProfiles",
            "browserProfiles",
            "venv",
            ".venv"
        };

        private static readonly Regex LiteralConfigSectionRegex = new Regex(
            "config\\.conf(?:\\.spec)?\\s*\\[\\s*[rRuUbBfF]*[\\\"'](?<name>[^\\\"']+)[\\\"']\\s*\\]",
            RegexOptions.Compiled);

        private static readonly Regex ConfigGetSectionRegex = new Regex(
            "config\\.conf\\.get\\(\\s*[rRuUbBfF]*[\\\"'](?<name>[^\\\"']+)[\\\"']",
            RegexOptions.Compiled);

        private static readonly Regex StringAssignmentRegex = new Regex(
            "(?m)^\\s*(?<identifier>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*[rRuUbBfF]*[\\\"'](?<value>[^\\\"']+)[\\\"']",
            RegexOptions.Compiled);

        public static IniSectionClassificationResult Classify(
            string nvdaConfigDirectory,
            IEnumerable<string> sectionNames)
        {
            var result = new IniSectionClassificationResult();
            var scanComplete = true;
            var addonSections = DiscoverAddonSectionNames(nvdaConfigDirectory, ref scanComplete);
            result.AddonScanComplete = scanComplete;
            foreach (var sectionName in sectionNames)
            {
                var owner = IniSectionOwner.Unknown;
                if (NvdaCoreSections.Contains(sectionName))
                {
                    owner = IniSectionOwner.NvdaCore;
                }
                else if (MatchesAddonSection(addonSections, sectionName))
                {
                    owner = IniSectionOwner.Addon;
                }
                result.Sections.Add(new IniSectionClassification
                {
                    SectionName = sectionName,
                    Owner = owner
                });
            }
            return result;
        }

        private static HashSet<string> DiscoverAddonSectionNames(string nvdaConfigDirectory, ref bool scanComplete)
        {
            var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(nvdaConfigDirectory) || !Directory.Exists(nvdaConfigDirectory))
            {
                scanComplete = false;
                return sections;
            }

            foreach (var addonsDirectory in FindAddonDirectories(nvdaConfigDirectory, ref scanComplete))
            {
                string[] addonDirectories;
                try
                {
                    addonDirectories = Directory.GetDirectories(addonsDirectory);
                }
                catch
                {
                    scanComplete = false;
                    continue;
                }
                foreach (var addonDirectory in addonDirectories)
                {
                    var manifestPath = Path.Combine(addonDirectory, "manifest.ini");
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }
                    var addonIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    addonIdentities.Add(Path.GetFileName(addonDirectory));
                    sections.Add(Path.GetFileName(addonDirectory));
                    var manifestName = ReadManifestName(manifestPath, ref scanComplete);
                    if (!string.IsNullOrWhiteSpace(manifestName))
                    {
                        addonIdentities.Add(manifestName);
                        sections.Add(manifestName);
                    }
                    ScanAddonPython(addonDirectory, addonIdentities, sections, ref scanComplete);
                }
            }
            return sections;
        }

        private static IEnumerable<string> FindAddonDirectories(string nvdaConfigDirectory, ref bool scanComplete)
        {
            var results = new List<string>();
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(nvdaConfigDirectory);
            }
            catch
            {
                scanComplete = false;
                return results;
            }
            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (string.Equals(name, "addons", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("addons.", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("addons-", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(directory);
                }
            }
            return results;
        }

        private static string ReadManifestName(string manifestPath, ref bool scanComplete)
        {
            try
            {
                foreach (var line in File.ReadLines(manifestPath, Encoding.UTF8))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("name", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex < 0 || !string.Equals(trimmed.Substring(0, equalsIndex).Trim(), "name", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    return Unquote(trimmed.Substring(equalsIndex + 1).Trim());
                }
            }
            catch
            {
                scanComplete = false;
            }
            return string.Empty;
        }

        private static void ScanAddonPython(string addonDirectory, HashSet<string> addonIdentities, HashSet<string> sections, ref bool scanComplete)
        {
            var filesRead = 0;
            foreach (var pythonFile in EnumerateOwnedPythonFiles(addonDirectory, addonIdentities, ref scanComplete))
            {
                if (filesRead >= MaximumPythonFilesPerAddon)
                {
                    scanComplete = false;
                    break;
                }
                filesRead++;
                string source;
                try
                {
                    var info = new FileInfo(pythonFile);
                    if (info.Length > MaximumPythonFileSize)
                    {
                        scanComplete = false;
                        continue;
                    }
                    source = File.ReadAllText(pythonFile, Encoding.UTF8);
                }
                catch
                {
                    scanComplete = false;
                    continue;
                }

                AddNamedMatches(LiteralConfigSectionRegex, source, sections);
                AddNamedMatches(ConfigGetSectionRegex, source, sections);
                AddVariableConfigSections(source, sections);
            }
        }

        private static IEnumerable<string> EnumerateOwnedPythonFiles(string addonDirectory, HashSet<string> addonIdentities, ref bool scanComplete)
        {
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(addonDirectory);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                string[] childDirectories;
                string[] childFiles;
                try
                {
                    childDirectories = Directory.GetDirectories(current);
                    childFiles = Directory.GetFiles(current, "*.py");
                }
                catch
                {
                    scanComplete = false;
                    continue;
                }
                foreach (var file in childFiles)
                {
                    files.Add(file);
                }
                foreach (var childDirectory in childDirectories)
                {
                    var childName = Path.GetFileName(childDirectory);
                    if (SkippedDirectoryNames.Contains(childName))
                    {
                        continue;
                    }
                    var currentName = Path.GetFileName(current);
                    if ((string.Equals(currentName, "lib", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(currentName, "libs", StringComparison.OrdinalIgnoreCase)) &&
                        !MatchesAddonIdentity(addonIdentities, childName))
                    {
                        continue;
                    }
                    pending.Push(childDirectory);
                }
            }
            return files;
        }

        private static bool MatchesAddonIdentity(HashSet<string> addonIdentities, string value)
        {
            var normalizedValue = NormalizeName(value);
            foreach (var identity in addonIdentities)
            {
                if (string.Equals(NormalizeName(identity), normalizedValue, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddNamedMatches(Regex regex, string source, HashSet<string> sections)
        {
            foreach (Match match in regex.Matches(source))
            {
                var value = match.Groups["name"].Value.Trim();
                if (value.Length > 0)
                {
                    sections.Add(value);
                }
            }
        }

        private static void AddVariableConfigSections(string source, HashSet<string> sections)
        {
            foreach (Match assignment in StringAssignmentRegex.Matches(source))
            {
                var identifier = assignment.Groups["identifier"].Value;
                var usePattern = "config\\.conf(?:\\.spec)?\\s*\\[\\s*" + Regex.Escape(identifier) + "\\s*\\]";
                var getPattern = "config\\.conf\\.get\\(\\s*" + Regex.Escape(identifier) + "(?:\\s*[,\\)])";
                if (Regex.IsMatch(source, usePattern) || Regex.IsMatch(source, getPattern))
                {
                    sections.Add(assignment.Groups["value"].Value.Trim());
                }
            }
        }

        private static bool MatchesAddonSection(HashSet<string> addonSections, string sectionName)
        {
            if (addonSections.Contains(sectionName))
            {
                return true;
            }
            var normalizedSection = NormalizeName(sectionName);
            if (normalizedSection.Length == 0)
            {
                return false;
            }
            foreach (var addonSection in addonSections)
            {
                if (string.Equals(NormalizeName(addonSection), normalizedSection, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeName(string value)
        {
            var normalized = new StringBuilder();
            foreach (var character in value ?? string.Empty)
            {
                if (char.IsLetterOrDigit(character))
                {
                    normalized.Append(char.ToLowerInvariant(character));
                }
            }
            return normalized.ToString();
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                return value.Substring(1, value.Length - 2).Trim();
            }
            return value;
        }
    }
}
