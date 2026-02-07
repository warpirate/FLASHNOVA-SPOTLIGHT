## FlashSpot

**FlashSpot** is a lightweight Windows search utility built on .NET 8.0.  
It indexes your filesystem and exposes a fast search experience through a desktop UI.

### Project structure

- **`FlashSpot.sln`**: Solution file.
- **`src/FlashSpot.App`**: Windows desktop front-end (XAML + C#).
- **`src/FlashSpot.Core`**: Core search and settings logic:
  - `Services/` for file search, indexing status, and settings providers.
  - `Models/` for small immutable data models (e.g. `IndexStatusSnapshot`).
  - `Abstractions/` for interfaces that define the core contracts.
- **`ui updates/`**: Design documents and experimental UI/XAML variants.
- **`FLASHSPOT ARCHITECTURE.MD`**: High‑level architecture notes for the project.

### Getting started

1. **Requirements**
   - .NET 8.0 SDK
   - Windows 10+ (the app targets `net8.0-windows`)

2. **Build and run**

   ```bash
   dotnet restore
   dotnet build
   dotnet run --project src/FlashSpot.App/FlashSpot.App.csproj
   ```

### Development notes

- The core logic lives in `FlashSpot.Core` and is kept independent from the UI.
- Indexing status is exposed via small immutable models (see `IndexStatusSnapshot`).
- File search is implemented in `LuceneFileSearchService` (Lucene-based search).

### Repository hygiene

- Build output and IDE-specific files are excluded via `.gitignore`.
- No secrets or environment-specific configuration files are checked into the repo by default.
- If you introduce API keys or private config, keep them **out of source control** and document how to provide them via local config or environment variables instead.

### Configuration

- Configuration is handled in `FlashSpot.Core` via dedicated services (for example `JsonFlashSpotSettingsProvider`), keeping environment-specific details out of the UI layer.
- The current repository does **not** include production configuration files or secrets. You are expected to provide them at deploy time (for example as JSON on disk or environment variables).

### Security and privacy

- **Data scope**: FlashSpot indexes files on the local machine only; there are no outbound network calls in the current codebase.
- **Access control**: The application runs with the permissions of the current user. Any sensitive data they can read may be surfaced in search results.
- **Enterprise rollout**: Before broad deployment, review the indexing rules, configuration location, and packaging to ensure alignment with your internal security and privacy standards.

### Logging, monitoring, and diagnostics

- Logging is currently minimal and focused on local diagnostics.
- For enterprise usage, you should:
  - Integrate with your standard logging framework and sinks (for example, centralized log collection or SIEM).
  - Add basic health and startup checks around the main entry points.
  - Optionally add structured logging around search requests and indexing operations (without capturing sensitive payloads).

### Testing and quality

- As of now, there is **no standalone automated test project** in this repository.
- To make this production-ready, you should:
  - Add unit tests around `FlashSpot.Core` services (search, indexing status, configuration).
  - Add smoke tests for basic UI flows (startup, search, exit).
  - Wire tests into your CI pipeline and require them to pass before deployment.

### Licensing and usage

- A formal license has not yet been specified. In the absence of a `LICENSE` file, treat the code as **all rights reserved**.
- Before using FlashSpot in an enterprise or redistributing it, define an explicit license (for example MIT, Apache-2.0, or a company-internal license) and add it to the repository.
