# Changelog

All notable changes to Nexstrap are listed here. Versions follow [Semantic Versioning](https://semver.org).

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

[1.6.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.6.0
[1.5.2]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.5.2
[1.5.1]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.5.1
[1.5.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.5.0
[1.4.1]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.4.1
[1.4.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.4.0
[1.3.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.3.0
[1.1.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.1.0
[1.0.0]: https://github.com/brandtmajxi-wq/nexstrap/releases/tag/v1.0.0
