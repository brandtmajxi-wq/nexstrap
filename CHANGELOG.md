# Changelog

All notable changes to Nexstrap are listed here. Versions follow [Semantic Versioning](https://semver.org).

## [1.22.0] - 2026-11-08

### Added
- **Secret room** (About): a hidden page that opens with a password. Only a fingerprint of the password is stored in the code.

### Changed
- Password boxes match the rest of the app.

## [1.21.0] - 2026-11-07

### Added
- **Roblox Studio** (Home): open Studio, see if Roblox has released a newer one, and install or update it straight from Roblox with the same file checks as the Player (about 600 MB). Studio keeps its own sign-in, plugins and settings. Older Nexstrap Studio copies are removed once Studio is closed, and other launchers' copies are never changed.

## [1.20.0] - 2026-11-07

### Added
- **Crash alerts** (Notifications, on by default): if Roblox closes suddenly, Nexstrap offers to rejoin the same server or open the logs. This works for Roblox started through Nexstrap.
- **Play time reminder** (Notifications, off by default): a small note in the corner over Roblox every 30 minutes to 3 hours. It doesn't take focus and clicks pass through it. **Preview** shows what it looks like.

## [1.19.1] - 2026-11-06

### Added
- **Automatic Roblox updates** (Roblox Settings, on by default): new Roblox versions download in the background while Roblox is closed, and the download stops if you start Roblox. This only applies to Nexstrap's own builds, and not if you've pinned a build.

## [1.19.0] - 2026-11-06

### Added
- **Game profiles** (Games): give a game its own FPS cap and graphics quality. They're set when you join it from Nexstrap, and your normal settings come back when Roblox closes.
- **Stats**: a new page with your total play time, sessions, streaks, the last 14 days and your top games.
- **Log viewer** (About): read and search Nexstrap's and Roblox's logs inside the app.
- **FastFlag presets**: Low-end PC, Clean look and Sharp graphics, using only flags Roblox still allows.
- **Global hotkeys** (Roblox Settings, off by default): Ctrl+Alt+N opens Nexstrap and Ctrl+Alt+J rejoins your last server.
- **Settings search**: find any option from the top of the window, or press Ctrl+K.
- **First-run tour**: a short tour for new users, which you can replay from About.

### Fixed
- **Find a server by region** stopped after one try, because Roblox takes about 30 seconds to close by itself. It now ends Roblox after 10 seconds, and can't mistake the old server for the new one.
- A game with several places no longer appears more than once in your play history.

### Changed
- The uninstall question now keeps your data if you press Enter.
- The Back button on the privacy page is easier to see.

## [1.18.1] - 2026-11-06

### Added
- **Update reminder**: if you open an older version, Nexstrap asks you to update right away, with **Update now** and **Later** buttons. It uses the same safety checks as other updates, and it follows the *Check for updates on launch* setting.

## [1.18.0] - 2026-11-06

### Added
- **Privacy and terms** (About): what Nexstrap does with your data, what goes online, and the terms for using it.
- **Design options** (Appearance): sharp or extra round corners, glass and outline cards, a sidebar colour, an icons-only sidebar, gradient buttons with your own second colour, page animations, background blur, and your own sidebar name and logo. Saved themes keep all of it.
- **Much more loading screen customisation**: a second line of text, text size and colour, your own logo or no logo, window size, position and corners, image strength, fit and blur, a bouncing dots animation, animation speed, fading in and out, a minimum time on screen, a Neon preset and a reset button.

### Fixed
- Right after a Roblox update, Play could switch to another launcher's build. It now stays on Nexstrap's own build until you update.

## [1.17.2] - 2026-11-05

### Added
- **Name filter**: the Welcome page refuses hateful and obscene names, including spaced-out and number-swapped spellings. A blocked name that was already saved is left out of the greeting.

## [1.17.1] - 2026-11-05

### Fixed
- Status badges such as "Update available" and "Ready to play" were squashed into ovals. They're neat rounded pills now.

## [1.17.0] - 2026-11-05

### Added
- **Your name on the Welcome page**: Nexstrap asks once how it should welcome you, so the page can say something like "Good afternoon, Liam". It's optional, stays on this PC, and can be changed under Appearance.

## [1.16.0] - 2026-11-05

### Added
- **Welcome page**: the new first page greets you with good morning, afternoon, evening or night, shows a live clock and the date, and fills the window with a landscape for that time of day, covering your custom background on this page only. It only uses your PC's clock, never your location. You can hide it or move it under Appearance.

## [1.15.0] - 2026-11-05

### Added
- **Upload image** (Integrations → Card icon): pick a PNG, JPG or GIF for your Discord card instead of pasting a link. Discord needs a public link, so after you agree, the image goes to catbox.moe, where anyone with the link can see it and it can't be deleted later. JPG metadata and your file's name are removed first.

## [1.14.0] - 2026-11-05

### Added
- **Remove frame-rate limit** (Performance, and *No limit* in Roblox Settings): asks Roblox not to limit its frame rate. Roblox only supports up to 240 FPS, so it may ignore this or reset it.

### Fixed
- Very high or unusual saved caps, such as 9999, now show as no limit instead of a strange FPS number.

## [1.13.0] - 2026-11-05

### Added
- **Custom sidebar** (Appearance): hide pages you don't use and change their order. The top page opens when Nexstrap starts. Home and Appearance always stay.

## [1.12.0] - 2026-11-05

### Added
- **Show server location** (Integrations): adds your server's city to your Discord card, like "by Maker · Frankfurt, Germany". It's off by default, because anyone who can see your status will see it.
- **Game ready alerts** (Notifications): a Windows pop-up with the game and server location when a game finishes loading while you're in another app. It stays quiet if Roblox is in front or during a region search.

## [1.11.0] - 2026-11-05

### Added
- **Find a server by region** (Servers, experimental): pick a city such as Frankfurt, and Nexstrap joins public servers one at a time, closing Roblox between tries, until it lands there. It stops after 8 tries and may not find one. Roblox only reveals a server's location after you join, unless you hand over your sign-in, which Nexstrap never touches.
- **Server locations**: the Servers page shows where your current server is, and marks servers you've been on since opening Nexstrap. Locations come from a community list of Roblox's addresses and can be wrong or out of date.

## [1.10.0] - 2026-11-05

### Added
- **Game search** (Games): find games by name, with icons and player counts, then play, favorite or browse their servers. It uses the search behind roblox.com, which Roblox doesn't document, so it could stop working if Roblox changes it.
- **Rejoin last server** (Home and tray): go back to the exact server you were on, for 24 hours after leaving. It only works if the server is still running and has room, and may not work for private servers.

## [1.9.0] - 2026-11-04

### Added
- **Play history** (Games): time played per game, session counts, the last 7 days and last played dates, saved only on this PC. You can turn it off or clear it.
- **Tray icon**: open Nexstrap, play Roblox, jump into a recent game or quit from the notification area.
- **Keep running in the tray when closed** and **Start hidden in the tray** (Roblox Settings).

### Changed
- Starting Nexstrap while it's already open now brings that copy forward instead of opening a second one.
- With play history on, "Close Nexstrap after launch" hides Nexstrap until Roblox closes, so the time can be counted.

## [1.8.0] - 2026-11-04

### Added
- **Roblox CPU priority and CPU cores** (Roblox Settings): choose how Windows treats Roblox when it starts from Nexstrap. These manage your CPU; they don't add FPS by themselves.
- **Roblox window title** (Roblox Settings): your own name in the title bar and taskbar.
- **Death sound and mouse cursor** (Appearance): replace Roblox's defaults in Nexstrap-installed builds with your own .ogg and .png files. Removing them restores the originals. Roblox doesn't support this, and games with their own sound or cursor won't change.
- **App size** (Appearance): 85% to 130%.
- **Open Nexstrap when Windows starts** (Roblox Settings).
- **Clean up old files** (Roblox Settings): removes Roblox logs, Roblox temp files and leftover downloads older than 7 days, on demand or each time Nexstrap opens.

### Fixed
- An installed copy now puts its Installed apps entry back if it goes missing.
- Roblox Settings could say links were handled by "Nobody" until the page was reopened.

## [1.7.0] - 2026-11-04

### Added
- **The whole app is translated**: every page, dropdown, tooltip, dialog and notification now follows your language (English, Deutsch, Español, Français, Português (Brasil) and Русский). Change it under Appearance and it applies right away, no restart needed.

### Changed
- Dates and numbers on the pages follow your Windows region.
- Buttons on the Home cards wrap onto a second line when their text is long.

## [1.6.0] - 2026-11-04

### Added
- **Setup**: double-clicking Nexstrap.exe now offers to install it for your Windows account, with no admin rights. Choose a language, install location, desktop and Start menu shortcuts, and whether Roblox links open with Nexstrap. You can also run it without installing.
- **Uninstall**: Nexstrap appears in Installed apps in Windows Settings. Uninstalling removes its shortcuts, gives Roblox links back to your previous launcher, and lets you keep or delete your data.
- **Languages**: English, Deutsch, Español, Français, Português (Brasil) and Русский. Setup, the menu and page titles are translated; the rest of the app is in English for now. Change it under Appearance.

## [1.5.2] - 2026-11-04

### Changed
- The browsing time on your Discord card no longer resets when you leave a game. It counts from when Roblox was opened. The in-game time still starts fresh for each game.

## [1.5.1] - 2026-11-04

### Changed
- Hovering over the game's image on your Discord card now shows the game name followed by "via Nexstrap".

## [1.5.0] - 2026-11-04

### Added
- **Roblox font** (Appearance): opt-in custom font for Roblox itself, from a .ttf/.otf file or your Nexstrap font. It only changes builds Nexstrap installed, keeps a backup of the original font settings, and restores them when turned off. Roblox doesn't support this, so it may stop working after a Roblox update, and text from a game's own fonts may not change.

## [1.4.1] - 2026-11-04

### Fixed
- **View update** (and **Open** on an update notification) now jumps to the Nexstrap updates card. Before, it seemed to do nothing when Home was already open, because the card was further down the page.

## [1.4.0] - 2026-11-04

### Added
- **Notification Center**: Nexstrap updates, Roblox updates and configuration warnings in one inbox, with a count in the sidebar, per-category preferences and dismissing. A dismissed notification returns only if something changes.

## [1.3.0] - 2026-11-03

### Added
- **Servers page**: browse an experience's public servers with player counts, ping and FPS; sort, filter and join a specific server. Regions aren't shown because Roblox's public list doesn't include them.
- **Games page**: favorites and recently played with thumbnails, quick Play, view servers, and desktop shortcuts.
- **Loading Screen page**: a customizable screen while Roblox starts, with text, colours, background image, animation and presets. It closes itself when Roblox opens.
- **Custom Discord card icon**: use a direct https image link or an Art Asset name (Integrations → Card icon).

### Changed
- Joining now uses Roblox's documented `roblox://placeId=` link.
- The sidebar scrolls on small windows.

## [1.1.0] - 2026-11-03

### Added
- **Performance page**: FPS status that compares your cap with your display's refresh rate, four presets (Balanced, Smoothest, Best looking, Power saver), the frame-rate cap, and graphics preferences (graphics API, anti-aliasing, texture quality) using allowlisted FastFlags.
- **Custom Discord text**: choose both menu lines on your Discord card (Integrations).

### Fixed
- After installing an update, the old version could log errors while closing because it could no longer load parts of itself. It now saves first and exits straight after the swap.
- The app icon files were missing from the GitHub source, which stopped it from building.

## [1.0.0] - 2026-11-03

First public release.

### Launching
- Detects Roblox installed by the official installer, Bloxstrap, Voidstrap, Fishstrap or Nexstrap; choose which build **Play** uses, or let Nexstrap pick the one matching Roblox's live version.
- **Play Roblox** opens the Roblox app; **Join an experience** accepts a place ID or roblox.com link, shows the game's name, creator and player count, and remembers recent games.
- Handles `roblox:` / `roblox-player:` links (optional: make the website's Play buttons open Nexstrap, with exact restore of the previous handler).
- Installs and updates the official Roblox Player from Roblox's CDN with MD5 verification of every package; removes outdated Nexstrap-installed builds.

### Appearance
- Dark and light themes, accent colour (swatches or HEX), any installed font with live preview, background images with opacity, sizing and position.
- Seven presets; save, apply, import and export your own themes; one-click reset. Everything applies instantly.

### FastFlags
- Full editor: add, edit, enable/disable, delete, search and filter, with type validation.
- Each flag is labelled **Applied by Roblox** or **Ignored by Roblox** according to Roblox's allowlist (checked 3 Nov 2026); catalogue of known flags with descriptions.
- Import/export JSON (Bloxstrap/Voidstrap format), import from another launcher's build (read-only), automatic backups with restore.

### Roblox Settings
- Frame-rate cap, graphics quality, fullscreen, volume, camera sensitivity and inversion, written to Roblox's own settings file only while Roblox is closed, with automatic backups and restore.

### Integrations
- Discord Rich Presence: menu ("Inside Nexstrap · Browsing Roblox") and in-game (name, icon, your avatar, *View game* button), with privacy options.
- Companion programs that start with Roblox and can be asked to close with it.
- Export/import your whole Nexstrap setup as one file.

### Updates
- Quick update check on every launch with an in-app banner; release notes, progress and cancel.
- Updates are installed only after verifying source, size, SHA-256, product name and version; failed swaps roll back.

[1.22.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.22.0
[1.21.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.21.0
[1.20.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.20.0
[1.19.1]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.19.1
[1.19.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.19.0
[1.18.1]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.18.1
[1.18.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.18.0
[1.17.2]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.17.2
[1.17.1]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.17.1
[1.17.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.17.0
[1.16.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.16.0
[1.15.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.15.0
[1.14.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.14.0
[1.13.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.13.0
[1.12.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.12.0
[1.11.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.11.0
[1.10.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.10.0
[1.9.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.9.0
[1.8.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.8.0
[1.7.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.7.0
[1.6.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.6.0
[1.5.2]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.5.2
[1.5.1]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.5.1
[1.5.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.5.0
[1.4.1]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.4.1
[1.4.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.4.0
[1.3.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.3.0
[1.1.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.1.0
[1.0.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.0.0
