# NowPlaying RPC

NowPlaying RPC shows the music that you listen to as Discord Rich Presence.
Your Discord profile then shows "Listening to …" with the song, the artist, the album cover and a progress bar.

The program runs in the system tray (the menu bar on macOS). It runs on Windows, macOS and Linux.

It supports these players:

* Plexamp
* Spotify
* Apple Music
* foobar2000

The program is written in C# with .NET 10. On Windows, it makes the tray icon with the Windows API directly. On macOS and Linux, it uses [Avalonia](https://avaloniaui.net) for the tray icon.

The program uses little memory: about 17 MB private memory on Windows when idle.

## Support matrix

Not every player is available on every operating system. The table shows how the program reads each player.

| Player | Windows | macOS | Linux |
|---|---|---|---|
| Plexamp | Plexamp local API or Plex API | Plexamp local API or Plex API | Plexamp local API, MPRIS or Plex API |
| Spotify | Media controls | AppleScript | MPRIS |
| Apple Music | Media controls (Apple Music app or iTunes) | AppleScript (Music app) | Not available (no Linux app) |
| foobar2000 | beefweb component | Not supported (the Mac version cannot load beefweb) | beefweb under Wine |

Notes:

* "Media controls" is the Windows media overlay (GSMTC). A player must send its track to Windows for this to work.
* Plexamp desktop has a local player API on port 32500. The program reads it with no setup and no token. Plexamp does not report to the Windows media controls.
* The Plex API reads Plexamp through your Plex Media Server. Use it when the local API is not available, for example for Plexamp on another device.
* The program writes each media session that it ignores to the log. Use this to find the name of a player that does not match.
* On Linux, Spotify sometimes reports position 0 through MPRIS. Then the progress bar can be wrong.

## The tray menu

Right-click or left-click the tray icon to open the menu. The menu is the native system menu on each operating system. On Windows, it follows the dark or light theme.

When you hover over the tray icon, it shows the current track and the Discord connection state.

| Item | What it does |
|---|---|
| Track line | Shows the current track, or why nothing shows |
| Discord line | Shows if the program is connected to Discord |
| Pause presence | Removes the presence from Discord until you click it again |
| Start with system | Starts the program when you log in |
| Open config file | Opens the config file in your default editor |
| Reload config | Reads the config file again after you change it |
| Open log | Opens the log file |
| Close | Stops the program and removes the presence |

## Setup

### 1. Make a Discord application

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications).
2. Click **New Application**.
3. Give it a name, for example `music`. Discord always shows this name in "Listening to …", so the name `music` gives "Listening to music". A program cannot change this text.
4. Copy the **Application ID**.

To change the text later, rename the application. The Application ID stays the same. Discord can take a few minutes to show the new name.

Optional: make one application for each player, for example `music on Plexamp` and `music on Spotify`.
Put each ID in `client_ids` in the config. Then Discord shows "Listening to music on Plexamp" for Plexamp.

Optional: upload player icons under **Rich Presence → Art Assets**.
Use the keys `plexamp`, `spotify`, `apple_music` and `foobar2000`. Then set `show_player_icon` to `true`.

### 2. Build the program

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download). Then run the command for your operating system.

Windows:

```bash
dotnet publish NowPlayingRpc -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained -p:PublishSingleFile=true
```

macOS (Apple Silicon; use `osx-x64` for Intel):

```bash
dotnet publish NowPlayingRpc -c Release -f net10.0 -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

Linux:

```bash
dotnet publish NowPlayingRpc -c Release -f net10.0 -r linux-x64 --self-contained -p:PublishSingleFile=true
```

The program is in `NowPlayingRpc/bin/Release/<framework>/<runtime>/publish/`.

Important: on Windows, use the `net10.0-windows10.0.19041.0` framework. The plain `net10.0` build is for macOS and Linux only. It does not contain the Windows media controls source or the native Windows tray menu.

### 3. Set the Application ID

1. Start the program. It makes a config file with default values.
2. Click the tray icon and select **Open config file**.
3. Set `client_id` to your Application ID.
4. Enable the sources that you need (see below).
5. Save the file and select **Reload config**.

The config file is here:

* Windows: `%APPDATA%\NowPlayingRpc\config.json`
* macOS: `~/Library/Application Support/NowPlayingRpc/config.json`
* Linux: `~/.config/NowPlayingRpc/config.json`

The log file `log.txt` is in the same folder.

### 4. Set up the players

**Plexamp**

Plexamp needs no setup. The program reads Plexamp desktop through `http://127.0.0.1:32500`.
To turn this off, set `plexamp.enabled` to `false`.

