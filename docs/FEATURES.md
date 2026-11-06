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

## Welcome

The first page in the sidebar, so it's what opens by default. It greets you by the time of day, shows a live clock and the date in your Windows format, and fills the window with a landscape for morning, afternoon, evening or night. The landscapes are drawn by `tools/make-welcome-images.ps1` and cover your custom background on this page only. It only reads your PC's clock, so no location is used or sent. Nexstrap asks once, after updating or installing, how it should welcome you. A name is optional, up to 32 characters, stays in `Settings.json` on this PC and isn't part of exports; change or clear it under Appearance. A word filter refuses hateful and obscene names, and works on this PC only. No word list is complete, so some may get through. **Play Roblox** and **Go to Home** are underneath. If you've already changed your sidebar order, Welcome starts at the bottom of it, and you can move or hide it under Appearance.

## Home

| Feature | Details |
|---|---|
| **Play Roblox** | Starts the Roblox app (`RobloxPlayerBeta.exe --app`) from the selected build. Your FastFlags are written first (Nexstrap builds only). |
| **Install / Update Roblox** | Shown when no build is found or yours is older than Roblox's live version. Downloads the official build from `setup.rbxcdn.com`, verifies each package's MD5, and installs to `%LocalAppData%\Nexstrap\Versions`. Cancel leaves nothing behind. |
| **Join an experience** | Paste a place ID, `roblox.com/games/<id>/…` link or `…/games/start?placeId=…` link. Nexstrap looks up the name via Roblox's public API, then launches `roblox://placeId=<id>`. If Roblox is already open you're asked first, since Roblox runs one game at a time. |
| **Rejoin last server** | Shown for 24 hours after leaving a game Nexstrap followed. Uses Roblox's `gameInstanceId` link, so it only works if that server is still running and has room, and may not work for private servers. Also in the tray menu. |
| **Installations** | Every build found on the PC. *Choose automatically* uses the one matching Roblox's live version (Nexstrap's own first). If none matches, it stays on Nexstrap's own build. Builds from other launchers can be used but are never modified or deleted. |
| **Quick settings** | Light theme, close Nexstrap after launch, check for updates on launch. |
| **Nexstrap updates** | Current version, latest release, release notes, *Download & install*. If a newer version is out when Nexstrap opens, it asks once per run, with **Update now** and **Later**. This follows *Check for updates on launch*. |

## Games

Favorites and recently played experiences (newest 20) with thumbnails from Roblox's public API. Each game has **Play**, a servers button, a desktop-shortcut button (a `.url` file that opens `roblox://placeId=<id>` through whichever program handles Roblox links; existing files are never overwritten) and a favorite star.

**Find a game** searches Roblox by name without signing in, using the search behind roblox.com, and shows up to 20 results with sponsored ones left out. Roblox doesn't document this search, so if it changes, Nexstrap says it couldn't search and place IDs and links still work.

**Play history** counts time in games while Nexstrap follows Roblox, which means Roblox was started from Nexstrap or through a link it handles. Time in the Roblox menu isn't counted, and visits under 15 seconds are ignored. Each place or server counts as a separate session. Totals, session counts, the last 7 days and last played dates are stored in `History.json` on this PC. It can be turned off or cleared on the Games page.

**Game profiles** give a game its own FPS cap and graphics quality, set in Roblox's own settings when you join it from Nexstrap, whichever build you use. Your previous values are saved first and go back when Roblox closes, or the next time Nexstrap opens if it closed first. Profiles don't apply to website links, or while Roblox is already open.

## Stats

Total play time, sessions, current and best streak, the last 14 days and your top five games, from the history on the Games page. Streaks and daily bars use the newest 500 sessions. A streak survives until a whole day is missed.

## Tray icon

While Nexstrap runs it has an icon in the notification area. Left-click opens the window; right-click offers Open, Play Roblox, the five most recent games and Quit. *Keep running in the tray when closed* makes the X hide the window, and *Start hidden in the tray* keeps it there when Windows opens Nexstrap. Starting Nexstrap again brings the running copy forward.

## Servers

Enter a place ID or link to list its public servers (100 at a time) with player counts, ping and FPS. Sort by most or fewest players or lowest ping; filter by full servers, free slots and maximum ping. **Join** opens that server with Roblox's `gameInstanceId` link.

Ping is the average Roblox reports for the server, not your own.

**Server locations.** Roblox's public list has no locations, and asking for them before joining needs your account's sign-in, which Nexstrap never touches. Once you're on a server, Roblox writes its address to its own log, and Nexstrap matches that to a city using a community list of Roblox's addresses. The list isn't official, so locations can be wrong, out of date or unknown.

**Find a server by region** (experimental) joins public servers one at a time until one is in the city you picked, up to 8 tries. It closes Roblox between tries, ending it after 10 seconds if it hasn't closed by itself, can take a few minutes, and may not find one. Companion programs set to close with Roblox close and restart with each try.

## Loading Screen

A small window shown when you start Roblox from Nexstrap. It closes when Roblox's window opens, when Roblox exits, or after 20 seconds. Customize the text and an optional second line, text size and colour, the logo (Nexstrap, your own image or hidden), background and accent colours, background image with its strength, fit and blur, animation (spinner, sliding bar, pulsing logo, bouncing dots or none) and its speed, fading, window size, position and corners, or use a preset. *Stay open for at least* keeps it up for 2 or 5 seconds if Roblox opens quickly, on top of Roblox for that time. *Reset loading screen* puts everything back. Roblox's own start-up isn't changed.

## Appearance

