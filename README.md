# Kira: bookkeeping and Malaysian tax for small companies and individuals

Kira reads bank statements, receipts and payment screenshots with AI, sorts every line into the right account, keeps a double-entry ledger, and tracks Malaysian tax and compliance (CP204, Form C, e-Invoice, audit exemption, personal tax reliefs).

It has two kinds of workspace:

| Workspace | What it does |
|---|---|
| **Company** | Set up the bank account (usually just one) and upload monthly statements. The AI uses each line's description to choose the account. You review and post. Lines can also be marked as petty cash or director drawings. You get P&L, balance sheet, trial balance and general ledger reports, tax estimates and compliance checks. Your accountant can get read-only access. |
| **Personal** | Optionally list the accounts you pay from (bank accounts, cards, e-wallets, cash). Upload receipts. Mark each payment as cash, or upload the payment screenshot so Kira works out which account paid. You can always change it by hand. Statements, spending reports and Form BE tax reliefs are tracked. |

---

## 1. Technology

| Layer | Stack |
|---|---|
| Frontend | **Angular 22**: standalone components, signals, zoneless change detection, `@if`/`@for` control flow, functional guards and interceptors, `HttpClient` with `fetch`, lazy-loaded routes, strict templates |
| Backend | **.NET 10 (LTS)** and **ASP.NET Core 10** Web API with controllers, built as clean architecture (Domain / Application / Infrastructure / Api) |
| Database | **Microsoft SQL Server** (2019, 2022 or Azure SQL) with **EF Core 10** migrations |
| AI | Anthropic Claude Messages API, called from the server only. The key never reaches the browser. Optional: without a key, CSV statements still import. |
| Files | Excel exports through ClosedXML. Uploads are checked by their file signature (magic bytes), not their extension. |

### Security and protocols built in

- **Authentication**
  - Short-lived **JWT access token** (15 minutes, HS256, issuer and audience validated). It is kept in memory only, never in localStorage.
  - **Rotating refresh token** in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie scoped to `/api/v1/auth`. Only a SHA-256 hash is stored in the database.
  - If an old refresh token is used again, all of that user's sessions are revoked.
  - Passwords are hashed with ASP.NET Core Identity's PBKDF2 hasher. Accounts lock after 5 failed sign-ins, and passwords must be at least 10 characters.
- **Authorization:** a deny-by-default fallback policy means every endpoint needs a signed-in user unless it is marked anonymous. Each company request is checked against the caller's membership (Owner, or read-only Accountant). Personal data is always scoped to the signed-in user.
- **Errors:** **RFC 9457 Problem Details** (`application/problem+json`), each with a `traceId`.
- **Rate limiting:** a global limit per user or IP, a strict limit on sign-in endpoints, and a token bucket on AI endpoints.
- **Security headers:** the API sends `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` and `Cache-Control: no-store`. nginx adds a strict Content-Security-Policy for the web app.
- **Hosting and API docs**
  - Forwarded-headers support for running behind nginx or a load balancer.
  - **OpenAPI 3.1** document at `/openapi/v1.json` and the **Scalar** API reference UI at `/scalar`.
  - Health checks at `/health/live` (process) and `/health/ready` (includes SQL Server).
- **API design:** all routes are versioned under `/api/v1`.
- **Ledger integrity:** journal entries are **immutable**. Corrections are posted as reversals, and an EF Core interceptor refuses any update or delete of posted entries.

---

## 2. Project structure

