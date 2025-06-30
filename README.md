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