- **Theme**: Dark or Light.
- **Presets**: change theme + accent only; your font and background stay.
- **Accent colour**: swatches or `#RGB` / `#RRGGBB`. Text on accent-coloured buttons switches between black and white for readability.
- **Font**: any installed text font (symbol fonts are hidden). The whole app switches immediately.
- **Roblox font** (opt-in, off by default): uses a .ttf/.otf file or your Nexstrap font in Roblox itself by pointing the font family files of a Nexstrap-installed build at that font. Other launchers' builds are never changed. The original files are backed up inside the build and restored at the next launch after you turn it off. Chinese, Japanese and Korean fallback text is left alone. Roblox doesn't support this: it may stop working after a Roblox update, and text from a game's own fonts may not change.
- **Design**: corners (normal, sharp or extra round), card style (solid, glass or outline), sidebar colour (theme, accent tint or see-through), an icons-only sidebar, gradient buttons with an optional second colour, page animation (slide and fade, fade or none), background blur, your own sidebar name and logo, and the tagline. Saved themes keep these; presets leave them alone. Exports don't include the logo file, and shared sidebar names go through the name filter.
- **App size**: 85%, 100%, 115% or 130%. Not part of saved themes.
- **Sidebar**: hide pages and change their order. The top page opens when Nexstrap starts. Home and Appearance can't be hidden, and links inside the app can still open hidden pages. Notifications and About stay at the bottom.
- **Death sound and mouse cursor** (opt-in): an .ogg file replaces `content\sounds\ouch.ogg`, and a .png up to 256 × 256 replaces the arrow cursors in `content\textures\Cursors\KeyboardMouse`. Only Nexstrap-installed builds change. Originals are backed up inside the build and restored at the next launch after you remove the file. Roblox doesn't support this, and games with their own sound or cursor won't change.
- **Background**: PNG/JPG/BMP/GIF, copied into Nexstrap's folder. Opacity, sizing (fill, fit, stretch, center, tile) and position. Cards become slightly translucent while a background is shown.
- **Your themes**: save the current look; apply, export, import or delete. Exports don't include the image file.
- **Reset**: back to defaults; saved themes are kept.

## Performance

These settings change limits and quality; they can't make your PC faster.

- **FPS status**: your primary display's refresh rate, whether your cap suits it, and warnings such as Roblox being open or the ignored `DFIntTaskSchedulerTargetFps` flag being present.
- **Presets**: Balanced, Smoothest, Best looking and Power saver set Roblox's frame-rate cap and graphics quality together. All except Power saver use the highest standard cap your display can show. Roblox must be closed.
- **Maximum frame rate**: 60, 120, 144 or 240, stored in Roblox's own settings.
- **Remove frame-rate limit**: saves `FramerateCap` as 0, which community guides list as uncapped. Roblox only supports up to 240 FPS and doesn't document this, so it may ignore or reset it. Turning it off goes back to the cap that suits your display. Saved values of 0 or less, or 1000 and above, count as no limit.
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
- **Quick presets** add a set of allowlisted flags in one click: Low-end PC, Clean look and Sharp graphics.

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
- **Global hotkeys** (off by default): Ctrl+Alt+N opens Nexstrap and Ctrl+Alt+J rejoins your last server, from any app while Nexstrap is running.
- **Clean up old files** deletes files older than 7 days from Roblox's logs folder, Roblox's temp folder and Nexstrap's Downloads folder, and waits if Roblox is open. Settings, games and Roblox builds aren't touched.

## Integrations

- **Discord Rich Presence** (needs the Discord desktop app). Menu: "Inside Nexstrap · Browsing Roblox" by default, and you can write your own text for both lines and choose the card icon (an uploaded image, a direct https image link or an Art Asset name). **Upload image** takes a PNG, JPG or GIF up to 10 MB. Discord only shows images from a public link, so after you agree the file goes to catbox.moe, a free host Nexstrap has no connection to. Anyone with the link can see it and it can't be deleted later. Nexstrap sends it as `image.png` (or similar) rather than your file's name, and saves JPGs again without their metadata. In game: experience name, creator, icon, your avatar headshot and a *View game* button. Each part can be switched off. *Show server location* (off by default) adds your server's city to the second line, and can be wrong. Nexstrap reads Roblox's own log lines to tell menu from game and stays running in the background until Roblox closes.
- **Start with Roblox**: programs launched when a Nexstrap-started Roblox session begins; optionally asked to close (never force-killed) when it ends.
- **Move your setup**: one JSON file with appearance, launch and Discord settings, FastFlags, saved themes and companion apps. Machine-specific items (window size, background files, chosen build, URL-handler backup) are not exported. Importing backs up the current setup first.
- **Multiple Roblox windows**: not offered. Roblox allows one game at a time and Nexstrap doesn't get around that.

## Notifications

An inbox with a count in the sidebar. It covers new Nexstrap releases, Roblox builds newer than the one Play uses, and configuration warnings: FastFlags Roblox ignores, flags that can't apply because Play uses another launcher's build, and a frame-rate cap above your display's refresh rate. Each category can be turned off. **Open** goes to the page that fixes it. A dismissed notification comes back only if something changes, such as a newer version. Notifications appear inside Nexstrap while it's open, not as Windows pop-ups.

**Game ready alerts** are the one exception: a Windows pop-up from the tray icon when a game finishes loading while another app is in front. They need the tray icon, so they don't appear for games started from a website link while Nexstrap isn't open. Windows' Do Not Disturb can hide them.

## About

Version and runtime details, *Copy diagnostics*, data and log folders, a **log viewer** for Nexstrap's and Roblox's logs (the last 3,000 lines, with search), a replayable tour, open-source credits, and **Privacy and terms**, which explains what stays on your PC, what goes online and the terms for using Nexstrap.

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