```
kira/
├─ backend/
│  ├─ Kira.sln
│  ├─ global.json                  # pins the .NET 10 SDK
│  ├─ Directory.Build.props        # net10.0, nullable, implicit usings for all projects
│  ├─ Directory.Packages.props     # central NuGet package versions
│  ├─ src/
│  │  ├─ Kira.Domain/              # entities, enums, ledger rules, tax calculator, payment matcher (no dependencies)
│  │  ├─ Kira.Application/         # use cases (services), DTOs, AI prompts and parsing, interfaces
│  │  ├─ Kira.Infrastructure/      # EF Core DbContext + SQL Server config, Anthropic client, Excel, hashing, clock, seeding
│  │  └─ Kira.Api/                 # controllers, auth, ProblemDetails, rate limiting, OpenAPI, Program.cs, Dockerfile
│  └─ tests/
│     └─ Kira.UnitTests/           # xUnit tests for the ledger, tax, matching and CSV parsing
├─ frontend/
│  ├─ angular.json, package.json, proxy.conf.json, Dockerfile, nginx.conf
│  └─ src/app/
│     ├─ core/api/                 # ApiService (typed HTTP, errors) and models.ts (DTO types)
│     ├─ core/auth/                # AuthService (tokens, refresh), interceptor, guards, WorkspaceService
│     ├─ core/ui/                  # icons, toasts, pipes (RM currency, dates)
│     ├─ layout/                   # app shell: sidebar, workspace switcher, Ctrl+K search, Ask Kira
│     └─ features/
│        ├─ auth/                  # sign in, register, add workspace
│        ├─ company/               # dashboard, bank statements, transactions, reports, tax, documents, access, settings, activity
│        └─ personal/              # overview, accounts, receipts, receipt detail, transactions, reliefs, reports
├─ docker-compose.yml              # SQL Server 2022 + API + web
├─ .env.example
└─ README.md
```

How the backend layers depend on each other: `Api → Application + Infrastructure`, `Infrastructure → Application`, `Application → Domain`. Business rules live in Domain and Application, and the database and HTTP sit at the edges.

---

## 3. Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | **10.0.100 or later** | `dotnet --version` |
| Node.js | **22.22.3+, 24.15+ or 26.x** (what Angular 22 supports) | `node --version` |
| npm | 10 or later | `npm --version` |
| SQL Server | 2019 or later: Developer/Express locally, Docker, or Azure SQL | — |
| EF Core CLI | 10.x | `dotnet tool install --global dotnet-ef` |
| Docker (optional) | Docker Desktop or Engine with Compose v2 | `docker compose version` |

To run SQL Server locally in Docker (on Windows, macOS or Linux):

```bash
docker run -d --name kira-sql -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=Kira_Dev_Passw0rd!" \
  -p 1433:1433 -v kira-sql:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest
```

This uses the same password as the development connection string in `backend/src/Kira.Api/appsettings.Development.json`. On Apple Silicon, turn on "Use Rosetta" in Docker Desktop, or use Azure SQL Edge.

---

## 4. Run locally (development)

### 4.1 Backend

```bash
cd backend
dotnet restore
dotnet build
```

**Set your secrets.** Keep them out of `appsettings.json`. Development already has a working JWT key and connection string, so this step is optional for a first run:

```bash
cd src/Kira.Api
dotnet user-secrets set "ConnectionStrings:Kira" "Server=localhost,1433;Database=Kira;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;Encrypt=True"
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."        # optional, turns on the AI features
cd ../..
```

On Windows with SQL Server Express and Windows sign-in, use a connection string like this instead:
`Server=localhost\SQLEXPRESS;Database=Kira;Trusted_Connection=True;TrustServerCertificate=True`

**Create the database migration** the first time (run from `backend/`):

```bash
dotnet ef migrations add Initial -p src/Kira.Infrastructure -s src/Kira.Api -o Persistence/Migrations
```

**Run the API:**

```bash
dotnet run --project src/Kira.Api --launch-profile http
```

- The API runs at **http://localhost:5080**, and the API reference is at **http://localhost:5080/scalar**.
- On start-up the database is created or migrated (`Database:AutoMigrate`). In Development, demo data is loaded (`Seed:Demo`).
- If you skip the migration step, the app still starts: it creates the schema directly and logs a warning. Add migrations before you go to production.

**Run the tests:**

```bash
dotnet test
```

### 4.2 Frontend

```bash
cd frontend
npm install
npm start            # ng serve on http://localhost:4200, proxying /api to http://localhost:5080
```

Open **http://localhost:4200**.

### 4.3 Demo sign-ins (when `Seed:Demo` is true)

