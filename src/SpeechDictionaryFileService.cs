using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NvdaAddonSync
{
    internal sealed class SpeechDictionaryEntry
    {
        public int Index { get; set; }
        public string Pattern { get; set; }
        public string Replacement { get; set; }
        public string CaseSensitiveRaw { get; set; }
        public string TypeRaw { get; set; }
        public string Comment { get; set; }
        public string SourceLine { get; set; }

        public string DisplayCaseSensitive
        {
            get { return CaseSensitiveRaw == "1" ? "Yes" : "No"; }
        }
    }

    internal sealed class SpeechDictionaryRecord
    {
        public SpeechDictionaryEntry Entry { get; set; }
        public List<string> RawLines { get; set; }

        public SpeechDictionaryRecord()
        {
            RawLines = new List<string>();
        }
    }

    internal sealed class SpeechDictionaryParseResult
    {
        public List<SpeechDictionaryEntry> Entries { get; set; }
        public List<SpeechDictionaryRecord> Records { get; set; }
        public string LineEnding { get; set; }
        public bool HasTrailingNewline { get; set; }

        public SpeechDictionaryParseResult()
        {
            Entries = new List<SpeechDictionaryEntry>();
            Records = new List<SpeechDictionaryRecord>();
            LineEnding = "\r\n";
            HasTrailingNewline = true;
        }
    }

    internal sealed class SpeechDictionaryFileInfo
    {
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public string RelativePath { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    internal static class SpeechDictionaryFileService
    {
        public static event Action<string> Message;

        public static List<SpeechDictionaryFileInfo> DiscoverDictionaryFiles(string nvdaConfigFolder)
        {
            var result = new List<SpeechDictionaryFileInfo>();
            if (string.IsNullOrWhiteSpace(nvdaConfigFolder))
            {
                return result;
            }
            var speechDicts = Path.Combine(nvdaConfigFolder, "speechDicts");
            if (!Directory.Exists(speechDicts))
            {
                return result;
            }

            var defaultPath = Path.Combine(speechDicts, "default.dic");
            if (File.Exists(defaultPath))
            {
                result.Add(new SpeechDictionaryFileInfo
                {
                    DisplayName = "Default",
                    Path = defaultPath,
                    RelativePath = "default.dic"
                });
            }

            var voiceRoot = Path.Combine(speechDicts, "voiceDicts.v1");
            if (Directory.Exists(voiceRoot))
            {
                foreach (var synthDirectory in Directory.EnumerateDirectories(voiceRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    var synth = Path.GetFileName(synthDirectory);
                    foreach (var file in Directory.EnumerateFiles(synthDirectory, "*.dic", SearchOption.TopDirectoryOnly))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var voice = fileName;
                        var prefix = synth + "-";
                        if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            voice = fileName.Substring(prefix.Length);
                        }
                        var display = string.Equals(fileName, synth, StringComparison.OrdinalIgnoreCase)
                            ? "Voice dictionary: " + synth
                            : "Voice dictionary: " + synth + " (" + voice + ")";
                        result.Add(new SpeechDictionaryFileInfo
                        {
                            DisplayName = display,
                            Path = file,
                            RelativePath = GetRelativePath(speechDicts, file)
                        });
                    }
                }
            }

            foreach (var file in Directory.EnumerateFiles(speechDicts, "*.dic", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(file), "default.dic", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                result.Add(new SpeechDictionaryFileInfo
                {
                    DisplayName = "Voice dictionary (legacy): " + Path.GetFileNameWithoutExtension(file),
                    Path = file,
                    RelativePath = Path.GetFileName(file)
                });
            }

            result.Sort(delegate(SpeechDictionaryFileInfo left, SpeechDictionaryFileInfo right)
            {
                return string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        public static SpeechDictionaryParseResult ParseFile(string dicPath)
        {
            if (string.IsNullOrWhiteSpace(dicPath))
            {
                throw new ArgumentException("No dictionary path was supplied.", "dicPath");
            }
            if (!File.Exists(dicPath))
            {
                throw new FileNotFoundException(Path.GetFileName(dicPath) + " was not found.", dicPath);
            }

            var text = File.ReadAllText(dicPath, new UTF8Encoding(true));
            var result = new SpeechDictionaryParseResult();
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

            var pendingComments = new List<string>();
            var entryIndex = 0;
            foreach (var line in lines)
            {
                if (line.Trim().Length == 0)
                {
                    AddRawRecord(result, pendingComments);
                    pendingComments.Clear();
                    AddRawRecord(result, new[] { line });
                    continue;
                }
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    pendingComments.Add(line);
                    continue;
                }
                var fields = line.Split('\t');
                if (fields.Length == 4)
                {
                    var entry = new SpeechDictionaryEntry
                    {
                        Index = entryIndex++,
                        Pattern = UnescapeField(fields[0]),
                        Replacement = UnescapeField(fields[1]),
                        CaseSensitiveRaw = fields[2],
                        TypeRaw = fields[3],
                        Comment = CommentText(pendingComments),
                        SourceLine = line
                    };
                    var record = new SpeechDictionaryRecord { Entry = entry };
                    record.RawLines.AddRange(pendingComments);
                    record.RawLines.Add(line);
                    result.Records.Add(record);
                    result.Entries.Add(entry);
                    pendingComments.Clear();
                    continue;
                }
                AddRawRecord(result, pendingComments);
                pendingComments.Clear();
                AddRawRecord(result, new[] { line });
            }
            AddRawRecord(result, pendingComments);
            return result;
        }

        public static IniSectionOperationResult DeleteEntries(string dicPath, IEnumerable<int> indices)
        {
            var parsed = ParseFile(dicPath);
            var selected = SelectedIndexSet(indices);
            var stats = CaptureFileStats(dicPath);
            var deleted = parsed.Records.RemoveAll(record => record.Entry != null && selected.Contains(record.Entry.Index));
            if (deleted == 0)
            {
                throw new InvalidOperationException("No selected dictionary entries were found.");
            }
            WriteParsedFile(dicPath, parsed);
            stats = CompleteFileStats(stats, dicPath);
            var message = "Deleted " + FormatCount(deleted, "dictionary entry", "dictionary entries") + " from " + dicPath;
            Notify(message);
            Notify(stats.ToLogMessage());
            return new IniSectionOperationResult { Message = message, Stats = new List<IniFileChangeStat> { stats } };
        }

        public static IniSectionOperationResult CopyEntries(string sourceDicPath, string destinationDicPath, IEnumerable<int> sourceIndices)
        {
            return CopyOrMoveEntries(sourceDicPath, destinationDicPath, sourceIndices, false);
        }

        public static IniSectionOperationResult MoveEntries(string sourceDicPath, string destinationDicPath, IEnumerable<int> sourceIndices)
        {
            return CopyOrMoveEntries(sourceDicPath, destinationDicPath, sourceIndices, true);
        }

        public static IniSectionOperationResult ReplaceFile(string sourceDicPath, string destinationDicPath)
        {
            if (!File.Exists(sourceDicPath))
            {
                throw new FileNotFoundException("Source dictionary was not found.", sourceDicPath);
            }
            var stats = CaptureFileStats(destinationDicPath);
            var parent = Path.GetDirectoryName(destinationDicPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }
            if (File.Exists(destinationDicPath))
            {
                var backupPath = CreateBackup(destinationDicPath);
                Notify("Backed up " + destinationDicPath + " to " + backupPath);
            }
            CopyFileAtomically(sourceDicPath, destinationDicPath);
            stats = CompleteFileStats(stats, destinationDicPath);
            var message = "Synced whole dictionary to " + destinationDicPath;
            Notify(message);
            Notify(stats.ToLogMessage());
            return new IniSectionOperationResult { Message = message, Stats = new List<IniFileChangeStat> { stats } };
        }

        public static string BuildDestinationPath(string nvdaConfigFolder, string relativeDictionaryPath)
        {
            return Path.Combine(Path.Combine(nvdaConfigFolder, "speechDicts"), relativeDictionaryPath ?? "default.dic");
        }

        public static string InferImportRelativePath(string importPath)
        {
            if (string.IsNullOrWhiteSpace(importPath))
            {
                throw new ArgumentException("No dictionary file was supplied.", "importPath");
            }
            var fileName = Path.GetFileName(importPath);
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".dic", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Choose an NVDA .dic speech dictionary file.");
            }
            if (string.Equals(fileName, "default.dic", StringComparison.OrdinalIgnoreCase))
            {
                return "default.dic";
            }

            var speechDictsRelative = RelativePathAfterFolder(importPath, "speechDicts");
            if (!string.IsNullOrWhiteSpace(speechDictsRelative))
            {
                return speechDictsRelative;
            }

            var parent = Path.GetDirectoryName(importPath);
            var grandParent = string.IsNullOrWhiteSpace(parent) ? string.Empty : Path.GetDirectoryName(parent);
            if (!string.IsNullOrWhiteSpace(parent) &&
                !string.IsNullOrWhiteSpace(grandParent) &&
                string.Equals(Path.GetFileName(grandParent), "voiceDicts.v1", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine("voiceDicts.v1", Path.GetFileName(parent), fileName);
            }

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var dash = string.IsNullOrEmpty(baseName) ? -1 : baseName.IndexOf('-');
            if (dash > 0)
            {
                var synth = baseName.Substring(0, dash).Trim();
                if (IsSafePathPart(synth))
                {
                    return Path.Combine("voiceDicts.v1", synth, fileName);
                }
            }

            return fileName;
        }

        public static IniSectionOperationResult ImportFile(string importPath, string nvdaConfigFolder)
        {
            if (!File.Exists(importPath))
            {
                throw new FileNotFoundException("Import dictionary was not found.", importPath);
            }
            var parsed = ParseFile(importPath);
            if (parsed.Entries.Count == 0)
            {
                throw new InvalidOperationException("The selected .dic file does not contain any dictionary entries.");
            }
            var relativePath = InferImportRelativePath(importPath);
            var destinationPath = BuildDestinationPath(nvdaConfigFolder, relativePath);
            var result = ReplaceFile(importPath, destinationPath);
            result.Message = "Imported speech dictionary to " + destinationPath;
            Notify(result.Message);
            return result;
        }

        private static IniSectionOperationResult CopyOrMoveEntries(string sourceDicPath, string destinationDicPath, IEnumerable<int> sourceIndices, bool move)
        {
            var source = ParseFile(sourceDicPath);
            var selected = SelectedIndexSet(sourceIndices);
            var recordsToCopy = new List<SpeechDictionaryRecord>();
            foreach (var record in source.Records)
            {
                if (record.Entry != null && selected.Contains(record.Entry.Index))
                {
                    recordsToCopy.Add(CloneRecord(record));
                }
            }
            if (recordsToCopy.Count == 0)
            {
                throw new InvalidOperationException("No selected dictionary entries were found.");
            }

            var destinationStats = CaptureFileStats(destinationDicPath);
            var sourceStats = CaptureFileStats(sourceDicPath);
            SpeechDictionaryParseResult destination;
            if (File.Exists(destinationDicPath))
            {
                destination = ParseFile(destinationDicPath);
            }
            else
            {
                destination = new SpeechDictionaryParseResult();
                destination.LineEnding = source.LineEnding;
                destination.HasTrailingNewline = true;
            }
            destination.Records.AddRange(recordsToCopy);
            RebuildEntries(destination);
            WriteParsedFile(destinationDicPath, destination);
            destinationStats = CompleteFileStats(destinationStats, destinationDicPath);

            if (move)
            {
                source.Records.RemoveAll(record => record.Entry != null && selected.Contains(record.Entry.Index));
                RebuildEntries(source);
                WriteParsedFile(sourceDicPath, source);
                sourceStats = CompleteFileStats(sourceStats, sourceDicPath);
            }

            var message = (move ? "Moved " : "Copied ") + FormatCount(recordsToCopy.Count, "dictionary entry", "dictionary entries") + " to " + destinationDicPath;
            Notify(message);
            Notify(destinationStats.ToLogMessage());
            var stats = new List<IniFileChangeStat> { destinationStats };
            if (move)
            {
                Notify(sourceStats.ToLogMessage());
                stats.Add(sourceStats);
            }
            return new IniSectionOperationResult { Message = message, Stats = stats };
        }

        private static HashSet<int> SelectedIndexSet(IEnumerable<int> indices)
        {
            var set = new HashSet<int>();
            foreach (var index in indices)
            {
                set.Add(index);
            }
            return set;
        }

        private static void RebuildEntries(SpeechDictionaryParseResult parsed)
        {
            parsed.Entries.Clear();
            var index = 0;
            foreach (var record in parsed.Records)
            {
                if (record.Entry == null)
                {
                    continue;
                }
                record.Entry.Index = index++;
                parsed.Entries.Add(record.Entry);
            }
        }

        private static SpeechDictionaryRecord CloneRecord(SpeechDictionaryRecord source)
        {
            var clone = new SpeechDictionaryRecord();
            clone.RawLines.AddRange(source.RawLines);
            if (source.Entry != null)
            {
                clone.Entry = new SpeechDictionaryEntry
                {
                    Pattern = source.Entry.Pattern,
                    Replacement = source.Entry.Replacement,
                    CaseSensitiveRaw = source.Entry.CaseSensitiveRaw,
                    TypeRaw = source.Entry.TypeRaw,
                    Comment = source.Entry.Comment,
                    SourceLine = source.Entry.SourceLine
                };
            }
            return clone;
        }

        private static void WriteParsedFile(string path, SpeechDictionaryParseResult parsed)
        {
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
            var content = BuildContent(parsed);
            var tempPath = path + ".nvdaSync.tmp";
            File.WriteAllText(tempPath, content, new UTF8Encoding(true));
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

        private static string BuildContent(SpeechDictionaryParseResult parsed)
        {
            var lines = new List<string>();
            foreach (var record in parsed.Records)
            {
                if (record.Entry == null)
                {
                    lines.AddRange(record.RawLines);
                    continue;
                }
                if (!string.IsNullOrEmpty(record.Entry.Comment))
                {
                    lines.Add("#" + record.Entry.Comment);
                }
                lines.Add(EscapeField(record.Entry.Pattern) + "\t" +
                          EscapeField(record.Entry.Replacement) + "\t" +
                          record.Entry.CaseSensitiveRaw + "\t" +
                          record.Entry.TypeRaw);
            }
            var content = string.Join(parsed.LineEnding, lines.ToArray());
            if (parsed.HasTrailingNewline && (lines.Count > 0 || content.Length > 0))
            {
                content += parsed.LineEnding;
            }
            return content;
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

        private static string CreateBackup(string path)
        {
            var folder = Path.GetDirectoryName(path);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var baseName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "dictionary";
            }
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".dic";
            }
            var backupPath = Path.Combine(folder, baseName + stamp + extension);
            var counter = 2;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(folder, baseName + stamp + "-" + counter + extension);
                counter++;
            }
            File.Copy(path, backupPath, false);
            return backupPath;
        }

        private static void CopyFileAtomically(string sourceFile, string targetFile)
        {
            var tempFile = targetFile + ".nvdaSync.tmp";
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            File.Copy(sourceFile, tempFile, true);
            if (File.Exists(targetFile))
            {
                try
                {
                    File.Replace(tempFile, targetFile, null);
                }
                catch
                {
                    File.Delete(targetFile);
                    File.Move(tempFile, targetFile);
                }
            }
            else
            {
                File.Move(tempFile, targetFile);
            }
        }

        private static void AddRawRecord(SpeechDictionaryParseResult result, IEnumerable<string> lines)
        {
            var record = new SpeechDictionaryRecord();
            record.RawLines.AddRange(lines);
            if (record.RawLines.Count > 0)
            {
                result.Records.Add(record);
            }
        }

        private static string CommentText(IEnumerable<string> commentLines)
        {
            var builder = new StringBuilder();
            foreach (var line in commentLines)
            {
                if (builder.Length > 0)
                {
                    builder.Append(" ");
                }
                builder.Append(line.Length > 0 && line[0] == '#' ? line.Substring(1) : line);
            }
            return builder.ToString();
        }

        private static string EscapeField(string value)
        {
            return (value ?? string.Empty).Replace("#", "\\#");
        }

        private static string UnescapeField(string value)
        {
            return (value ?? string.Empty).Replace("\\#", "#");
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

        private static string GetRelativePath(string root, string path)
        {
            var rootUri = new Uri(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var pathUri = new Uri(path);
            var relative = Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string RelativePathAfterFolder(string path, string folderName)
        {
            var parts = new List<string>();
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrWhiteSpace(current))
            {
                parts.Insert(0, Path.GetFileName(current));
                var parent = Path.GetDirectoryName(current);
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = parent;
            }
            for (var index = 0; index < parts.Count - 1; index++)
            {
                if (string.Equals(parts[index], folderName, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(parts.GetRange(index + 1, parts.Count - index - 1).ToArray());
                }
            }
            return string.Empty;
        }

        private static bool IsSafePathPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   value.IndexOf(Path.DirectorySeparatorChar) < 0 &&
                   value.IndexOf(Path.AltDirectorySeparatorChar) < 0;
        }

        private static string FormatCount(int count, string singular, string plural)
        {
            return count + " " + (count == 1 ? singular : plural);
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
