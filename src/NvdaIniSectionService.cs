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
            try
            {
                var parsed = ParseFile(iniPath);
                var index = FindSectionIndex(parsed.Sections, sectionName);
                if (index < 0)
                {
                    throw new InvalidOperationException("Section [" + sectionName + "] was not found.");
                }
                parsed.Sections.RemoveAt(index);
                WriteParsedFile(iniPath, parsed);
                var message = "Deleted [" + sectionName + "] from " + iniPath;
                Notify(message);
                return new IniSectionOperationResult { Message = message };
            }
            catch (IOException ex)
            {
                throw new IOException("Could not delete [" + sectionName + "] from nvda.ini: " + ex.Message, ex);
            }
        }

        public static IniSectionOperationResult MoveSection(string sourceIniPath, string destinationIniPath, string sectionName, bool overwriteIfExists)
        {
            try
            {
                var source = ParseFile(sourceIniPath);
                var sourceIndex = FindSectionIndex(source.Sections, sectionName);
                if (sourceIndex < 0)
                {
                    throw new InvalidOperationException("Section [" + sectionName + "] was not found in the source nvda.ini.");
                }

                var sectionToMove = CloneSection(source.Sections[sourceIndex]);
                var destinationCreated = false;
                var overwritten = false;

                if (File.Exists(destinationIniPath))
                {
                    var destination = ParseFile(destinationIniPath);
                    var destinationIndex = FindSectionIndex(destination.Sections, sectionName);
                    if (destinationIndex >= 0)
                    {
                        if (!overwriteIfExists)
                        {
                            throw new InvalidOperationException("The destination already contains [" + sectionName + "].");
                        }
                        destination.Sections[destinationIndex] = sectionToMove;
                        overwritten = true;
                    }
                    else
                    {
                        destination.Sections.Add(sectionToMove);
                    }
                    WriteParsedFile(destinationIniPath, destination);
                }
                else
                {
                    var destination = new NvdaIniParseResult();
                    destination.LineEnding = source.LineEnding;
                    destination.HasTrailingNewline = true;
                    destination.Sections.Add(sectionToMove);
                    var parent = Path.GetDirectoryName(destinationIniPath);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }
                    WriteParsedFile(destinationIniPath, destination);
                    destinationCreated = true;
                }

                source.Sections.RemoveAt(sourceIndex);
                try
                {
                    WriteParsedFile(sourceIniPath, source);
                }
                catch (Exception ex)
                {
                    Notify("Moved [" + sectionName + "] to the destination, but could not remove it from the source: " + ex.Message);
                    throw;
                }

                var message = "Moved [" + sectionName + "] to " + destinationIniPath;
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
                return new IniSectionOperationResult
                {
                    DestinationCreated = destinationCreated,
                    DestinationSectionOverwritten = overwritten,
                    Message = message
                };
            }
            catch (IOException ex)
            {
                throw new IOException("Could not move [" + sectionName + "] between nvda.ini files: " + ex.Message, ex);
            }
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
