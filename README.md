# NVDA Sync

A small portable Windows utility for mirroring selected NVDA components from one primary NVDA folder to up to five secondary NVDA folders.

The intended use is to keep a main installed NVDA profile and one or more portable NVDA copies aligned without manually copying add-ons, gestures, dictionaries, profiles, or other configuration files every time.

## Behaviour

- One primary folder pushes to up to five secondary folders, with optional per-folder overrides.
- Settings are stored beside the app in `Settings\settings.json`.
- Folder pickers accept an NVDA folder, portable NVDA folder, NVDA configuration folder, or known component folder.
- The primary folder is shown read-only and can be chosen with Browse or found with Detect.
- Supported components are add-ons, input gestures through `gestures.ini`, `nvda.ini`, speech dictionaries, configuration profiles, other root configuration files, other root configuration folders, and optional portable NVDA program-file updates.
- Add-ons are selected by default; add-on sync can copy all add-ons, update only add-ons already present in a secondary, or be disabled per target. Potentially machine-specific configuration is opt-in.
- Python cache files are excluded by default.
- Auto-sync watches the primary folder and pushes changes after a 1.5 second debounce.
- Unavailable removable-drive secondaries are skipped without interrupting the user, then retried every 60 seconds while auto-sync is enabled.
- Manual sync is available from the main window or tray menu.
- Folders, Add-ons, Options, and Help menus provide keyboard-first access to folder management, add-on pack export, local add-on install, Preferences, updates, project links, contact, donate, and About.
- Add-on pack export writes readable JSON metadata for installed add-ons from the primary folder.
- Local add-on install copies valid unpacked add-on folders and `.nvda-addon` archives into configured secondary folders only.
- Preferences applies changes live and contains sync component choices, stale deletion, Python cache exclusion, auto-sync, Windows startup, start-minimized behavior, and update checks.
- Optional stale-item deletion makes selected secondary components match the primary exactly. Existing-add-ons-only mode updates matching add-ons without adding new ones or removing target-only add-ons.
- Folder validation prevents syncing a folder into itself or into a child/parent folder. Portable program-file updates compare before writing, create optional ZIP backups beside the portable folder, exclude user content from backups, refuse installed NVDA targets, and refuse portable copies that are currently running.
- Command-line switches support closing, showing, syncing the running app, one-shot syncs, component selection, and cache handling.
- A rolling log is written to `Logs\NVDASync.log`; Options > Save log saves the visible log to a chosen file.
- GitHub release update checks and self-updates use `https://github.com/OnjLouis/NVDASync`.
- Automatic update checks can be set to never, startup, hourly, or daily, with optional silent install when a release ZIP is available.

## Keyboard

- `F1`: open the manual.
- `Shift+F1`: check for updates.
- `Ctrl+F1`: open the GitHub project page.
- `Ctrl+,`: open Preferences.
- `Alt+D`: detect the primary NVDA folder from likely user-profile locations.
- `Alt+A`: open the Add-ons menu.
- `Enter`: open properties for the selected secondary folder.
- `Delete`: remove the selected secondary folder when the secondary list has focus.
- `Escape`: hide to the notification area.

## Command line

- `--show`
- `--close`
- `--sync-running`
- `--sync`
- `--primary <folder>`
- `--secondary <folder>`
- `--component <id>`
- `--all-components`
- `--export-addon-pack <file>`
- `--install-addons <folder>`
- `--delete-stale`
- `--no-delete-stale`
- `--exclude-python-cache`
- `--include-python-cache`
- `--save`
- `--apply-update`
- `--version`
- `--help`

## Build

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1
```

The portable app is written to `portable`.
