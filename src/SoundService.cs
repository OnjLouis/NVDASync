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

        private readonly string bundledSoundDirectory;
        private readonly string userSoundDirectory;
        private readonly object sync = new object();
        private SoundPlayer currentPlayer;
        private Stream currentStream;

        public SoundService(string appDirectory, string settingsDirectory)
        {
            bundledSoundDirectory = Path.Combine(appDirectory, "sounds");
            userSoundDirectory = Path.Combine(settingsDirectory, "sounds");
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

            var path = PreferredSoundPath(fileName);
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var data = File.ReadAllBytes(path);
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

        private string PreferredSoundPath(string fileName)
        {
            var userPath = Path.Combine(userSoundDirectory, fileName);
            return File.Exists(userPath) ? userPath : Path.Combine(bundledSoundDirectory, fileName);
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