| User | Password | Sees |
|---|---|---|
| `hassan@demo.kira` | `Demo12345!` | Owner of **ABC Software Sdn. Bhd.**, plus a personal workspace with accounts and receipts |
| `sarah@demo.kira` | `Demo12345!` | Tax agent with read-only access to ABC Software |

The sign-in page also has one-click demo buttons.

---

## 5. Run everything with Docker Compose

```bash
cp .env.example .env        # then edit: SQL_SA_PASSWORD, JWT_KEY, optionally ANTHROPIC_API_KEY
docker compose up -d --build
docker compose logs -f api  # watch the migration and seeding on first start
```

| Service | URL |
|---|---|
| Web app (nginx + Angular, proxies `/api`) | http://localhost:8080 |
| API (direct) | http://localhost:5080 (`/scalar` when `EXPOSE_API_DOCS=true`) |
| SQL Server | `localhost,1433`, user `sa` |

Data is kept in the `sqldata` Docker volume. `docker compose down` keeps it; `docker compose down -v` deletes it.

---

## 6. Deploy to a test server

The simplest route uses a Linux VM with Docker installed:

1. Copy the project to the server (`git clone` or `scp`).
2. `cp .env.example .env` and set:
   - a strong `SQL_SA_PASSWORD` and a random `JWT_KEY` (`openssl rand -base64 48`);
   - `SEED_DEMO=true` for a test server with sample data, or `false` for real data;
   - `SECURE_COOKIES=true` once the site is served over **HTTPS**. Browsers only send the sign-in cookie over HTTPS (or `localhost`), so leave it `false` only on a plain-HTTP internal test server.
3. Run `docker compose up -d --build`.
4. Put HTTPS in front. For example, Caddy can be added in one line (`your.domain { reverse_proxy localhost:8080 }`), or use nginx with Let's Encrypt. The API already honours `X-Forwarded-Proto`.
5. Don't publish SQL Server's port 1433 to the internet. Remove the `ports:` entry from the `db` service, or firewall it.

**Without Docker** (IIS, or a Linux service):

- **API:**
  - Publish with `dotnet publish src/Kira.Api -c Release -o ./publish` and host it on IIS (install the .NET 10 Hosting Bundle) or as a systemd service behind nginx.
  - Set these as environment variables: `ConnectionStrings__Kira`, `Jwt__Key`, `Anthropic__ApiKey`, `Auth__SecureCookies`, and `Cors__Origins__0` (only needed if the web app is on a different origin).
- **Web:**
  - Build with `npm run build`, which writes static files to `frontend/dist/kira-web/browser`.
  - Serve them from any web server, sending unknown paths to `index.html` and proxying `/api/` to the API. `frontend/nginx.conf` is a working example.
- **Azure:** App Service (Linux, .NET 10) for the API, Static Web Apps or App Service for the frontend, and Azure SQL Database. Put the connection string in App Service's *Connection strings* setting under the name `Kira`.

**Database migrations in production:** either keep `Database:AutoMigrate=true`, which is simplest for a single instance, or set it to `false` and apply migrations as a deployment step:

```bash
dotnet ef migrations script --idempotent -p src/Kira.Infrastructure -s src/Kira.Api -o migrate.sql   # review, then run with sqlcmd/SSMS
# or
dotnet ef migrations bundle -p src/Kira.Infrastructure -s src/Kira.Api -o efbundle && ./efbundle --connection "<conn>"
```

---

## 7. Configuration reference

