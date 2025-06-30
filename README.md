# OrganizadorArquivosWPF
This application interacts with Microsoft Graph and requires Azure AD credentials.

## Configuration

Credentials are read from the environment or an optional `config.json` file placed next to the executable. Set the following environment variables (or provide the same keys in `config.json`):

- `TENANT_ID` – Azure Active Directory tenant ID
- `CLIENT_ID` – Application (client) ID
- `CLIENT_SECRET` – Application secret

Example `config.json`:

```json
{
  "TENANT_ID": "00000000-0000-0000-0000-000000000000",
  "CLIENT_ID": "00000000-0000-0000-0000-000000000000",
  "CLIENT_SECRET": "your-secret"
}
```

Environment variables take precedence over values from the configuration file.
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

## License

This project is licensed under the [MIT License](LICENSE).
