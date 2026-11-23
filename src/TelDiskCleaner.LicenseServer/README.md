# TelDiskCleaner licence server

Issues the 7-day trials and yearly Pro licences that the app checks online. ASP.NET Core 8 with a SQLite database.

## Secrets (never commit these)

- `keys/license-signing.pem`: the private key that signs licences. **Back it up somewhere safe.** If it is lost, every installed copy of the app needs an update. If it leaks, anyone can make fake licences.
- `Admin:ApiKey`: password for the admin page (at least 16 characters). Set it as the environment variable `Admin__ApiKey` or in `appsettings.Production.json` (git-ignored).

The matching public key is already built into the app (`LicenseConfig.PublicKey`). Only run `--generate-keys` again if you are replacing the key on purpose.

## Run locally

```
cd src/TelDiskCleaner.LicenseServer
set Admin__ApiKey=choose-a-long-admin-password
dotnet run --no-launch-profile --urls http://localhost:5080
```

The app's development build talks to `http://localhost:5080`.

## Deploy

1. Publish: `dotnet publish src/TelDiskCleaner.LicenseServer -c Release -o out/license-server`
2. Copy `out/license-server` to the server, then copy `keys/license-signing.pem` into a `keys` folder next to it.
3. Set `Admin__ApiKey`, and point `ConnectionStrings__Licenses` at a folder that is backed up (for example `Data Source=/var/lib/teldisk/licenses.db`).
4. Run it behind HTTPS (IIS, Nginx or a hosting panel) on its own address, for example `https://license.daraerp.co.zw`.
5. Build the app for that address:
   `dotnet publish src/TelDiskCleaner.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:LicenseServerUrl=https://license.daraerp.co.zw -o publish`

## Selling a licence

1. The customer pays (EcoCash, bank transfer and so on).
2. Open `https://<your server>/admin`, enter the admin key, and create a licence with their name and phone or email (12 months, 1 PC).
3. Send them the key. They enter it under **Licence** in the app.

From the same page you can renew a licence (adds 12 months), free up its PC when a customer changes computers, or revoke it.

## API used by the app

| Endpoint | Purpose |
|---|---|
| `POST /api/trial/start` | Starts or returns the PC's 7-day trial; reinstalling doesn't restart it |
| `POST /api/licenses/activate` | Activates a key on one PC |
| `POST /api/licenses/validate` | Renews the signed token (the app checks in at least every 14 days) |
| `POST /api/licenses/deactivate` | Frees the PC so the key can move |

The admin endpoints under `/api/admin` need the `X-Admin-Key` header. Requests are limited to 30 per minute per IP address.
