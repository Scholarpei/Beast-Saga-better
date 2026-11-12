using System.Security.Cryptography;

namespace BeastSagaBetter.Core;

internal static class DataRootLock
{
    private const string MutexPrefix = @"Local\BeastSagaBetter.DataRoot.";

    internal static IDisposable Acquire(string dataRoot)
    {
        var normalizedRoot = Path.GetFullPath(dataRoot);
        var pathBytes = System.Text.Encoding.UTF8.GetBytes(normalizedRoot.ToUpperInvariant());
        var mutexName = MutexPrefix + Convert.ToHexString(SHA256.HashData(pathBytes));
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
                throw new IOException("Timed out waiting for the data root lock");
            return new Scope(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class Scope : IDisposable
    {
        private Mutex? mutex;

        public Scope(Mutex mutex) => this.mutex = mutex;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref mutex, null);
            if (current is null) return;
            try { current.ReleaseMutex(); }
            finally { current.Dispose(); }
        }
    }
}
