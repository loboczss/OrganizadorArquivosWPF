// BackupService.cs — .NET 8.0 • Envio de Backup em pasta para SharePoint via Microsoft Graph

using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.IO.Compression;
using OrganizadorArquivosWPF.Models;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Net;
using Microsoft.Kiota.Abstractions;

namespace OrganizadorArquivosWPF.Services;

public class BackupService
{

    private const string SPDomain = "oneengenharia.sharepoint.com";
    private const string SPSitePath = "OneEngenharia";
    private const string DocumentLibraryName = "DatalogGERAL";

    private readonly GraphServiceClient _graph;
    private readonly LoggerService _log = LoggerService.Instance;
    private string? _driveId;

    public BackupService()
    {
        var scopes = new[] { "https://graph.microsoft.com/.default" };
        var credential = new ClientSecretCredential(Config.TenantId, Config.ClientId, Config.ClientSecret);
        _graph = new GraphServiceClient(credential, scopes);
    }


    private static string? ExtrairOs(string pasta)
    {
        try
        {
            var dir = Path.GetFileName(pasta);
            if (string.IsNullOrWhiteSpace(dir)) return null;

            var parts = dir.Split('_');
            if (parts.Length == 0) return null;

            var os = parts[0];

            if (os.Length > 2 && os.Substring(2).All(c => c == '0'))
            {
                var sigfi = parts.Length > 1 ? parts[1] : null;
                if (!string.IsNullOrWhiteSpace(sigfi))
                    return $"{sigfi}_instalacao";
            }

            return os;
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

    private static string CalcularSha1(string arquivo)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        using var fs = File.OpenRead(arquivo);
        var hash = sha1.ComputeHash(fs);
        return Convert.ToBase64String(hash);
    }

    private async Task<DriveItem?> UploadFileAsync(string driveId, string folderId, string file)
    {
        const int SmallFileLimit = 4 * 1024 * 1024; // 4 MB
        using var fs = File.OpenRead(file);
        string fileName = Path.GetFileName(file);

        if (fs.Length <= SmallFileLimit)
        {
            return await _graph.Drives[driveId]
                .Items[folderId]
                .ItemWithPath(fileName)
                .Content
                .PutAsync(fs);
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
        var result = await uploadTask.UploadAsync();
        return result.ItemResponse;
    }

    private async Task<DriveItem?> UploadFileWithRetryAsync(
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
                return await UploadFileAsync(driveId, folderId, file);
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

    private async Task<FileUploadResult> UploadAndVerifyAsync(
        string driveId,
        string folderId,
        string file)
    {
        var item = await UploadFileWithRetryAsync(driveId, folderId, file);
        var hashLocal = CalcularSha1(file);
        bool ok = false;
        if (item != null)
        {
            var sizeLocal = new FileInfo(file).Length;
            ok = await WaitForFileHashAsync(driveId, item.Id, hashLocal, sizeLocal);
        }
        return new FileUploadResult(Path.GetFileName(file), ok, hashLocal);
    }

    private async Task<bool> WaitForFileHashAsync(
        string driveId,
        string itemId,
        string expectedHash,
        long expectedSize,
        int attempts = 10,
        int delayMs = 3000)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                var remoto = await _graph.Drives[driveId]
                    .Items[itemId]
                    .GetAsync(r => r.QueryParameters.Select = new[] { "file", "size" });

                var hashRemoto = remoto.File?.Hashes?.Sha1Hash;
                if (!string.IsNullOrEmpty(hashRemoto))
                {
                    return string.Equals(hashRemoto, expectedHash, StringComparison.OrdinalIgnoreCase);
                }
                if (remoto.Size == expectedSize)
                {
                    _log.Info($"Arquivo '{remoto.Name}' enviado com sucesso, mas sem hash SHA1.");
                    return true; // Arquivo enviado, mas sem hash
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Falha ao verificar hash: {ex.Message}");
            }

            await Task.Delay(delayMs);
        }

        return false;
    }


    public async Task<IReadOnlyList<FileUploadResult>> EnviarBackupAsync(string pasta, string? numOs = null)
    {
        var resultados = new List<FileUploadResult>();
        if (string.IsNullOrWhiteSpace(pasta) || !Directory.Exists(pasta)) return resultados;

        numOs ??= ExtrairOs(pasta);
        if (string.IsNullOrWhiteSpace(numOs)) return resultados;

        try
        {
            var driveId = await ObterDriveIdAsync();

            // Verifica se a pasta da OS já existe (usa caminho absoluto para evitar paginação)
            DriveItem? existingFolder = null;
            try
            {
                existingFolder = await _graph.Drives[driveId].Root.ItemWithPath(numOs).GetAsync();
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                existingFolder = null;
            }

            string folderId;
            if (existingFolder != null)
            {
                folderId = existingFolder.Id;
            }
            else
            {
                // Cria pasta no SharePoint com nome da OS sem gerar duplicatas
                var pastaItem = new DriveItem
                {
                    Name = numOs,
                    Folder = new Folder(),
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "@microsoft.graph.conflictBehavior", "fail" }
                    }
                };
                var createdFolder = await _graph.Drives[driveId].Items["root"].Children.PostAsync(pastaItem);
                folderId = createdFolder!.Id;
            }

            // Lista arquivos já existentes na pasta
            var folderChildren = await _graph.Drives[driveId].Items[folderId].Children.GetAsync();
            var enviados = new HashSet<string>(folderChildren.Value
                .Where(it => it.File != null)
                .Select(it => it.Name), StringComparer.OrdinalIgnoreCase);

            // Envia arquivos em paralelo limitando concorrencia
            bool allOk = true;
            var sem = new SemaphoreSlim(4);
            var tarefas = new List<Task<FileUploadResult>>();
            foreach (var file in Directory.GetFiles(pasta))
            {
                if (enviados.Contains(Path.GetFileName(file)))
                    continue;

                await sem.WaitAsync();
                tarefas.Add(Task.Run(async () =>
                {
                    try
                    {
                        var r = await UploadAndVerifyAsync(driveId, folderId, file);
                        return r;
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Erro ao enviar '{file}': {ex.Message}");
                        allOk = false;
                        return new FileUploadResult(Path.GetFileName(file), false, string.Empty);
                    }
                    finally
                    {
                        sem.Release();
                    }
                }));
            }

            var resultArray = await Task.WhenAll(tarefas);
            resultados.AddRange(resultArray);


            // Envia zip de segurança
            string? zipTmp = null;
            try
            {
                var zipName = Path.GetFileName(pasta) + ".zip";
                if (!enviados.Contains(zipName))
                {
                    zipTmp = CriarZipTemporario(pasta);
                    var r = await UploadAndVerifyAsync(driveId, folderId, zipTmp);
                    resultados.Add(r);
                    if (!r.Verificado) allOk = false;
                }
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

            if (!allOk)
            {
                _log.Warning("Alguns arquivos falharam ao enviar. Tente novamente mais tarde.");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Falha ao enviar backup: {ex.Message}");
        }

        return resultados;
    }

    /// <summary>
    /// Sincroniza todas as pastas de datalog presentes no diretório raiz
    /// enviando-as para o SharePoint caso ainda não existam lá.
    /// </summary>
    public async Task SincronizarPastasAsync(string diretorioRaiz)
    {
        if (string.IsNullOrWhiteSpace(diretorioRaiz) || !Directory.Exists(diretorioRaiz))
            return;

        string driveId = await ObterDriveIdAsync();

        foreach (var dir in Directory.GetDirectories(diretorioRaiz))
        {
            string nome = ExtrairOs(dir) ?? Path.GetFileName(dir);
            bool exists = true;
            try
            {
                await _graph.Drives[driveId].Root.ItemWithPath(nome).GetAsync();
            }
            catch (ApiException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                exists = false;
            }
            catch (Exception ex)
            {
                _log.Error($"Erro ao verificar pasta '{nome}': {ex.Message}");
                continue;
            }

            if (!exists)
            {
                try
                {
                    await EnviarBackupAsync(dir, nome);
                }
                catch (Exception ex)
                {
                    _log.Error($"Falha ao sincronizar '{dir}': {ex.Message}");
                }
            }
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
        IProgress<double>? progress = null)
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
        {
            var nome = ExtrairOs(renamer.LastDestination);
            await EnviarBackupAsync(renamer.LastDestination, nome);
        }
    }
}

public record FileUploadResult(string Nome, bool Verificado, string Sha1Hash);