All settings live in `backend/src/Kira.Api/appsettings.json`. You can override any of them with environment variables, using `__` for each level (for example `Jwt__Key`).

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Kira` | — | SQL Server connection string |
| `Database:AutoMigrate` | `true` | Apply migrations on start-up |
| `Seed:Demo` | `false` (`true` in Development) | Load demo users and company |
| `Jwt:Key` | — (**required**, at least 32 characters) | Signing key for access tokens |
| `Jwt:Issuer` / `Jwt:Audience` | `kira-api` / `kira-web` | Token validation values |
| `Jwt:AccessTokenMinutes` | `15` | Access token lifetime |
| `Auth:RefreshTokenDays` | `14` | How long a sign-in lasts |
| `Auth:MaxFailedLogins` / `Auth:LockoutMinutes` | `5` / `15` | Lockout after failed sign-ins |
| `Auth:MinPasswordLength` | `10` | Minimum password length |
| `Auth:SecureCookies` | `true` (`false` in Development) | Marks the refresh cookie `Secure` |
| `Cors:Origins` | `[]` | Only needed when the web app is on a different origin from the API |
| `Uploads:MaxFileMb` | `25` | Largest file accepted |
| `App:TimeZone` | `Asia/Kuala_Lumpur` | Decides what "today" means for deadlines |
| `App:HttpsRedirect` | `false` | Turn on if Kestrel itself serves HTTPS |
| `App:ExposeApiDocs` | `false` (`true` in Development) | Serve `/openapi/v1.json` and `/scalar` |
| `Database:Provider` | `SqlServer` | `SqlServer` (supported). `Postgres` is only a fallback for small test servers that can't give SQL Server its 2 GB minimum memory, such as Railway's Hobby plan. `ConnectionStrings:Kira` can then be a `postgres://` URL. |
| `Anthropic:ApiKey` | empty | Turns on the AI features |
| `Anthropic:Model` | `claude-sonnet-5-5` | Model used for reading documents and answering questions |

### Malaysian rules (`Rules` section)

Tax rates, thresholds and reliefs are **configuration, not code**, so a Budget change only means editing `appsettings.json`:

- `Rules:Tax[]`, one entry per year of assessment:
  - SME tiers (15% / 17% / 24%) and the standard rate;
  - first-year capital allowance %;
  - the entertainment deductible % (50%).
- `Rules:Compliance`:
  - e-Invoice exemption threshold (RM 3 million in this version — the threshold has changed several times, so check it);
  - audit-exemption criteria for each phase (2025–2027);
  - Form C deadline (7 months after year end);
  - the CP204 penalty margin (30%) and revision months (6th, 9th, 11th).
- `Rules:Reliefs[]`: personal relief categories and limits for each year of assessment.

When a year has no entry, Kira uses the latest earlier one. **Check these values against LHDN's current publications before relying on them.** Kira's figures are estimates for planning, and your tax agent confirms the final returns.

---

## 8. API overview

Everything is under `/api/v1`. The full, live reference is at `/scalar`, and `backend/src/Kira.Api/Kira.Api.http` has ready-made requests for VS Code or Rider.

| Area | Endpoints |
|---|---|
| Auth | `POST auth/login`, `auth/register`, `auth/refresh`, `auth/logout` · `GET me` · `POST me/password`, `me/personal`, `me/companies` |
| Company | `GET/PUT companies/{id}` · `GET/POST companies/{id}/accounts` · `POST companies/{id}/bank-accounts` |
| Bank statements | `GET companies/{id}/banking`, `.../banking/lines` · `POST .../banking/import` (multipart) · `PUT .../lines/{lineId}` · `POST .../lines/{lineId}/accept`, `/cash`, `/ignore`, `/apply-similar` · `POST .../accept-confident`, `.../recheck` · `GET/DELETE .../rules` |
| Ledger | `GET/POST companies/{id}/journal` · `GET .../journal/{entryId}` · `POST .../journal/{entryId}/reverse` · `POST .../journal/petty-cash` |
| Reports | `GET companies/{id}/reports/dashboard`, `/pl`, `/bs`, `/tb`, `/gl` · `GET .../reports/export/{pl\|bs\|tb\|gl}` (xlsx) |
| Compliance | `GET companies/{id}/compliance`, `.../compliance/package` (xlsx for your accountant) · `POST/DELETE .../reminders` |
| Documents, members, activity | `companies/{id}/documents`, `/members`, `/activity`, `/search`, `/ask` |
| Personal | `personal/accounts`, `personal/transactions` (+ `import`), `personal/receipts` (+ `manual`, `payment`, `screenshot`, `relief`, `save`), `personal/overview`, `personal/reliefs`, `personal/reports/spending`, `personal/search`, `personal/ask` |

