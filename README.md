# 🍅 aADHD (anti-ADHD)

aADHD (anti-ADHD) is a menu bar (macOS) and system tray (Windows) app that helps you build study habits with Pomodoro sessions, website blocking, and a place to park distractions for later.

- **Mac:** see [Install](#install).
- **Windows:** see [Windows](#windows).

## Features

- **A customizable focus cycle:** 35 minutes of focus, 3 minutes to wrap up, and a 7-minute break by default. Set wrap-up or break to 0 minutes to skip it.
- **Website blocking:** Redirects distracting sites to a local blocked page during focus and wrap-up, including while the timer is paused. The default list includes YouTube, Instagram, LinkedIn, Facebook, X, Twitter, Reddit, and Netflix. You can paste full URLs into the list; only the domain is kept, and lines that aren't recognized as domains are reported. An allow list overrides the block list for specific subdomains; by default it contains `music.youtube.com`, so YouTube Music (including its installed Chrome app) keeps playing during focus.
- **Distraction reminders:** Outside focus and wrap-up, warns you after 10 minutes of tracked use on a listed site, then at further 10-minute intervals. Brief interruptions of up to 3 minutes can count toward the same streak.
- **A "Later" list:** Capture a thought from the menu bar or press `⌥⇧L` from any app. Review your list during wrap-up.
- **Daily planning:** Organize tasks into must-do, nice-to-do, and other tasks for today or any other day, then check them off as you go. The to-do window opens the first time you use your Mac after 6 a.m. each day, showing anything you planned ahead; you can turn this off in Settings.
- **Weekly history:** Review daily study time, completed sessions, wrap-up notes, and time spent on distracting sites. Sessions that cross midnight are split between the two days.
- **Study stats:** By day or week, see study time, completed sessions, focus success rate, and the top five apps and websites you used while focusing, with their share and minutes.
- **Friction before quitting:** Pausing during focus asks "Have you earned this break?" first, and giving up requires typing a confirmation phrase (configurable). Spacing and punctuation don't matter.
- **Korean and English:** Choose the language at the top of Settings. On first launch, the app follows your Mac's language.

## Requirements

- macOS 14 Sonoma or later, on Apple Silicon or Intel.
- A supported browser: Google Chrome, Safari, Arc, Brave, or Microsoft Edge. Firefox is not supported.

## Install

### Option 1: Download (recommended)

1. Download **FocusGuard.zip** from the [latest release](https://github.com/brightree/aADHD/releases/latest).
2. Open the zip if it isn't unzipped already, and drag **FocusGuard.app** into your **Applications** folder.
3. Open it. Because the app isn't signed by Apple, macOS blocks it the first time:
   - **macOS 15 or later:** click **Done**, then go to **System Settings → Privacy & Security**, scroll down, and click **Open Anyway** next to FocusGuard.
   - **macOS 14:** Control-click the app, choose **Open**, then click **Open** again.

   Or run this once in Terminal instead:

   ```bash
   xattr -dr com.apple.quarantine /Applications/FocusGuard.app
   ```

aADHD appears as a 🍅 in the menu bar. It shows up in the Dock and ⌘Tab only while one of its windows is open. The download, app file, and data folder keep the name FocusGuard, so existing installs keep updating and keep their records.

### Option 2: Build from source

Requires Xcode Command Line Tools. Copy this line, paste it into **Terminal**, and press Return:

```bash
if ! xcode-select -p >/dev/null 2>&1; then xcode-select --install; echo "👉 Finish the installer that just opened, then paste this command again."; else cd ~ && ( [ -d focus-guard ] || git clone -q https://github.com/brightree/aADHD.git focus-guard ) && cd focus-guard && git pull -q && cd app && ./build.sh && { pgrep -x FocusGuard >/dev/null && osascript -e 'tell application id "local.focusguard.app" to quit' && sleep 2; open FocusGuard.app; }; fi
```

- If Xcode Command Line Tools aren't installed yet, an installer opens. When it finishes, paste the same command again.
- The source is downloaded to `~/focus-guard`, built for your Mac, and opened.
- If the build fails with errors about `Foundation` or the SDK, update or reinstall Command Line Tools (`sudo rm -rf /Library/Developer/CommandLineTools && xcode-select --install`), or use Option 1.

### Permissions

| Permission | When it is requested | What it is used for |
| --- | --- | --- |
| Automation | When the app first tries to control a supported browser | Read tab URLs, redirect blocked tabs, and close a tab when you choose that action in a reminder. |
| Notifications | On first launch | Show phase changes, distraction reminders, update notices, and storage problems. |
| Launch at login | When you enable it in Settings | Start aADHD when you log in. Blocking only works while the app is running. |

If you deny browser automation, the menu bar panel shows a warning with a shortcut to **System Settings → Privacy & Security → Automation → aADHD**, where you can turn on the relevant browser.

## Updating

The app checks GitHub for a new release once a day. When one is available, you get a notification and an **Update** button in the menu bar panel and at the bottom of the main window. The update window lists what changed; **Update Now** installs it and relaunches the app. If the update fails, the current version keeps running.

- **Downloaded app:** the new FocusGuard.zip is downloaded and replaces the app in place.
- **Built from source:** the app runs `git pull` and `build.sh` in its folder.

Releases are signed with the same certificate, so browser automation permission granted once carries over to later updates. (Updating from 1.6.0 or earlier asks one more time.)

On version 1.1.1 or earlier, update once from Terminal:

```bash
killall FocusGuard; cd ~/focus-guard && git pull && cd app && ./build.sh && open FocusGuard.app
```

The current version is shown at the bottom of Settings and the main window. See [CHANGELOG.md](CHANGELOG.md) for changes.

## Windows

The Windows version has the same features and data format. It lives in the system tray (the 🍅 shows the minutes left).

### Install

1. Download **FocusGuard-Windows.zip** from the [latest release](https://github.com/brightree/aADHD/releases/latest).
2. Unzip it into a folder you own, such as `Documents\FocusGuard` (not `Program Files`, so the app can update itself).
3. Run **FocusGuard.exe**. Because the app isn't signed, Windows SmartScreen may say "Windows protected your PC": click **More info → Run anyway**.
4. If you don't see the 🍅, click the **^** arrow next to the clock and drag it onto the taskbar.

Requirements: Windows 10 or 11 (64-bit). Supported browsers: Chrome, Edge, Brave, Firefox, Vivaldi, Opera, and Naver Whale.

### Differences from the Mac version

- "Later" shortcut is **Ctrl+Alt+L** (Alt+Shift switches the input language on Windows).
- Blocking reads the address bar of the **active tab** through Windows UI Automation. Tabs that were already open are blocked when you switch to them. Reading the address bar can make some browsers turn on their accessibility support, which uses a little more memory.
- Data is stored in `%APPDATA%\FocusGuard\data.json`; settings such as the storage folder and update checks are in `%LOCALAPPDATA%\FocusGuard`. If something goes wrong, `%LOCALAPPDATA%\FocusGuard\crash.log` has the details.
- Updates download **FocusGuard-Windows.zip** from the latest release and replace `FocusGuard.exe` in place.
- **Settings → Delete aADHD…** removes the app, turns off launch at login, and can move your data to the Recycle Bin.

## Everyday use

1. Click the 🍅 menu bar icon to start a session, pause or resume the timer, open the main window, or end the current phase. You can also add an item to your Later list here.
2. Press `⌥⇧L` to capture a thought without opening the main window.
3. Use the main window to review your weekly history, plan to-dos, open Study Stats, or open Settings.
4. In Settings, change the language, durations, blocked and allowed sites, the stopping phrase, tracking, the morning to-do window, the storage folder, and launch at login. Settings can be changed at any time; new durations apply from the next phase. Every numeric setting is kept within a fixed range (for example, focus is 1–180 minutes).

## Privacy, safety, and limitations

### Browser access

[BrowserWatcher.swift](app/Sources/BrowserWatcher.swift) checks the active tab of the foreground browser roughly once per second. At the start of a focus session—and when the app launches with focus or wrap-up still active—it also reads the URLs of all tabs in running supported browsers and redirects matching tabs, including those in background windows.

Browsing statistics contain the matched domain and tracked time, not full URLs or page contents. For study stats, the app also records, during focus and wrap-up only (not while paused or idle for a minute), the name of the frontmost app and, for supported browsers, the active tab's domain, with time spent. You can turn this off in Settings. The original URL is encoded in the blocked page's URL fragment so you can return to it later. Fragments are not sent to the local HTTP server, but the full blocked-page URL may remain in browser history or browser sync.

macOS grants broad browser automation permission. This app uses it to read tab URLs, replace them with the blocked page, and close a tab when you choose “Close this tab” in a reminder.

**Redirecting a tab can discard unsaved work.** Save drafts before starting a focus session. The reminder's close action first re-checks the browser's current tab and closes it only if it is still on the site the reminder was about; if you've switched to another site, it does nothing.

### Local web server

[Server.swift](app/Sources/Server.swift) serves the blocked page at `http://127.0.0.1:47321`. It binds to the IPv4 loopback address, so it is not directly accessible from other devices on your network.

The server requires the exact `Host` header `127.0.0.1:47321`. When an `Origin` header is present, it must match `http://127.0.0.1:47321`. These checks help restrict requests from other websites; they do not authenticate local applications. Malformed requests (missing, invalid, or duplicate `Content-Length`) are rejected, request bodies are limited to 64 KB, and connections are closed after 5 seconds.

Other programs running on your Mac can read the current phase, remaining time, and session task title, or add items to the Later list through this server.

If the server cannot start—for example, because another process is using the port—the app still redirects tabs to that address. The blocked page may fail to load; if another service is listening there, its response may appear instead. When the blocked page can't reach the app, it says so rather than claiming the session has ended.

### Storage and backups

By default, records are stored only on your Mac:

```text
~/Library/Application Support/FocusGuard/data.json
```

You can check the active location or choose another folder in Settings. If the selected folder already contains `data.json`, the app loads that file instead of merging it with the current records. The folder only changes if the current records were saved first and the new location can be read (or written, if it's empty); otherwise the app keeps the old location and tells you why.

The file contains tasks, Later items, timer state, session history, wrap-up notes, and distraction statistics. It is **unencrypted JSON**. Anyone with access to the file or its synced folder can read it.

The app creates a dated backup on the first successful save each day and keeps the 14 most recent backups in a `backups/` subfolder. These are local copies beside the data file, not independent backups.

How the app protects an existing file:

- **The file exists but can't be read** (for example, a cloud file that hasn't downloaded): the app stops saving so it can't overwrite it, and shows a warning.
- **The file can be read but is damaged:** the app copies it to `data.unreadable-<timestamp>-<id>.json`, checks that the copy matches the original byte for byte, and only then starts with empty records. If the copy can't be made, it stops saving instead.
- **A save fails** (for example, the folder is read-only or disconnected): the menu bar panel shows the error, and the app retries every 30 seconds.

While saving is stopped or failing, the Later inputs refuse new items instead of pretending to save them.

There is no conflict resolution for multiple Macs sharing one folder. A later save can overwrite another Mac's changes.

### Network and system changes

aADHD has no analytics or tracking. Its only outbound requests are the daily update check, which reads the latest release's version and notes from `api.github.com`, and, when you click **Update Now**, downloading that release (or, for source builds, `git pull`). Nothing about you or your usage is sent. **If you choose a folder synced by a cloud service (for example, Google Drive or iCloud Drive), that service uploads your records.**

The app does not require administrator privileges or change `/etc/hosts`, network settings, or other system files.

### Performance and blocking limits

The app uses a single one-second timer for session updates and browser monitoring. Regular polling runs AppleScript only when a supported browser is in the foreground. The all-tab sweep runs at most two scripts per running browser: one to list tabs and one to redirect every matching tab at once. Closed windows and the menu bar panel are released from memory, so they don't keep redrawing in the background.

Time on distracting sites is collected in memory and written to the data file at most every 30 seconds (and on quit). If the app crashes, up to 30 seconds of distraction time can be lost; other changes are saved within about a second.

Browser scripts run synchronously on the main thread with a two-second timeout per script. An unresponsive browser can briefly freeze the interface.

aADHD is a self-management aid. Quitting or force-quitting the app, denying automation permission, or using an unsupported browser bypasses blocking.

Releases are signed with a self-signed certificate, not an Apple Developer ID, and aren't notarized, so Gatekeeper blocks the downloaded app until you allow it once (see Install). Builds from source are ad-hoc signed, so macOS may ask for automation permission again after each rebuild.

## Uninstall

**Settings → Delete aADHD…** turns off launch at login, moves the app to the Trash, and quits. Check the box to move your records folder to the Trash as well. Items in the Trash can still be restored until you empty it.

To uninstall by hand:

1. Disable launch at login in Settings if you enabled it.
2. Open the main window from the menu bar and choose Quit.
3. Delete `FocusGuard.app`.
4. To remove your records, delete the active storage folder shown in Settings, including its `backups/` subfolder and any `data.unreadable-*.json` copies. If you changed locations, check previous folders for retained copies as well. Deleting files in a synced folder may also remove them from other devices.
5. Optionally disable aADHD's browser permissions under **System Settings → Privacy & Security → Automation**.

## Project structure

```text
windows/                   Windows version (.NET, WinForms)
  FocusGuard.Core/         Timer, records, settings, local server — shared rules, tested on any OS
  FocusGuard.Core.Tests/   Unit tests
  FocusGuard.App/          Tray app, windows, address-bar watcher, updater
app/
  VERSION                  App version (shown in Settings)
  build.sh                 Builds the app with swiftc; no Xcode project required
  release.sh               Builds a universal app and publishes it as a GitHub release
  MakeIcon.swift           Draws the 🍅 app icon at build time
  Sources/
    main.swift             Menu bar, windows, global shortcut, and app startup
    Store.swift            Timer, statistics, and JSON persistence
    Models.swift           Data models and setting ranges
    BrowserWatcher.swift   Browser monitoring and redirects through AppleScript
    Server.swift           Loopback HTTP server for the blocked page
    BlockedPage.swift      Blocked-page HTML, CSS, and JavaScript
    Views.swift            SwiftUI views
```

## Credits

Made by Anonymous, KAIST IS

## License

[MIT](LICENSE)