**Plexamp through the Plex API (optional)**

1. Set `plex.enabled` to `true`.
2. Set `server_url` to the address of your Plex Media Server.
3. Set `token` to your Plex token. See [Finding an authentication token](https://support.plex.tv/articles/204059436).
4. If other people use your server, set `username` to your Plex user name.

The program never sends the Plex token to Discord. It gets album covers from public sources instead (see "Album covers").

**foobar2000**

1. Install the [beefweb](https://github.com/hyperblast/beefweb) component in foobar2000.
2. Make sure that beefweb listens on port 8880, or change `foobar2000.url`.
3. If you set a password in beefweb, put the user name and password in the config.

**Spotify and Apple Music**

These players need no setup. Keep `media_session.enabled` set to `true`.

On macOS, the first run shows a permission prompt. Allow the program to control Spotify and Music.

## Config settings

| Setting | Default | Description |
|---|---|---|
| `discord.client_id` | empty | Your Discord Application ID |
| `discord.client_ids` | empty | One Application ID for each player (optional) |
| `discord.status_display` | `state` | The text in the member list: `state` (artist), `details` (song title) or `name` (Discord application name) |
| `discord.large_image_fallback` | empty | An asset key or HTTPS URL to show when no album cover is found |
| `discord.show_player_icon` | `false` | Show a small player icon |
| `general.poll_interval` | `2` | Seconds between checks |
| `general.clear_on_pause` | `true` | Remove the presence when the music is paused |
| `general.priority` | all four players | When more than one player is active, use the first player in this list |
| `general.lookup_artwork` | `true` | Find album covers through iTunes and MusicBrainz |
| `media_session.enabled` | `true` | Read players through Windows media controls, AppleScript on macOS or MPRIS on Linux |
| `plexamp.enabled` | `true` | Read Plexamp through its local player API |
| `plexamp.url` | `http://127.0.0.1:32500` | The address of the Plexamp local player API |
| `plex.enabled` | `false` | Read Plexamp through your Plex Media Server |
| `plex.server_url` | `http://127.0.0.1:32400` | The address of your Plex Media Server |
| `plex.token` | empty | Your Plex token |
| `plex.username` | empty | Show only the sessions of this Plex user. Leave empty for all users |
| `plex.verify_tls` | `true` | Check the server certificate. Set to `false` for a self-signed certificate |
| `foobar2000.enabled` | `true` | Read foobar2000 through the beefweb component |
| `foobar2000.url` | `http://127.0.0.1:8880` | The address of beefweb |
| `foobar2000.username` | empty | The beefweb user name, if you set one |
| `foobar2000.password` | empty | The beefweb password, if you set one |

## How it works

* The program checks all sources every 2 seconds.
* When more than one player is active, a playing player wins over a paused player. Then the `priority` list decides.
* The program sends an update to Discord only when the track, the play state, the cover or the position (a seek) changes.
* Discord draws the progress bar from the start and end times. The program does not send each second.
* If Discord is closed, the program connects again when Discord starts.

## Album covers

Discord can show only a cover with a public web address. Most players give no such address, so the program searches for the cover:

1. The iTunes Search API, with the album artist and the album name.
2. MusicBrainz and the Cover Art Archive, if iTunes has no match.

The program uses a cover only when the album name matches. This prevents a wrong cover from a similar album.
To see the cover that the program finds, run `NowPlayingRpc --once`.

## Platform notes

* **Windows:** "Start with system" writes two registry entries: one under `Run` and one under `StartupApproved\Run`. Newer Windows 11 builds skip an app at login if the second entry is missing. You can also turn the app on or off in Task Manager → Startup apps. The check mark in the menu follows that setting.

* **Linux:** GNOME does not show tray icons by default. Install the "AppIndicator and KStatusNotifierItem Support" extension.
* **macOS:** The program does not show a Dock icon. Use the menu bar icon.

## Test from the command line

This command prints the tracks and covers that the program finds, and then stops:

```bash
NowPlayingRpc --once
```

On Windows the program has no console window. Send the output to a pipe to see it, for example `NowPlayingRpc.exe --once | more`.

## License

This project uses the MIT License. See [LICENSE](LICENSE).
