# OrganizadorArquivosWPF
This application interacts with Microsoft Graph and requires Azure AD credentials.

## Configuration

Credentials are read from a JSON file located at
`%LOCALAPPDATA%\OneEngRenamer\config.json`. Create this file with the
following content:

```json
{
  "TenantId": "<your-tenant-id>",
  "ClientId": "<your-client-id>",
  "ClientSecret": "<your-client-secret>",
  "BackupFolder": "C:\\Path\\To\\Backup"
}
```

OrganizadorArquivosWPF is a Windows Presentation Foundation (WPF) desktop application used by One Engenharia LTDA to organize engineering files. It automates tasks such as renaming, moving and backing up documents to cloud storage.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- Windows with Visual Studio 2022 (or later) with the **Desktop development with C#** workload

## Setup

1. Clone this repository:
   ```bash
   git clone <repo-url>
   ```
2. Restore dependencies:
   ```bash
   dotnet restore
   ```
3. Build the application:
   ```bash
   dotnet build
   ```

## Running

To run from the command line use:

```bash
dotnet run
```

Alternatively open the `OrganizadorArquivosWPF.csproj` file in Visual Studio and run the project.

When pressing **Sincronizar Tudo** in the application, all local folders created by the renamer (AC/MT or in *Documentos*) that contain a service order number are scanned and automatically uploaded to the **DatalogGERAL** library on SharePoint if they are not already present.

### Background Sync Service

The `SyncWorker` project builds a Windows service that synchronizes data every 10 minutes.
Install the service using `sc create` or similar tools and ensure the `BackupFolder` setting is configured.

### Reliable Backup

Backup uploads now retry automatically using an exponential backoff strategy and
verify file integrity on SharePoint for up to several minutes.

## License

This project is licensed under the [MIT License](LICENSE).
