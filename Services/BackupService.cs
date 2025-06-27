// BackupService.cs — .NET 8.0 • Envio de Backup em pasta para SharePoint via Microsoft Graph

using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.IO.Compression;
using OrganizadorArquivosWPF.Models;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
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
    private readonly HashSet<string> _skipLogged = new(StringComparer.OrdinalIgnoreCase);
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

    private static string CriarZipTemporario(string pasta)
    {
        var nome = Path.GetFileName(pasta);
        var tmp = Path.Combine(Path.GetTempPath(), nome + ".zip");

        if (File.Exists(tmp))
            File.Delete(tmp);

        ZipFile.CreateFromDirectory(pasta, tmp, CompressionLevel.SmallestSize, false);
        return tmp;
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

    private async Task UploadFileAsync(string driveId, string folderId, string file)
    {
        const int SmallFileLimit = 4 * 1024 * 1024; // 4 MB
        using var fs = File.OpenRead(file);
        string fileName = Path.GetFileName(file);

        if (fs.Length <= SmallFileLimit)
        {
            await _graph.Drives[driveId]
                .Items[folderId]
                .ItemWithPath(fileName)
                .Content
                .PutAsync(fs);
            return;
        }

        // 👇 Definindo o corpo da requisição de upload session
        var uploadBody = new CreateUploadSessionPostRequestBody
        {
            Item = new DriveItemUploadableProperties
            {
                Name = fileName,
                AdditionalData = new Dictionary<string, object>
            {
                { "@microsoft.graph.conflictBehavior", "rename" }
            }
            }
        };

        // 👇 Corrigido: PostAsync agora exige esse body como argumento obrigatório
        var uploadSession = await _graph.Drives[driveId]
            .Items[folderId]
            .ItemWithPath(fileName)
            .CreateUploadSession
            .PostAsync(uploadBody);

        var uploadTask = new LargeFileUploadTask<DriveItem>(uploadSession, fs);
        await uploadTask.UploadAsync();
    }

    private async Task UploadFileWithRetryAsync(
        string driveId,
        string folderId,
        string file,
        int maxAttempts = 3)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                await UploadFileAsync(driveId, folderId, file);
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxAttempts)
                    throw new IOException(
                        $"Falha ao enviar '{file}' após {attempt} tentativas.", ex);

                _log.Warning(
                    $"Erro ao enviar '{file}' (tentativa {attempt}): {ex.Message}. Retentando...");
                await Task.Delay(1000);
            }
        }
    }


    public async Task EnviarBackupAsync(string pasta, string? numOs = null)
    {
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return;

        numOs ??= ExtrairOs(pasta);
        if (string.IsNullOrWhiteSpace(numOs)) return;

        var enviados = CarregarHistorico(BackupHistoryFile);
        if (enviados.Contains(numOs))
        {
            if (_skipLogged.Add(numOs))
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
            bool allOk = true;
            foreach (var file in Directory.GetFiles(pasta))
            {
                try
                {
                    await UploadFileWithRetryAsync(driveId, folderId, file);
                }
                catch (Exception ex)
                {
                    allOk = false;
                    _log.Error($"Erro ao enviar '{file}': {ex.Message}");
                }
            }

            // Envia zip de segurança
            string? zipTmp = null;
            try
            {
                zipTmp = CriarZipTemporario(pasta);
                await UploadFileWithRetryAsync(driveId, folderId, zipTmp);
            }
            catch (Exception ex)
            {
                allOk = false;
                _log.Error($"Erro ao enviar '{zipTmp ?? "zip"}': {ex.Message}");
            }
            finally
            {
                if (zipTmp != null && File.Exists(zipTmp))
                    File.Delete(zipTmp);
            }

            if (allOk)
            {
                enviados.Add(numOs);
                SalvarHistorico(BackupHistoryFile, enviados);
            }
            else
            {
                _log.Warning("Alguns arquivos falharam ao enviar. Tente novamente mais tarde.");
            }
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