# NowPlaying RPC

NowPlaying RPC shows the music that you listen to as Discord Rich Presence.
Your Discord profile then shows "Listening to …" with the song, the artist, the album cover and a progress bar.

The program runs in the system tray (the menu bar on macOS). It runs on Windows, macOS and Linux.

It supports these players:

* Plexamp
* Spotify
* Apple Music
* foobar2000

The program is written in C# with .NET 10 and [Avalonia](https://avaloniaui.net) for the tray icon.

## Support matrix

Not every player is available on every operating system. The table shows how the program reads each player.

| Player | Windows | macOS | Linux |
|---|---|---|---|
| Plexamp | Media controls or Plex API | Plex API | MPRIS or Plex API |
| Spotify | Media controls | AppleScript | MPRIS |
| Apple Music | Media controls (Apple Music app or iTunes) | AppleScript (Music app) | Not available (no Linux app) |
| foobar2000 | beefweb component | Not supported (the Mac version cannot load beefweb) | beefweb under Wine |

Notes:

* "Media controls" is the Windows media overlay (GSMTC). A player must send its track to Windows for this to work.
* The Plex API works on all operating systems. It is the most reliable source for Plexamp.
* On Linux, Spotify sometimes reports position 0 through MPRIS. Then the progress bar can be wrong.

## The tray menu

| Item | What it does |
|---|---|
| Status line | Shows the current track, or why nothing shows |
| Pause presence | Removes the presence from Discord until you click it again |
| Start with system | Starts the program when you log in |
| Open config file | Opens the config file in your default editor |
| Reload config | Reads the config file again after you change it |
| Open log | Opens the log file |
| Quit | Stops the program and removes the presence |

## Setup

### 1. Make a Discord application

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications).
2. Click **New Application**.
3. Give it a name, for example `Music`. Discord shows this name in "Listening to Music".
4. Copy the **Application ID**.

Optional: make one application for each player, for example `Plexamp` and `Spotify`.
Put each ID in `client_ids` in the config. Then Discord shows "Listening to Plexamp" for Plexamp.

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

Important: on Windows, use the `net10.0-windows10.0.19041.0` framework. The plain `net10.0` build does not contain the Windows media controls source.

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

**Plexamp through the Plex API**

1. Set `plex.enabled` to `true`.
2. Set `server_url` to the address of your Plex Media Server.
3. Set `token` to your Plex token. See [Finding an authentication token](https://support.plex.tv/articles/204059436).
4. If other people use your server, set `username` to your Plex user name.

The program never sends the Plex token to Discord. It gets album covers from the iTunes Search API.

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
| `discord.status_display` | `name` | The text in the member list: `name` (player), `details` (song title) or `state` (artist) |
| `discord.large_image_fallback` | empty | An asset key or HTTPS URL to show when no album cover is found |
| `discord.show_player_icon` | `false` | Show a small player icon |
| `general.poll_interval` | `2` | Seconds between checks |
| `general.clear_on_pause` | `true` | Remove the presence when the music is paused |
| `general.priority` | all four players | When more than one player is active, use the first player in this list |
| `general.lookup_artwork` | `true` | Find album covers through the iTunes Search API |

## How it works

* The program checks all sources every 2 seconds.
* When more than one player is active, a playing player wins over a paused player. Then the `priority` list decides.
* The program sends an update to Discord only when the track, the play state, the cover or the position (a seek) changes.
* Discord draws the progress bar from the start and end times. The program does not send each second.
* If Discord is closed, the program connects again when Discord starts.

## Platform notes

* **Linux:** GNOME does not show tray icons by default. Install the "AppIndicator and KStatusNotifierItem Support" extension.
* **macOS:** The program does not show a Dock icon. Use the menu bar icon.

## Test from the command line

This command prints the tracks that the program finds, and then stops:

```bash
NowPlayingRpc --once
```

On Windows the program has no console window. Send the output to a pipe to see it, for example `NowPlayingRpc.exe --once | more`.
