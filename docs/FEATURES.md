# Nexstrap features

A page-by-page guide. Everything Nexstrap stores lives in `%LocalAppData%\Nexstrap`.

## Setup

| Feature | Details |
|---|---|
| **Install** | Double-clicking `Nexstrap.exe` opens setup, which copies it to `%LocalAppData%\Programs\Nexstrap` or a folder you choose. No admin rights are needed, so folders like Program Files aren't allowed. |
| **Options** | Language, desktop shortcut, Start menu shortcut, and whether Roblox links open with Nexstrap. |
| **Run without installing** | Skips setup and remembers that choice. |
| **Update** | Running a newer `Nexstrap.exe` offers to replace the installed copy. The in-app updater also updates it in place. |
| **Uninstall** | From Installed apps in Windows Settings. Removes the exe, shortcuts and Windows entry, gives Roblox links back to your previous launcher, and asks whether to delete your data. A folder you chose is only removed if it's empty. |
| **Languages** | English, Deutsch, Español, Français, Português (Brasil) and Русский, following Windows by default. The whole app is translated, except a few rare technical error messages and screen-reader labels. Change it under Appearance; it applies right away. Text from Roblox, such as game names, stays as Roblox provides it. |

## Home

| Feature | Details |
|---|---|
| **Play Roblox** | Starts the Roblox app (`RobloxPlayerBeta.exe --app`) from the selected build. Your FastFlags are written first (Nexstrap builds only). |
| **Install / Update Roblox** | Shown when no build is found or yours is older than Roblox's live version. Downloads the official build from `setup.rbxcdn.com`, verifies each package's MD5, and installs to `%LocalAppData%\Nexstrap\Versions`. Cancel leaves nothing behind. |
| **Join an experience** | Paste a place ID, `roblox.com/games/<id>/…` link or `…/games/start?placeId=…` link. Nexstrap looks up the name via Roblox's public API, then launches `roblox://placeId=<id>`. If Roblox is already open you're asked first, since Roblox runs one game at a time. |
| **Installations** | Every build found on the PC. *Choose automatically* uses the one matching Roblox's live version (Nexstrap's own first). Builds from other launchers can be used but are never modified or deleted. |
| **Quick settings** | Light theme, close Nexstrap after launch, check for updates on launch. |
| **Nexstrap updates** | Current version, latest release, release notes, *Download & install*. |

## Games

Favorites and recently played experiences (newest 20) with thumbnails from Roblox's public API. Each game has **Play**, a servers button, a desktop-shortcut button (a `.url` file that opens `roblox://placeId=<id>` through whichever program handles Roblox links; existing files are never overwritten) and a favorite star.

**Play history** counts time in games while Nexstrap follows Roblox, which means Roblox was started from Nexstrap or through a link it handles. Time in the Roblox menu isn't counted, and visits under 15 seconds are ignored. Each place or server counts as a separate session. Totals, session counts, the last 7 days and last played dates are stored in `History.json` on this PC. It can be turned off or cleared on the Games page.

## Tray icon

While Nexstrap runs it has an icon in the notification area. Left-click opens the window; right-click offers Open, Play Roblox, the five most recent games and Quit. *Keep running in the tray when closed* makes the X hide the window, and *Start hidden in the tray* keeps it there when Windows opens Nexstrap. Starting Nexstrap again brings the running copy forward.

## Servers

Enter a place ID or link to list its public servers (100 at a time) with player counts, ping and FPS. Sort by most or fewest players or lowest ping; filter by full servers, free slots and maximum ping. **Join** opens that server with Roblox's `gameInstanceId` link.

Ping is the average Roblox reports for the server, not your own. Regions aren't shown: Roblox's public list doesn't include them, and finding them would need your account's sign-in, which Nexstrap never touches.

## Loading Screen

A small window shown when you start Roblox from Nexstrap. It closes when Roblox's window opens, when Roblox exits, or after 20 seconds. Customize the text, background and accent colours, background image and animation (spinner, sliding bar, pulsing logo or none), or use a preset. Roblox's own start-up isn't changed.

