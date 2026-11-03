# Nexstrap features

A page-by-page guide. Everything Nexstrap stores lives in `%LocalAppData%\Nexstrap`.

## Home

| Feature | Details |
|---|---|
| **Play Roblox** | Starts the Roblox app (`RobloxPlayerBeta.exe --app`) from the selected build. Your FastFlags are written first (Nexstrap builds only). |
| **Install / Update Roblox** | Shown when no build is found or yours is older than Roblox's live version. Downloads the official build from `setup.rbxcdn.com`, verifies each package's MD5, and installs to `%LocalAppData%\Nexstrap\Versions`. Cancel leaves nothing behind. |
| **Join an experience** | Paste a place ID, `roblox.com/games/<id>/…` link or `…/games/start?placeId=…` link. Nexstrap looks up the name via Roblox's public API, then launches `roblox://experiences/start?placeId=<id>`. If Roblox is already open you're asked first, since Roblox runs one game at a time. |
| **Installations** | Every build found on the PC. *Choose automatically* uses the one matching Roblox's live version (Nexstrap's own first). Builds from other launchers can be used but are never modified or deleted. |
| **Quick settings** | Light theme, close Nexstrap after launch, check for updates on launch. |
| **Nexstrap updates** | Current version, latest release, release notes, *Download & install*. |

## Appearance

- **Theme**: Dark or Light.
- **Presets**: change theme + accent only; your font and background stay.
- **Accent colour**: swatches or `#RGB` / `#RRGGBB`. Text on accent-coloured buttons switches between black and white for readability.
- **Font**: any installed text font (symbol fonts are hidden). The whole app switches immediately.
- **Background**: PNG/JPG/BMP/GIF, copied into Nexstrap's folder. Opacity, sizing (fill, fit, stretch, center, tile) and position. Cards become slightly translucent while a background is shown.
- **Your themes**: save the current look; apply, export, import or delete. Exports don't include the image file.
- **Reset**: back to defaults; saved themes are kept.

## Performance

These settings change limits and quality; they can't make your PC faster.

- **FPS status**: your primary display's refresh rate, whether your cap suits it, and warnings such as Roblox being open or the ignored `DFIntTaskSchedulerTargetFps` flag being present.
- **Presets**: Balanced, Smoothest, Best looking and Power saver set Roblox's frame-rate cap and graphics quality together. All except Power saver use the highest standard cap your display can show. Roblox must be closed.
- **Maximum frame rate**: 60, 120, 144 or 240, stored in Roblox's own settings.
- **Graphics preferences**: graphics API, anti-aliasing and texture quality. These are allowlisted FastFlags, so they only apply to Nexstrap-installed builds; *Automatic* removes the flag.

## FastFlags

FastFlags are Roblox client configuration values written to `ClientSettings\ClientAppSettings.json` inside a Roblox build.

**Important:** since 29 September 2025 the Roblox Player only applies flags on
[Roblox's allowlist](https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569).
Anything else is ignored (harmlessly). Nexstrap labels every flag accordingly and includes the allowlisted flags in its *Known flags* list. Popular old tweaks such as `DFIntTaskSchedulerTargetFps` are listed as ignored; use Roblox's own *Maximum Frame Rate* setting instead.

- Types come from the name: `FFlag`/`DFFlag` = True/False, `FInt`/`DFInt`/`FLog` = whole number, `FString`/`DFString` = text. Names are case-sensitive.
- Flags are written only into builds Nexstrap installed, right before each launch.
- Import accepts `{ "Name": value }` JSON (Roblox/Bloxstrap/Voidstrap format). *Import from Roblox build* reads another launcher's file without changing it.
- Backups are made before imports, resets, restores and your first edit each session (newest 20 kept).

## Roblox Settings

Edits Roblox's own `%LocalAppData%\Roblox\GlobalBasicSettings_13.xml` (the same values as Roblox's in-game Settings menu):
maximum frame rate (60/120/144/240), automatic or fixed graphics quality (1–10), fullscreen, volume, camera sensitivity and inversion.

- Changes are saved with **Save to Roblox** and take effect next time Roblox starts.
- Saving is blocked while Roblox is running, because Roblox overwrites the file when it exits.
- Only changed keys are written; everything else in the file is preserved. A backup is made before every save/restore (newest 15 kept).
- **Open website Play buttons with Nexstrap** registers the `roblox` and `roblox-player` URL schemes for your user only (HKCU), saving the previous handler so turning it off restores it exactly.

## Integrations

- **Discord Rich Presence** (needs the Discord desktop app). Menu: "Inside Nexstrap · Browsing Roblox" by default, and you can write your own text for both lines. In game: experience name, creator, icon, your avatar headshot and a *View game* button. Each part can be switched off. Nexstrap reads Roblox's own log lines to tell menu from game and stays running in the background until Roblox closes.
- **Start with Roblox**: programs launched when a Nexstrap-started Roblox session begins; optionally asked to close (never force-killed) when it ends.
- **Move your setup**: one JSON file with appearance, launch and Discord settings, FastFlags, saved themes and companion apps. Machine-specific items (window size, background files, chosen build, URL-handler backup) are not exported. Importing backs up the current setup first.
- **Multiple Roblox windows**: not offered. Roblox allows one game at a time and Nexstrap doesn't get around that.

## About

Version and runtime details, *Copy diagnostics*, data and log folders, open-source credits.

## Files and folders

| Path (under `%LocalAppData%\Nexstrap`) | Contents |
|---|---|
| `Settings.json` | All app settings |
| `FastFlags.json` | Your flag list |
| `Themes\*.json` | Saved themes |
| `Backgrounds\` | Copies of background images |
| `Backups\` | FastFlags, Roblox settings and configuration backups |
| `Versions\version-*` | Roblox builds Nexstrap installed |
| `Logs\` | One log per session (newest 10 kept) |
