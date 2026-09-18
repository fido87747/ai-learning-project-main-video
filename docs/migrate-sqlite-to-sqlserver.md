# Migrating the Database Engine: SQLite → SQL Server

This guide explains how to move the MentorBooking application from its prototype
**SQLite** database to **Microsoft SQL Server** (LocalDB, a full SQL Server instance,
a SQL Server container, or Azure SQL Database).

> **Audience:** developers taking the prototype toward production.
> **Time required:** ~30–60 minutes for a clean switch (no existing data to preserve).

---

## 1. How the app uses the database today

| Concern | Current state | Where |
|---|---|---|
| EF Core provider | `Microsoft.EntityFrameworkCore.Sqlite` | `MentorBooking/MentorBooking.csproj` |
| Provider registration | `options.UseSqlite(connectionString)` | `MentorBooking/Program.cs` (~line 12–13) |
| Connection string | `Data Source=mentorbooking.db` | `MentorBooking/appsettings.json` → `ConnectionStrings:DefaultConnection` |
| DbContext | `ApplicationDbContext : IdentityDbContext<ApplicationUser>` | `MentorBooking/Data/ApplicationDbContext.cs` |
| Migrations | Provider-specific (`TEXT`/`INTEGER` types) | `MentorBooking/Data/Migrations/` |
| Schema create + seed | `await db.Database.MigrateAsync()` then seed | `MentorBooking/Data/DbSeeder.cs` (runs on startup) |

Two important facts drive the steps below:

1. **EF Core migrations are provider-specific.** The existing migrations were
   generated for SQLite and use SQLite type mappings (`TEXT`, `INTEGER`, `REAL`).
   They will **not** apply to SQL Server, so the migrations must be regenerated
   (or a separate SQL Server migration set must be maintained).
2. **The schema is created by migrations on startup.** `DbSeeder.SeedAsync` calls
   `MigrateAsync()`, so once the provider and migrations are switched, the SQL Server
   database and seed data are created automatically the first time the app runs.

---

## 2. Choose a SQL Server target

Pick whichever fits your environment and use its connection string in step 4.

| Option | Typical connection string |
|---|---|
| **SQL Server LocalDB** (Windows dev) | `Server=(localdb)\\MSSQLLocalDB;Database=MentorBooking;Trusted_Connection=True;MultipleActiveResultSets=true` |
| **Local/remote SQL Server** (SQL auth) | `Server=localhost;Database=MentorBooking;User Id=sa;Password=Your_Strong_Pass1;TrustServerCertificate=True` |
| **SQL Server in Docker** | `Server=localhost,1433;Database=MentorBooking;User Id=sa;Password=Your_Strong_Pass1;TrustServerCertificate=True` |
| **Azure SQL Database** | `Server=tcp:<srv>.database.windows.net,1433;Database=MentorBooking;Authentication=Active Directory Default;Encrypt=True` |

To run SQL Server quickly in Docker:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Your_Strong_Pass1" \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

---

## 3. Swap the EF Core provider package

From the repository root:

```powershell
# Remove the SQLite provider
dotnet remove MentorBooking\MentorBooking.csproj package Microsoft.EntityFrameworkCore.Sqlite

# Add the SQL Server provider (match the installed EF Core version, currently 8.0.11)
dotnet add MentorBooking\MentorBooking.csproj package Microsoft.EntityFrameworkCore.SqlServer --version 8.0.11
```

> `nuget.config` restricts restore to nuget.org, where both packages are published — no
> extra feed is needed. Keep the version aligned with the other `Microsoft.EntityFrameworkCore.*`
> packages already referenced (`Microsoft.EntityFrameworkCore.Design`, `.Tools`).

After this, `MentorBooking.csproj` should reference
`Microsoft.EntityFrameworkCore.SqlServer` instead of `...Sqlite`.

---

## 4. Update the provider registration and connection string

**`MentorBooking/Program.cs`** — change the provider call:

```csharp
// Before
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// After
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
```

Also update the fallback default on line ~10 if you rely on it:

```csharp
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=MentorBooking;Trusted_Connection=True";
```

**`MentorBooking/appsettings.json`** — replace the SQLite connection string:

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=MentorBooking;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

> **Do not commit real credentials.** For anything with a password, use user-secrets or
> environment variables instead of `appsettings.json`:
> ```powershell
> cd MentorBooking
> dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Password=..."
> ```
> (The project already has a `UserSecretsId` of `mentorbooking-prototype`.)

---

## 5. Regenerate the migrations for SQL Server

Because the existing migrations are SQLite-specific, regenerate them. For a prototype
with **no data to keep**, the simplest, cleanest path is to replace them:

```powershell
cd MentorBooking

# 1. Delete the SQLite-specific migrations (keep the folder)
Remove-Item Data\Migrations\*.cs

# 2. Create a fresh initial migration for SQL Server.
#    -o keeps migrations in the existing Data/Migrations folder.
dotnet ef migrations add InitialCreate -o Data/Migrations
```

Requirements for the `dotnet ef` commands:

```powershell
# Install the EF Core CLI once (if not already present)
dotnet tool install --global dotnet-ef --version 8.*
```

The generated migration should now use SQL Server types (`nvarchar`, `datetime2`,
`int`, `bit`) instead of `TEXT`/`INTEGER`.

### Apply the schema

