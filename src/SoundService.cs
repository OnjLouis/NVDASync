using System;
using System.IO;
using System.Media;

namespace NvdaAddonSync
{
    internal sealed class SoundService : IDisposable
    {
        internal const string SyncStartedFileName = "sync-start.wav";
        internal const string SyncCompletedFileName = "sync-complete.wav";
        internal const string SecondaryUnavailableFileName = "secondary-unavailable.wav";
        private const string EmbeddedSoundResourcePrefix = "NvdaAddonSync.Sounds.";
        private static readonly string[] FactorySoundFileNames =
        {
            SyncStartedFileName,
            SyncCompletedFileName,
            SecondaryUnavailableFileName
        };

        private readonly string bundledSoundDirectory;
        private readonly string userSoundDirectory;
        private readonly object sync = new object();
        private SoundPlayer currentPlayer;
        private Stream currentStream;

        public SoundService(string appDirectory, string settingsDirectory)
        {
            bundledSoundDirectory = Path.Combine(appDirectory, "sounds");
            userSoundDirectory = Path.Combine(settingsDirectory, "sounds");
            EnsureBundledSounds(appDirectory);
        }

        public void SyncStarted(bool enabled)
        {
            Play(SyncStartedFileName, enabled);
        }

        public void SyncCompleted(bool enabled)
        {
            Play(SyncCompletedFileName, enabled);
        }

        public void SecondaryUnavailable(bool enabled)
        {
            Play(SecondaryUnavailableFileName, enabled);
        }

        private void Play(string fileName, bool enabled)
        {
            if (!enabled)
            {
                return;
            }

            try
            {
                var data = LoadSoundData(fileName);
                if (data == null)
                {
                    return;
                }

                lock (sync)
                {
                    DisposePlayer();
                    currentStream = new MemoryStream(data);
                    currentPlayer = new SoundPlayer(currentStream);
                    currentPlayer.Play();
                }
            }
            catch
            {
                // Sound feedback must never prevent or interrupt synchronization.
            }
        }

        private byte[] LoadSoundData(string fileName)
        {
            var userPath = Path.Combine(userSoundDirectory, fileName);
            if (File.Exists(userPath))
            {
                return File.ReadAllBytes(userPath);
            }

            var bundledPath = Path.Combine(bundledSoundDirectory, fileName);
            if (File.Exists(bundledPath))
            {
                return File.ReadAllBytes(bundledPath);
            }

            return ReadEmbeddedSound(fileName);
        }

        internal static void EnsureBundledSounds(string appDirectory)
        {
            var directory = Path.Combine(appDirectory, "sounds");
            foreach (var fileName in FactorySoundFileNames)
            {
                try
                {
                    var data = ReadEmbeddedSound(fileName);
                    if (data == null)
                    {
                        continue;
                    }

                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, fileName);
                    if (!File.Exists(path) || !FileMatches(path, data))
                    {
                        File.WriteAllBytes(path, data);
                    }
                }
                catch
                {
                    // Embedded fallback still allows sounds in read-only app folders.
                }
            }
        }

        private static byte[] ReadEmbeddedSound(string fileName)
        {
            using (var resource = typeof(SoundService).Assembly.GetManifestResourceStream(EmbeddedSoundResourcePrefix + fileName))
            {
                if (resource == null)
                {
                    return null;
                }

                using (var buffer = new MemoryStream())
                {
                    resource.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

        private static bool FileMatches(string path, byte[] expected)
        {
            var actual = File.ReadAllBytes(path);
            if (actual.Length != expected.Length)
            {
                return false;
            }
            for (var index = 0; index < actual.Length; index++)
            {
                if (actual[index] != expected[index])
                {
                    return false;
                }
            }
            return true;
        }

        private void DisposePlayer()
        {
            if (currentPlayer != null)
            {
                try { currentPlayer.Stop(); } catch { }
                try { currentPlayer.Dispose(); } catch { }
                currentPlayer = null;
            }
            if (currentStream != null)
            {
                try { currentStream.Dispose(); } catch { }
                currentStream = null;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                DisposePlayer();
            }
        }
    }
}