## Appearance

- **Theme**: Dark or Light.
- **Presets**: change theme + accent only; your font and background stay.
- **Accent colour**: swatches or `#RGB` / `#RRGGBB`. Text on accent-coloured buttons switches between black and white for readability.
- **Font**: any installed text font (symbol fonts are hidden). The whole app switches immediately.
- **Roblox font** (opt-in, off by default): uses a .ttf/.otf file or your Nexstrap font in Roblox itself by pointing the font family files of a Nexstrap-installed build at that font. Other launchers' builds are never changed. The original files are backed up inside the build and restored at the next launch after you turn it off. Chinese, Japanese and Korean fallback text is left alone. Roblox doesn't support this: it may stop working after a Roblox update, and text from a game's own fonts may not change.
- **App size**: 85%, 100%, 115% or 130%. Not part of saved themes.
- **Death sound and mouse cursor** (opt-in): an .ogg file replaces `content\sounds\ouch.ogg`, and a .png up to 256 × 256 replaces the arrow cursors in `content\textures\Cursors\KeyboardMouse`. Only Nexstrap-installed builds change. Originals are backed up inside the build and restored at the next launch after you remove the file. Roblox doesn't support this, and games with their own sound or cursor won't change.
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
- **Open Nexstrap when Windows starts** adds a per-user startup entry, which uninstalling removes.
- **Roblox CPU priority and CPU cores** use normal Windows process settings when Roblox starts from Nexstrap, with any build. Realtime priority isn't offered because it can freeze a PC. They don't add FPS by themselves, and fewer cores can lower it.
- **Roblox window title** changes the title of Roblox's window once it opens.
- **Clean up old files** deletes files older than 7 days from Roblox's logs folder, Roblox's temp folder and Nexstrap's Downloads folder, and waits if Roblox is open. Settings, games and Roblox builds aren't touched.

## Integrations

- **Discord Rich Presence** (needs the Discord desktop app). Menu: "Inside Nexstrap · Browsing Roblox" by default, and you can write your own text for both lines and choose the card icon (a direct https image link or an Art Asset name). In game: experience name, creator, icon, your avatar headshot and a *View game* button. Each part can be switched off. Nexstrap reads Roblox's own log lines to tell menu from game and stays running in the background until Roblox closes.
- **Start with Roblox**: programs launched when a Nexstrap-started Roblox session begins; optionally asked to close (never force-killed) when it ends.
- **Move your setup**: one JSON file with appearance, launch and Discord settings, FastFlags, saved themes and companion apps. Machine-specific items (window size, background files, chosen build, URL-handler backup) are not exported. Importing backs up the current setup first.
- **Multiple Roblox windows**: not offered. Roblox allows one game at a time and Nexstrap doesn't get around that.

## Notifications

An inbox with a count in the sidebar. It covers new Nexstrap releases, Roblox builds newer than the one Play uses, and configuration warnings: FastFlags Roblox ignores, flags that can't apply because Play uses another launcher's build, and a frame-rate cap above your display's refresh rate. Each category can be turned off. **Open** goes to the page that fixes it. A dismissed notification comes back only if something changes, such as a newer version. Notifications appear inside Nexstrap while it's open, not as Windows pop-ups.

## About

Version and runtime details, *Copy diagnostics*, data and log folders, open-source credits.

## Files and folders

| Path (under `%LocalAppData%\Nexstrap`) | Contents |
|---|---|
| `Settings.json` | All app settings |
| `FastFlags.json` | Your flag list |
| `History.json` | Play time per game |
| `Mods\` | Copies of your death sound and cursor |
| `Themes\*.json` | Saved themes |
| `Backgrounds\` | Copies of background images |
| `Backups\` | FastFlags, Roblox settings and configuration backups |
| `Versions\version-*` | Roblox builds Nexstrap installed |
| `Logs\` | One log per session (newest 10 kept) |
