# User Instruction Memory

This file records user instructions, preferences, and project knowledge for reference in future interactions.

## Format

### User Instruction Entry

[User Instruction Summary]
- Date: [YYYY-MM-DD]
- Context: [Mentioned scenario or time]
- Instructions:
  - [Content of user teaching or instruction, described line by line]

### Project Knowledge Entry

[Project Knowledge Summary]
- Date: [YYYY-MM-DD]
- Context: Discovered by Agent while performing [specific task description]
- Category: [Operations & Deployment|Build Methods|Testing Methods|Troubleshooting & Debugging|Workflow & Collaboration|Environment Configuration]
- Instructions:
  - [Specific knowledge points, described line by line]

## Deduplication Strategy

- Before adding a new entry, check for similar or identical instructions.
- If a duplicate is found, skip the new entry or merge it with the existing one.
- When merging, update the context or date information.
- This helps avoid redundant entries and keeps the memory file tidy.

## Entries

[User Instruction Summary]
- Date: 2026-09-30
- Context: User asked to add season and mod features to the ValleyServer project, then to package a binary to try.
- Instructions:
  - Do not push to remote: the upstream repo is someone else's; only commit locally.
  - Reply and reason in Simplified Chinese.

[Project Knowledge Summary]
- Date: 2026-09-30
- Context: Discovered by Agent while building ValleyServer 3.x (`src/ValleyServer`) binaries.
- Category: Build Methods
- Instructions:
  - .NET 8 SDK is not preinstalled. Install via dotnet-install script into `/usr/local/dotnet`; the CDN download is slow and may need to be resumed with `curl -C -`.
  - The environment has no ICU package. Run the SDK with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, otherwise the host aborts with "Couldn't find a valid ICU package".
  - Build: `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 /usr/local/dotnet/dotnet build src/ValleyServer/ValleyServer.csproj -c Release -v minimal`.
  - Publish (self-contained, bundles runtime packs): `dotnet publish src/ValleyServer/ValleyServer.csproj -c Release -r linux-x64 --self-contained true -o <out>` and same with `win-x64`.
  - Project has no PackageReferences; restore only needs the runtime/targeting packs.
  - All compile/build/publish steps must run through the background terminal tool (resource-limit rule).

[Project Knowledge Summary]
- Date: 2026-09-30
- Context: Discovered by Agent while preparing the ValleyServer 3.x runtime.
- Category: Operations & Deployment
- Instructions:
  - Game assets are not in the repo. Download `Content.zip` (434,387,647 bytes) from `https://github.com/Lixeer/ValleyContent/releases/download/G1.6.15/Content.zip` and extract its contents (Animals/, Maps/, ...) into `src/ValleyServer/Content/`; that path is gitignored.
  - Runtime also accepts `VALLEY_CONTENT_PATH` to point at the Content directory.
  - The server writes `config.json` and `saved_farmhands/` beside the executable. Season gameplay adds `saved_farmhands/world/world.json` plus `saved_farmhands/world/locations/*.xml`.
