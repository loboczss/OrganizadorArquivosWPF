// BackupService.cs — .NET 8.0 • Envio de Backup em pasta para SharePoint via Microsoft Graph

using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using OrganizadorArquivosWPF.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace OrganizadorArquivosWPF.Services;

public class BackupService
{
    private const string TenantId = "3b08e64e-b3be-402b-bb26-1fa4f91cf61f";
    private const string ClientId = "3cffac6a-f9d9-42d1-9065-4054fcd40163";
    private const string ClientSecret = "JFd8Q~hHgTYYo0P0EjAM8mpe3xm3.5vTfCHRFc.T";

    private const string SPDomain = "oneengenharia.sharepoint.com";
    private const string SPSitePath = "OneEngenharia";
    private const string DocumentLibraryName = "DatalogGERAL";

    private static readonly string BackupHistoryFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OneEngRenamer",
        "uploaded_backups.txt");

    private readonly GraphServiceClient _graph;
    private readonly LoggerService _log = LoggerService.Instance;
    private string? _driveId;

    public BackupService()
    {
        var scopes = new[] { "https://graph.microsoft.com/.default" };
        var credential = new ClientSecretCredential(TenantId, ClientId, ClientSecret);
        _graph = new GraphServiceClient(credential, scopes);
    }

    private static HashSet<string> CarregarHistorico(string path)
    {
        try
        {
            if (File.Exists(path))
                return new HashSet<string>(File.ReadAllLines(path), StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static void SalvarHistorico(string path, IEnumerable<string> itens)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, itens);
        }
        catch { }
    }

    private static string? ExtrairOs(string pasta)
    {
        try
        {
            var dir = Path.GetFileName(pasta);
            return dir?.Split('_')[0];
        }
        catch { return null; }
    }

    private async Task<string> ObterDriveIdAsync()
    {
        if (!string.IsNullOrEmpty(_driveId)) return _driveId!;

        var site = await _graph.Sites[$"{SPDomain}:/sites/{SPSitePath}"].GetAsync();
        var drives = await _graph.Sites[site.Id].Drives.GetAsync();
        var drive = drives.Value.FirstOrDefault(d => d.Name == DocumentLibraryName)
            ?? throw new($"Biblioteca '{DocumentLibraryName}' não encontrada.");

        _driveId = drive.Id;
        return _driveId;
    }

    public async Task EnviarBackupAsync(string pasta, string? numOs = null)
    {
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return;

        numOs ??= ExtrairOs(pasta);
        if (string.IsNullOrWhiteSpace(numOs)) return;

        var enviados = CarregarHistorico(BackupHistoryFile);
        if (enviados.Contains(numOs))
        {
            _log.Info($"Backup já enviado para a O.S {numOs}. Pulando envio.");
            return;
        }

        try
        {
            var driveId = await ObterDriveIdAsync();

            // Cria pasta no SharePoint com nome da OS
            var pastaItem = new DriveItem
            {
                Name = numOs,
                Folder = new Folder(),
                AdditionalData = new Dictionary<string, object>
                {
                    { "@microsoft.graph.conflictBehavior", "rename" }
                }
            };

            var createdFolder = await _graph.Drives[driveId].Items["root"].Children.PostAsync(pastaItem);
            string folderId = createdFolder!.Id;

            // Envia arquivos
            foreach (var file in Directory.GetFiles(pasta))
            {
                try
                {
                    using var fs = File.OpenRead(file);
                    string fileName = Path.GetFileName(file);

                    await _graph.Drives[driveId]
                        .Items[folderId]
                        .ItemWithPath(fileName)
                        .Content
                        .PutAsync(fs);
                }
                catch (Exception ex)
                {
                    _log.Warning($"Erro ao enviar '{file}': {ex.Message}");
                }
            }

            enviados.Add(numOs);
            SalvarHistorico(BackupHistoryFile, enviados);
        }
        catch (Exception ex)
        {
            _log.Error($"Falha ao enviar backup: {ex.Message}");
        }
    }

    public async Task ProcessarBackupAsync(
        string pastaOrigem,
        ClientRecord registro,
        string sistema,
        string tipoSistema,
        string destinoLocal,
        bool enviarParaNuvem,
        string nomeFuncionario,
        string matriculaFuncionario,
        IProgress<int>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(pastaOrigem) || !Directory.Exists(pastaOrigem))
            throw new DirectoryNotFoundException("Pasta de origem inválida para backup.");

        var logFileService = new LogFileService();
        var renamer = new RenamerService(_log, logFileService);

        await renamer.RenameAsync(
            pastaOrigem,
            registro,
            sistema,
            tipoSistema,
            true,
            destinoLocal,
            nomeFuncionario,
            matriculaFuncionario,
            progress);

        if (enviarParaNuvem)
            await EnviarBackupAsync(renamer.LastDestination, registro?.NumOS);
    }
}