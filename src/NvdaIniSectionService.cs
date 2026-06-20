using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NvdaAddonSync
{
    internal sealed class IniSection
    {
        public string Name { get; set; }
        public List<string> RawLines { get; set; }

        public IniSection()
        {
            RawLines = new List<string>();
        }
    }

    internal sealed class NvdaIniParseResult
    {
        public List<string> PreambleLines { get; set; }
        public List<IniSection> Sections { get; set; }
        public string LineEnding { get; set; }
        public bool HasTrailingNewline { get; set; }

        public NvdaIniParseResult()
        {
            PreambleLines = new List<string>();
            Sections = new List<IniSection>();
            LineEnding = Environment.NewLine;
        }
    }

    internal sealed class IniSectionOperationResult
    {
        public bool DestinationCreated { get; set; }
        public bool DestinationSectionOverwritten { get; set; }
        public string Message { get; set; }
        public List<IniFileChangeStat> Stats { get; set; }

        public IniSectionOperationResult()
        {
            Stats = new List<IniFileChangeStat>();
        }
    }

    internal sealed class IniFileChangeStat
    {
        public string Path { get; set; }
        public long SizeBefore { get; set; }
        public long SizeAfter { get; set; }
        public int LinesBefore { get; set; }
        public int LinesAfter { get; set; }

        public int LinesRemoved
        {
            get { return Math.Max(0, LinesBefore - LinesAfter); }
        }

        public string ToLogMessage()
        {
            return "Stats for " + Path + ":" + Environment.NewLine +
                   "Size before edits: " + FormatSize(SizeBefore) + " (" + LinesBefore + " lines)" + Environment.NewLine +
                   "Size after edits: " + FormatSize(SizeAfter) + " (" + LinesAfter + " lines)" + Environment.NewLine +
                   "Lines removed: " + LinesRemoved;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " bytes";
            }
            if (bytes < 1024 * 1024)
            {
                return (bytes / 1024.0).ToString("0.#") + " KB";
            }
            return (bytes / 1024.0 / 1024.0).ToString("0.##") + " MB";
        }
    }

    internal static class NvdaIniSectionService
    {
        public static event Action<string> Message;

        public static NvdaIniParseResult ParseFile(string iniPath)
        {
            if (string.IsNullOrWhiteSpace(iniPath))
            {
                throw new ArgumentException("No nvda.ini path was supplied.", "iniPath");
            }
            if (!File.Exists(iniPath))
            {
                throw new FileNotFoundException("nvda.ini was not found.", iniPath);
            }

            var text = File.ReadAllText(iniPath, Encoding.UTF8);
            var result = new NvdaIniParseResult();
            result.LineEnding = DetectLineEnding(text);
            result.HasTrailingNewline = text.EndsWith("\r\n", StringComparison.Ordinal) ||
                                        text.EndsWith("\n", StringComparison.Ordinal) ||
                                        text.EndsWith("\r", StringComparison.Ordinal);

            var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = new List<string>(normalized.Split(new[] { '\n' }));
            if (result.HasTrailingNewline && lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            IniSection currentSection = null;
            foreach (var line in lines)
            {
                string name;
                if (TryReadTopLevelSectionName(line, out name))
                {
                    currentSection = new IniSection { Name = name };
                    currentSection.RawLines.Add(line);
                    result.Sections.Add(currentSection);
                    continue;
                }

                if (currentSection == null)
                {
                    result.PreambleLines.Add(line);
                }
                else
                {
                    currentSection.RawLines.Add(line);
                }
            }

            return result;
        }

        public static List<string> GetSectionNames(string iniPath)
        {
            var names = new List<string>();
            foreach (var section in ParseFile(iniPath).Sections)
            {
                names.Add(section.Name);
            }
            return names;
        }

        public static IniSectionOperationResult DeleteSection(string iniPath, string sectionName)
        {
            return DeleteSections(iniPath, new[] { sectionName });
        }

        public static IniSectionOperationResult DeleteSections(string iniPath, IEnumerable<string> sectionNames)
        {
            try
            {
                var stats = CaptureFileStats(iniPath);
                var parsed = ParseFile(iniPath);
                var deleted = 0;
                foreach (var sectionName in sectionNames)
                {
                    var index = FindSectionIndex(parsed.Sections, sectionName);
                    if (index < 0)
                    {
                        throw new InvalidOperationException("Section [" + sectionName + "] was not found.");
                    }
                    parsed.Sections.RemoveAt(index);
                    deleted++;
                }
                WriteParsedFile(iniPath, parsed);
                stats = CompleteFileStats(stats, iniPath);
                var message = "Deleted " + deleted + " section(s) from " + iniPath;
                Notify(message);
                Notify(stats.ToLogMessage());
                return new IniSectionOperationResult { Message = message, Stats = new List<IniFileChangeStat> { stats } };
            }
            catch (IOException ex)
            {
                throw new IOException("Could not delete selected section(s) from nvda.ini: " + ex.Message, ex);
            }
        }

        public static IniSectionOperationResult MoveSection(string sourceIniPath, string destinationIniPath, string sectionName, bool overwriteIfExists)
        {
            return MoveSections(sourceIniPath, destinationIniPath, new[] { sectionName }, overwriteIfExists);
        }

        public static IniSectionOperationResult MoveSections(string sourceIniPath, string destinationIniPath, IEnumerable<string> sectionNames, bool overwriteIfExists)
        {
            try
            {
                var source = ParseFile(sourceIniPath);
                var sectionsToMove = new List<IniSection>();
                foreach (var sectionName in sectionNames)
                {
                    var sourceIndex = FindSectionIndex(source.Sections, sectionName);
                    if (sourceIndex < 0)
                    {
                        throw new InvalidOperationException("Section [" + sectionName + "] was not found in the source nvda.ini.");
                    }
                    sectionsToMove.Add(CloneSection(source.Sections[sourceIndex]));
                }

                var destinationCreated = false;
                var overwritten = false;
                var destinationStats = CaptureFileStats(destinationIniPath);
                var sourceStats = CaptureFileStats(sourceIniPath);

                NvdaIniParseResult destination;
                if (File.Exists(destinationIniPath))
                {
                    destination = ParseFile(destinationIniPath);
                }
                else
                {
                    destination = new NvdaIniParseResult();
                    destination.LineEnding = source.LineEnding;
                    destination.HasTrailingNewline = true;
                    var parent = Path.GetDirectoryName(destinationIniPath);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }
                    destinationCreated = true;
                }

                foreach (var sectionToMove in sectionsToMove)
                {
                    AddOrReplaceDestinationSection(destination, sectionToMove, overwriteIfExists, ref overwritten);
                }
                WriteParsedFile(destinationIniPath, destination);
                destinationStats = CompleteFileStats(destinationStats, destinationIniPath);

                foreach (var sectionToMove in sectionsToMove)
                {
                    var sourceIndex = FindSectionIndex(source.Sections, sectionToMove.Name);
                    if (sourceIndex >= 0)
                    {
                        source.Sections.RemoveAt(sourceIndex);
                    }
                }
                try
                {
                    WriteParsedFile(sourceIniPath, source);
                    sourceStats = CompleteFileStats(sourceStats, sourceIniPath);
                }
                catch (Exception ex)
                {
                    Notify("Moved selected section(s) to the destination, but could not remove them from the source: " + ex.Message);
                    throw;
                }

                var message = "Moved " + sectionsToMove.Count + " section(s) to " + destinationIniPath;
                if (overwritten)
                {
                    message += " and replaced the destination copy.";
                }
                else if (destinationCreated)
                {
                    message += " and created the destination nvda.ini.";
                }
                else
                {
                    message += ".";
                }
                Notify(message);
                Notify(destinationStats.ToLogMessage());
                Notify(sourceStats.ToLogMessage());
                return new IniSectionOperationResult
                {
                    DestinationCreated = destinationCreated,
                    DestinationSectionOverwritten = overwritten,
                    Message = message,
                    Stats = new List<IniFileChangeStat> { destinationStats, sourceStats }
                };
            }
            catch (IOException ex)
            {
                throw new IOException("Could not move selected section(s) between nvda.ini files: " + ex.Message, ex);
            }
        }

        public static IniSectionOperationResult CopySection(string sourceIniPath, string destinationIniPath, string sectionName, bool overwriteIfExists)
        {
            return CopySections(sourceIniPath, destinationIniPath, new[] { sectionName }, overwriteIfExists);
        }

        public static IniSectionOperationResult CopySections(string sourceIniPath, string destinationIniPath, IEnumerable<string> sectionNames, bool overwriteIfExists)
        {
            try
            {
                var source = ParseFile(sourceIniPath);
                var sectionsToCopy = new List<IniSection>();
                foreach (var sectionName in sectionNames)
                {
                    var sourceIndex = FindSectionIndex(source.Sections, sectionName);
                    if (sourceIndex < 0)
                    {
                        throw new InvalidOperationException("Section [" + sectionName + "] was not found in the source nvda.ini.");
                    }
                    sectionsToCopy.Add(CloneSection(source.Sections[sourceIndex]));
                }

                var destinationCreated = false;
                var overwritten = false;
                var destinationStats = CaptureFileStats(destinationIniPath);

                NvdaIniParseResult destination;
                if (File.Exists(destinationIniPath))
                {
                    destination = ParseFile(destinationIniPath);
                }
                else
                {
                    destination = new NvdaIniParseResult();
                    destination.LineEnding = source.LineEnding;
                    destination.HasTrailingNewline = true;
                    var parent = Path.GetDirectoryName(destinationIniPath);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }
                    destinationCreated = true;
                }

                foreach (var sectionToCopy in sectionsToCopy)
                {
                    AddOrReplaceDestinationSection(destination, sectionToCopy, overwriteIfExists, ref overwritten);
                }
                WriteParsedFile(destinationIniPath, destination);
                destinationStats = CompleteFileStats(destinationStats, destinationIniPath);

                var message = "Copied " + sectionsToCopy.Count + " section(s) to " + destinationIniPath;
                if (overwritten)
                {
                    message += " and replaced the destination copy.";
                }
                else if (destinationCreated)
                {
                    message += " and created the destination nvda.ini.";
                }
                else
                {
                    message += ".";
                }
                Notify(message);
                Notify(destinationStats.ToLogMessage());
                return new IniSectionOperationResult
                {
                    DestinationCreated = destinationCreated,
                    DestinationSectionOverwritten = overwritten,
                    Message = message,
                    Stats = new List<IniFileChangeStat> { destinationStats }
                };
            }
            catch (IOException ex)
            {
                throw new IOException("Could not copy selected section(s) between nvda.ini files: " + ex.Message, ex);
            }
        }

        private static void AddOrReplaceDestinationSection(NvdaIniParseResult destination, IniSection section, bool overwriteIfExists, ref bool overwritten)
        {
            var destinationIndex = FindSectionIndex(destination.Sections, section.Name);
            if (destinationIndex >= 0)
            {
                if (!overwriteIfExists)
                {
                    throw new InvalidOperationException("The destination already contains [" + section.Name + "].");
                }
                destination.Sections[destinationIndex] = CloneSection(section);
                overwritten = true;
            }
            else
            {
                destination.Sections.Add(CloneSection(section));
            }
        }

        private static IniFileChangeStat CaptureFileStats(string path)
        {
            var stat = new IniFileChangeStat { Path = path ?? string.Empty };
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var info = new FileInfo(path);
                stat.SizeBefore = info.Length;
                stat.LinesBefore = CountPhysicalLines(File.ReadAllText(path, Encoding.UTF8));
            }
            return stat;
        }

        private static IniFileChangeStat CompleteFileStats(IniFileChangeStat stat, string path)
        {
            if (stat == null)
            {
                stat = new IniFileChangeStat();
            }
            stat.Path = path ?? stat.Path;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var info = new FileInfo(path);
                stat.SizeAfter = info.Length;
                stat.LinesAfter = CountPhysicalLines(File.ReadAllText(path, Encoding.UTF8));
            }
            return stat;
        }

        private static int CountPhysicalLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            var count = 1;
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '\n')
                {
                    count++;
                }
            }
            if (text.EndsWith("\n", StringComparison.Ordinal))
            {
                count--;
            }
            return count;
        }

        private static bool TryReadTopLevelSectionName(string line, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(line) || line[0] != '[')
            {
                return false;
            }
            if (line.Length < 3 || line[1] == '[' || line[line.Length - 1] != ']')
            {
                return false;
            }
            var candidate = line.Substring(1, line.Length - 2);
            if (candidate.IndexOf('[') >= 0 || candidate.IndexOf(']') >= 0 || candidate.Length == 0)
            {
                return false;
            }
            name = candidate;
            return true;
        }

        private static int FindSectionIndex(List<IniSection> sections, string sectionName)
        {
            for (var index = 0; index < sections.Count; index++)
            {
                if (string.Equals(sections[index].Name, sectionName, StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return -1;
        }

        private static IniSection CloneSection(IniSection source)
        {
            return new IniSection
            {
                Name = source.Name,
                RawLines = new List<string>(source.RawLines)
            };
        }

        private static string DetectLineEnding(string text)
        {
            var crlf = CountOccurrences(text, "\r\n");
            var lf = CountOccurrences(text, "\n") - crlf;
            return crlf >= lf ? "\r\n" : "\n";
        }

        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            {
                return 0;
            }
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static void WriteParsedFile(string path, NvdaIniParseResult parsed)
        {
            var content = BuildContent(parsed);
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }
            if (File.Exists(path))
            {
                var backupPath = CreateBackup(path);
                Notify("Backed up " + path + " to " + backupPath);
            }
            var tempPath = path + ".nvdaSync.tmp";
            File.WriteAllText(tempPath, content, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, null);
                    }
                    catch
                    {
                        File.Delete(path);
                        File.Move(tempPath, path);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }

        private static string CreateBackup(string path)
        {
            var folder = Path.GetDirectoryName(path);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(folder, "nvda" + stamp + ".ini");
            var counter = 2;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(folder, "nvda" + stamp + "-" + counter + ".ini");
                counter++;
            }
            File.Copy(path, backupPath, false);
            return backupPath;
        }

        private static string BuildContent(NvdaIniParseResult parsed)
        {
            var lines = new List<string>();
            lines.AddRange(parsed.PreambleLines);
            foreach (var section in parsed.Sections)
            {
                lines.AddRange(section.RawLines);
            }
            var content = string.Join(parsed.LineEnding, lines.ToArray());
            if (parsed.HasTrailingNewline && (lines.Count > 0 || content.Length > 0))
            {
                content += parsed.LineEnding;
            }
            return content;
        }

        private static void Notify(string message)
        {
            var handler = Message;
            if (handler != null)
            {
                handler(message);
            }
        }
    }
}