---

## 9. How the AI parts work

- **Bank statements.** PDF, image, CSV or Excel files are sent to Claude together with your chart of accounts and its hints. Claude returns each line with a suggested account, a confidence score and a short reason, and flags cash withdrawals, foreign payees and transfers between your own accounts. Without AI, CSV files (with English or Malay headers) are imported and you categorise by hand.
- **Learning.** When you change a line's account, Kira remembers the merchant pattern. It then offers to apply the change to similar lines, and uses it for future imports.
- **Receipts.** Kira reads the merchant, date, total, SST and items, picks a category, notes how the receipt says it was paid, and suggests a tax relief category.
- **Payment screenshots.** Kira reads the bank or e-wallet, the last 4 digits, the amount and the time. It matches them to your accounts by the last 4 digits first, then by provider name. You can always override the result.
- **Matching.** A saved receipt is linked to an imported statement line on the same account with the same amount within 3 days, so spending isn't counted twice.
- **Ask Kira** answers questions using a summary of your own books only.

---

## 10. Troubleshooting

| Problem | Fix |
|---|---|
| `Jwt:Key must be at least 32 characters` on start-up | Set `Jwt:Key` with user-secrets, or set the `Jwt__Key` environment variable. |
| `A network-related or instance-specific error` / `Login failed for user 'sa'` | SQL Server isn't running, the port isn't 1433, or the password is wrong. Test with `sqlcmd -S localhost,1433 -U sa -C`. |
| `The certificate chain was issued by an authority that is not trusted` | Keep `TrustServerCertificate=True` for local or test servers, or install a proper certificate on SQL Server. |
| Signed out on every page refresh | The refresh cookie isn't being sent: on plain HTTP (other than localhost), set `SECURE_COOKIES=false`, or better, use HTTPS. Also check the web app and API share an origin (through the proxy or nginx). |
| `npm install` peer-dependency errors | Use a supported Node version (see section 3). Delete `node_modules` and `package-lock.json`, then reinstall. |
| `ng` not found | Run with `npx ng ...` or `npm start`, or install the CLI globally: `npm i -g @angular/cli@22`. |
| `dotnet ef` not found | `dotnet tool install --global dotnet-ef`, then open a new terminal. |
| Statement upload says AI is off | Set `Anthropic:ApiKey`, or upload a CSV export from your bank instead. |
| HTTP 429 | The rate limit was hit. Wait a minute. |
| Port already in use | Change `API_PORT` / `WEB_PORT` / `SQL_PORT` in `.env`, or `applicationUrl` in `launchSettings.json`. |

---

## 11. Before going live

- [ ] Real secrets in a vault (Azure Key Vault, AWS Secrets Manager, or at least environment variables). No keys in Git.
- [ ] HTTPS everywhere and `Auth:SecureCookies=true`.
- [ ] `Seed:Demo=false`, and the demo users deleted if they were ever seeded.
- [ ] A SQL user with least privilege instead of `sa`, plus automated backups (LHDN expects records to be kept for 7 years).
- [ ] Rules in `appsettings.json` checked against the latest Budget and LHDN guidance.
- [ ] A PDPA notice and privacy policy for users. Uploaded documents are stored in the database (`StoredFiles` table). For large volumes, consider moving them to Azure Blob or S3.
- [ ] Logs and alerts (Application Insights or OpenTelemetry) and uptime checks on `/health/ready`.

---

## 12. Note on this delivery

This solution was written in an environment without the .NET SDK or npm registry access, so it has **not been compiled or run here**. The code was reviewed carefully and the TypeScript was syntax-checked, but expect possibly a few small compile fixes on your first `dotnet build` or `npm start`. The compiler messages will point straight to them. If a NuGet or npm package version doesn't resolve (for example `Scalar.AspNetCore`), move it to the newest version in the same major line in `backend/Directory.Packages.props` or `frontend/package.json`.
