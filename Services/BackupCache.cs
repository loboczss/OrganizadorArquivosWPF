using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace OrganizadorArquivosWPF.Services;

public sealed class BackupCache
{
    private static readonly Mutex _fileMutex = new(false, "Global\\OneEngRenamer_BackupCache");
    private readonly object _lock = new();
    private readonly string _path;
    private readonly Dictionary<string, HashSet<string>> _map;

    public BackupCache(string path)
    {
        _path = path;
        _map = Carregar(path);
    }

    public bool Contains(string pasta, string nomeArquivo)
    {
        lock (_lock)
            return _map.TryGetValue(pasta, out var set) && set.Contains(nomeArquivo);
    }

    public void Add(string pasta, string nomeArquivo)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(pasta, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _map[pasta] = set;
            }
            set.Add(nomeArquivo);
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            bool acquired = false;
            try
            {
                try { _fileMutex.WaitOne(); acquired = true; }
                catch (AbandonedMutexException) { acquired = true; }

                for (int i = 0; ; i++)
                {
                    try
                    {
                        File.WriteAllText(
                            _path,
                            JsonSerializer.Serialize(_map,
                                new JsonSerializerOptions { WriteIndented = true }));
                        break;
                    }
                    catch (IOException) when (i < 4)
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            finally
            {
                if (acquired) _fileMutex.ReleaseMutex();
            }
        }
    }

    private static Dictionary<string, HashSet<string>> Carregar(string path)
    {
        bool acquired = false;
        try
        {
            try { _fileMutex.WaitOne(); acquired = true; }
            catch (AbandonedMutexException) { acquired = true; }

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { /* Se der ruim, começa do zero */ }
        finally
        {
            if (acquired) _fileMutex.ReleaseMutex();
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }
}
