# Repository Guidelines

## Project Structure & Module Organization

The active application lives under `src/` and targets .NET 10:

- `TaskTracker.Web/`: ASP.NET Core MVC entry point, controllers, Razor views, JavaScript, and Tailwind source assets.
- `TaskTracker.Services/`: business rules, interfaces, email delivery, SignalR hubs, and query/write services.
- `TaskTracker.Data/`: EF Core context, domain/view models, and SQL Server migrations.
- `TaskTracker.Tests/`: xUnit service tests plus in-memory database stubs and factories.

Operational manuals and email samples belong in `docs/`; one-off utilities belong in `tools/`. Treat `publish/`, `data/`, `test/`, and `task-manager-v1.5.0/` as generated, scratch, or legacy material unless a task explicitly concerns them.

## Build, Test, and Development Commands

Run commands from the repository root unless noted:

- `dotnet restore src/TaskTracker.slnx` restores NuGet dependencies.
- `dotnet build src/TaskTracker.slnx` compiles all production projects.
- `dotnet run --project src/TaskTracker.Web` starts the local web application.
- `dotnet test src/TaskTracker.Tests/TaskTracker.Tests.csproj` runs the xUnit suite (the test project is not in the solution file).
- `npm install --prefix src/TaskTracker.Web` installs Tailwind and image tooling.
- `npm run css:watch --prefix src/TaskTracker.Web` rebuilds CSS while editing views.
- `dotnet build src/TaskTracker.Web -c Release` also builds minified Tailwind CSS.

## Coding Style & Naming Conventions

Use four-space indentation in C# and conventional .NET naming: `PascalCase` for types, methods, and public members; `camelCase` for locals and parameters; `_camelCase` for private fields. Nullable reference types and implicit usings are enabled. Keep controllers thin, business behavior in services, and persistence concerns in `TaskTracker.Data`. Follow existing Razor and JavaScript style, and edit `wwwroot/css/app.src.css` rather than generated `app.css`. Run `dotnet format src/TaskTracker.slnx` before submitting broad C# changes.

## Testing Guidelines

Add tests beside the relevant service suite using `*Tests.cs`, `[Fact]`, and descriptive names such as `GetLogsAsync_FiltersByProject`. Use the helpers in `TaskTracker.Tests/Helpers` for isolated EF Core in-memory contexts. Cover permission checks, status transitions, filtering, and failure paths changed by the patch. No numeric coverage threshold is enforced; every behavior change should have a focused regression test.

## Commit & Pull Request Guidelines

History favors concise, imperative summaries, often prefixed with an issue or audit identifier (for example, `M-32: Cache lookup tables...`). Keep commits narrowly scoped. Pull requests should explain intent and verification, link relevant issues, call out migrations or configuration changes, and include screenshots for visible UI work.

## Security & Configuration

Never commit credentials. Keep real connection strings, SMTP passwords, and reCAPTCHA secrets in the ignored `src/TaskTracker.Web/appsettings.Development.json` or environment variables such as `ConnectionStrings__DefaultConnection` and `Email__Password`.
