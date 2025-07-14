// File: Services/BackupCache.cs
// Mantém um mapa “pasta → [arquivos enviados]” em disco (JSON).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OrganizadorArquivosWPF.Services;

public sealed class BackupCache
{
    private readonly string _path;
    private readonly Dictionary<string, HashSet<string>> _map;

    public BackupCache(string path)
    {
        _path = path;
        _map = Carregar(path);
    }

    public bool Contains(string pasta, string nomeArquivo) =>
        _map.TryGetValue(pasta, out var set) && set.Contains(nomeArquivo);

    public void Add(string pasta, string nomeArquivo)
    {
        if (!_map.TryGetValue(pasta, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _map[pasta] = set;
        }
        set.Add(nomeArquivo);
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(_map,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Dictionary<string, HashSet<string>> Carregar(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { /* Se der ruim, começa do zero */ }
        return new(StringComparer.OrdinalIgnoreCase);
    }
}