Either just **run the app** (recommended — `DbSeeder` calls `MigrateAsync()` on
startup and also seeds roles, the admin, mentors, topics, and the demo student):

```powershell
dotnet run --project MentorBooking
```

…or apply migrations explicitly without starting the app:

```powershell
cd MentorBooking
dotnet ef database update
```

---

## 6. Verify

1. **Build & test:**
   ```powershell
   dotnet build
   dotnet test
   ```
2. **Run the app** and confirm the SQL Server database `MentorBooking` is created
   (check with SSMS / Azure Data Studio / `sqlcmd`).
3. **Sign in** with a seeded demo account (e.g. `admin@mentorbooking.local` / `Admin#12345`)
   to confirm Identity tables and seed data are present.
4. **Book a mentor** end-to-end to confirm reads/writes work.

---

## Provider differences to be aware of

The app's model is standard EF Core and maps cleanly, but note:

- **Types.** SQLite is dynamically typed; SQL Server is strict. Migrations now emit
  `nvarchar(max)` for unbounded strings and `datetime2` for `DateTime`. String columns
  that declare `[StringLength]`/`maxLength` keep those limits.
- **Dates are UTC.** The app stores and compares everything in UTC (`DateTime` with
  `DateTimeKind.Utc`). SQL Server's `datetime2` has no timezone; keep treating values as
  UTC in code (unchanged). Avoid `DateTime.Now`; the app already uses `DateTime.UtcNow`.
- **Unique index on `Topic.Name`** and the composite index on `Booking (MentorId, StartUtc)`
  (see `ApplicationDbContext.OnModelCreating`) translate directly.
- **Cascade / restrict deletes** configured on `Booking` are honored by SQL Server.
- **Auto-increment.** SQLite `AUTOINCREMENT` becomes SQL Server `IDENTITY` — no code change.
- **Case sensitivity.** SQLite `LIKE`/comparisons are case-insensitive by default; SQL Server
  depends on the database **collation** (default `SQL_Latin1_General_CP1_CI_AS` is
  case-insensitive, matching current behavior). Choose a `CI` collation to preserve behavior.
- **Concurrency / MARS.** For SQL Server, `MultipleActiveResultSets=true` avoids issues when
  multiple readers are active on one connection.

---

## Migrating existing data (optional)

The prototype seeds its data on startup, so most environments need no data transfer.
If you must move existing rows out of `mentorbooking.db`:

- **Small / one-off:** open `MentorBooking/mentorbooking.db` in a tool such as
  *DB Browser for SQLite* or Azure Data Studio, export tables to SQL/CSV, and import
  into SQL Server (SSMS Import Wizard or `BULK INSERT`). Load parent tables first
  (`AspNetUsers`, `AspNetRoles`, `Topics`, `Mentors`) before dependent tables
  (`MentorTopics`, `AvailabilityWindows`, `Bookings`, `AspNetUserRoles`).
- **Preserve identity values:** use `SET IDENTITY_INSERT <table> ON` around inserts so
  foreign keys stay valid.
- **Password hashes** in `AspNetUsers.PasswordHash` are portable — copy them as-is so
  existing logins keep working. (Note: `DbSeeder` re-syncs *seeded demo* account passwords
  to their displayed hint on startup by design.)

---

## Keeping both providers (advanced, optional)

If you want SQLite for local dev and SQL Server elsewhere, keep **two** migration sets and
select the provider by configuration. Store migrations in provider-specific folders and
assemblies, e.g.:

```csharp
var provider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (provider == "SqlServer")
        options.UseSqlServer(connectionString,
            b => b.MigrationsAssembly("MentorBooking.Migrations.SqlServer"));
    else
        options.UseSqlite(connectionString,
            b => b.MigrationsAssembly("MentorBooking.Migrations.Sqlite"));
});
```

Then generate each set against its provider:

```powershell
dotnet ef migrations add InitialCreate --project MentorBooking.Migrations.SqlServer -- --DatabaseProvider SqlServer
dotnet ef migrations add InitialCreate --project MentorBooking.Migrations.Sqlite   -- --DatabaseProvider Sqlite
```

This is more setup than most prototypes need; prefer the single-provider switch in
steps 3–5 unless you specifically require both.

---

## Rollback

To return to SQLite:

1. `dotnet remove ... package Microsoft.EntityFrameworkCore.SqlServer`
2. `dotnet add ... package Microsoft.EntityFrameworkCore.Sqlite`
3. Restore `UseSqlite(...)` in `Program.cs` and the `Data Source=mentorbooking.db`
   connection string in `appsettings.json`.
4. Regenerate the SQLite migration (`Remove-Item Data\Migrations\*.cs` then
   `dotnet ef migrations add InitialCreate -o Data/Migrations`).

---

## Quick checklist

- [ ] SQL Server instance reachable; connection string ready
- [ ] `Microsoft.EntityFrameworkCore.Sqlite` removed, `...SqlServer` added (matching version)
- [ ] `Program.cs` uses `UseSqlServer(...)`
- [ ] `ConnectionStrings:DefaultConnection` updated (secrets for passwords)
- [ ] Old SQLite migrations replaced with a fresh SQL Server migration
- [ ] App runs; database + seed data created (`MigrateAsync` on startup)
- [ ] `dotnet build` and `dotnet test` pass
- [ ] Demo login + a booking verified against SQL Server
