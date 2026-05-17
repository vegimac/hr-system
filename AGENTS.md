# AGENTS.md

## Cursor Cloud specific instructions

### Overview
This is the **Schaub HR System** — an ASP.NET Core 8.0 monolith for HR/payroll management (McDonald's franchise restaurants in Switzerland). Single `.csproj`, single deployable, PostgreSQL backend, vanilla HTML/JS frontend in `wwwroot/index.html`.

### Required Services
| Service | How to start |
|---------|-------------|
| PostgreSQL 16 | `sudo pg_ctlcluster 16 main start` |
| ASP.NET Core app | `dotnet run` (listens on `http://localhost:5046`, Swagger UI at `/swagger` in Development) |

### Database Setup (fresh environment)
The app does **not** use EF Core migrations or `EnsureCreated()`. It relies on raw SQL `CREATE TABLE IF NOT EXISTS` and `ALTER TABLE ADD COLUMN IF NOT EXISTS` in `Program.cs` — but only for *some* tables. Core tables (`employee`, `employment`, `company_profile`, `nationality`, `permit_type`, etc.) must pre-exist. On a fresh database you must run `/workspace/Scripts/init_dev_schema.sql` before starting the app:

```bash
sudo pg_ctlcluster 16 main start
sudo -u postgres psql -c "CREATE DATABASE hr_system;" 2>/dev/null || true
sudo -u postgres psql -c "ALTER USER postgres WITH PASSWORD '201058';"
sudo -u postgres psql -d hr_system -f /workspace/Scripts/init_dev_schema.sql
```

Then start the app with `dotnet run` — it will finish schema setup and seed data.

### Authentication
- Default admin: `walter.schaub@gmail.com` / `Admin2026!` (created automatically on first startup)
- JWT-based auth; obtain token via `POST /api/auth/login`

### Build & Run
```bash
dotnet restore
dotnet build          # 0 warnings expected
dotnet run            # starts on http://localhost:5046 (Development mode)
```

### Key Gotchas
- **No EF migrations**: Schema changes are done via inline `ExecuteSqlRaw()` in `Program.cs`. New columns added to models *must* also get `ALTER TABLE ADD COLUMN IF NOT EXISTS` in `Program.cs`, or a migration SQL entry in `Scripts/init_dev_schema.sql` for fresh setups.
- **Swagger generation fails** due to a `[FromForm]` + `IFormFile` issue in `DocumentsController.Upload`. This is a pre-existing issue; Swagger UI still loads but `/swagger/v1/swagger.json` errors out.
- **Connection string** is in `appsettings.json` (`Host=localhost;Port=5432;Database=hr_system;Username=postgres;Password=201058`).
- **No test suite**: The repo has no automated tests (no xUnit/NUnit/MSTest projects).
- **QuestPDF license**: QuestPDF 2024.10.4 requires accepting the community license on first use; set env var `QuestPDF__Settings__License=Community` if needed.
- **Frontend**: Monolithic `wwwroot/index.html` (~10k lines of vanilla JS). No build step, no npm.
