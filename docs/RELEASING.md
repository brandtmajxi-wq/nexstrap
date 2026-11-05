# Releasing Nexstrap

The self-updater (`Services/Updates/UpdateInstaller.cs`) only installs a release that follows this exact convention.

## 1. Bump the version

Edit `src/Nexstrap/Nexstrap.csproj`: `Version`, `AssemblyVersion` and `FileVersion` (e.g. `1.1.0`, `1.1.0.0`, `1.1.0.0`).
Add a section to `CHANGELOG.md`.

## 2. Build the package

```powershell
powershell -File tools/package-release.ps1
```

This runs the tests, publishes a self-contained single-file `Nexstrap.exe` (win-x64), and writes to
`artifacts/release-v<version>/`:

| File | Purpose |
|---|---|
| `Nexstrap.exe` | The release asset the updater downloads |
| `SHA256SUMS.txt` | `<sha256>  Nexstrap.exe`, checked before installing |
| `RELEASE_NOTES.md` | That version's CHANGELOG section, to paste into the release |

## 3. Publish on GitHub

1. Repository → **Releases → Draft a new release**.
2. **Tag:** `v<version>` (e.g. `v1.1.0`), created from `main`. The tag must equal the exe's version.
3. **Title:** `Nexstrap <version>`; **description:** paste `RELEASE_NOTES.md`.
4. Attach **both** `Nexstrap.exe` and `SHA256SUMS.txt`. The asset names must be exactly these.
5. Leave *Set as a pre-release* unchecked (the updater ignores pre-releases and drafts) and click **Publish release**.

GitHub also records its own SHA-256 for each asset; the updater requires it to agree with `SHA256SUMS.txt`.

## What the updater checks

An update is installed only if:
- the release is published (not draft/pre-release) and newer than the running version;
- the files come from `https://github.com/brandtmajxi-wq/nexstrap/releases/download/…`;
- the download size equals the size GitHub reports;
- the SHA-256 matches `SHA256SUMS.txt` and/or GitHub's asset digest (at least one; they must agree);
- the exe's ProductName is `Nexstrap` and its file version equals the tag.

Then it renames the running exe to `Nexstrap.exe.old`, copies the new one in place (rolling back on failure), restarts with `--updated`, and the new instance deletes the old file.

## DEV Launcher

`tools/update-dev.ps1` builds the current source in the `Dev` configuration and puts it in `%LocalAppData%\Programs\Nexstrap Dev`, with a desktop shortcut. Run it after every change, so the dev copy always has work before it's released. It's never published.

The dev copy keeps its data in `%LocalAppData%\Nexstrap Dev` (copied from your setup the first time) and shares Roblox builds with the installed copy. It can run beside the installed copy, doesn't update from GitHub, and can't change Roblox link handling or Windows startup.

The winter theme and Christmas mode are dev only for now: `WinterSeason.Available` is false in public builds, and the `winter-*.jpg` pictures are only built into the `Dev` configuration. The dev copy reports the next version, set in the `Dev` block of `Nexstrap.csproj`. To release winter, remove both limits and add it to `CHANGELOG.md` and `docs/FEATURES.md`.
