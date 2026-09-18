# Mentor Booking (Prototype)

A web application where students book 1-hour mentoring sessions with mentors/teachers by
topic (Math, Physics, Chemistry, Biology, Cooking, ...). Mentors confirm or reject requests;
confirmed sessions get a free **Jitsi Meet** video link, and students are notified by email.

This is an initial prototype designed to grow.

## Tech stack

| Concern            | Choice                                                     |
|--------------------|------------------------------------------------------------|
| Framework          | ASP.NET Core 8, **Blazor Server** (single project)         |
| Data access        | Entity Framework Core (code-first + migrations)            |
| Database           | SQL Server (LocalDB by default; swappable to Azure SQL/PostgreSQL) |
| Auth               | ASP.NET Core Identity — roles **Mentor** and **Admin**     |
| Email              | MailKit SMTP (behind `IEmailSender`)                       |
| Video conferencing | **Jitsi Meet** (free, open-source, no API keys)            |
| Tests              | xUnit + EF Core InMemory                                   |

**Students are anonymous** — they book with email + name + message and do not log in.
Mentors and admins sign in.

## User roles

- **Student (anonymous):** pick a topic → system auto-assigns the most-available mentor (student
  can also pick another) → choose a free 1-hour slot on the daily/weekly calendar → submit
  name, email, and a message. Learns the outcome by email.
- **Mentor:** manage weekly availability, **edit their own skills (topics)**, view their
  schedule, confirm requests (generates a Jitsi link + emails the student) or reject with a
  required reason (emails the student).
- **Admin:** view all mentor schedules; reschedule or remove any booking; **create mentors
  (with a login account) and assign their skills**; **manage the topic catalog**.

## Booking lifecycle

`Pending` (holds the slot) → `Confirmed` (Jitsi link + confirmation email)
or → `Rejected` (reason required + rejection email). Admins can also `Cancel`/remove.

## Getting started

Prerequisites: .NET 8 SDK and SQL Server (the default connection string targets a
`(localdb)\MentorBookingDb` LocalDB instance; create it once with
`sqllocaldb create MentorBookingDb`, or point `ConnectionStrings:DefaultConnection`
at any SQL Server instance).

```powershell
# from the repository root
dotnet run --project MentorBooking
```

On first run the SQL Server database is created, migrations are applied, and demo data is seeded.
Browse to the URL shown in the console (e.g. https://localhost:5001).

> **Provider details / rollback to SQLite?** See [docs/migrate-sqlite-to-sqlserver.md](docs/migrate-sqlite-to-sqlserver.md)
> for connection-string options, provider differences, and how to switch providers.

### Demo accounts

| Role   | Email                       | Password       |
|--------|-----------------------------|----------------|
| Admin  | admin@mentorbooking.local   | Admin#12345    |
| Mentor | alice@mentorbooking.local   | Mentor#12345   |
| Mentor | bob@mentorbooking.local     | Mentor#12345   |

## Email (SMTP) configuration

Email uses real SMTP via MailKit. If no SMTP host is configured, messages are **logged**
instead of sent, so the prototype runs without credentials.

Configure via user-secrets (recommended, never committed):

```powershell
cd MentorBooking
dotnet user-secrets set "Smtp:Host" "smtp.gmail.com"
dotnet user-secrets set "Smtp:Port" "587"
dotnet user-secrets set "Smtp:Username" "you@gmail.com"
dotnet user-secrets set "Smtp:Password" "your-app-password"
dotnet user-secrets set "Smtp:FromEmail" "you@gmail.com"
```

For Gmail, use an App Password (not your account password).

## Video conferencing (Jitsi Meet)

Confirmed bookings get a unique room at `https://meet.jit.si/MentorBooking-<id>-<guid>`.
No account or API keys are required. To self-host, change `Meeting:JitsiBaseUrl` in
`appsettings.json` to your own Jitsi server URL.

## Tests

```powershell
dotnet test
```

Covers slot generation, double-booking prevention, topic/mentor matching, and confirm/reject.

## Notes & assumptions

- **Times are UTC.** Availability windows and calendar slots are treated as UTC in this
  prototype; a per-user timezone can be layered on later.
- Availability windows should use whole hours (1-hour slots).
- Pending requests hold the slot until the mentor acts (no auto-timeout in this prototype).

## Project layout

```
MentorBooking/            Blazor Server app
  Data/                   DbContext, migrations, seeder
  Models/                 Domain entities (Mentor, Topic, Booking, ...)
  Services/               Booking, Availability, Email, Jitsi, auth state
  Pages/                  Blazor pages (Book, Mentor/*, Admin/*) + Account login
  Shared/                 Layout, NavMenu, reusable CalendarView
MentorBooking.Tests/      xUnit tests
docs/                     Reference docs (e.g. SQLite → SQL Server migration)
```
