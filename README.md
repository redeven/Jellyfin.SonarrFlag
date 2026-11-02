<img src="Images/sonarr-flag.png" alt="Sonarr Flag" width="400">

# Sonarr Flag

Mark broken episodes from Jellyfin, and have Sonarr replace them.

## Features

- **Mark as Broken** entry in the context menu of episodes, available from a card's ⋮ menu, a right-click, or the ⋮ button on an episode's details page.
- A **Mark as Broken** button (<sub>heart_broken</sub> icon) in the video player's controls, next to the settings, picture-in-picture and fullscreen buttons.
- After you confirm, Sonarr:
  1. blocklists the release the file came from, so the same release isn't downloaded again,
  2. deletes the episode file,
  3. monitors the episode again, in case deleting the file unmonitored it,
  4. searches for a replacement.
- Files that contain several episodes are handled as a whole: every episode in the file is blocklisted, monitored and searched.
- Only administrators see the buttons, and only administrators can call the API.
- The Sonarr API key stays on the server. The browser only talks to Jellyfin.

## Prerequisites

- Jellyfin 12
- Sonarr v3 or v4
- The [JavaScript Injector](https://github.com/n00bcodr/Jellyfin-JavaScript-Injector) plugin

> [!IMPORTANT]
> The **Mark as Broken** buttons are delivered to the web client by JavaScript Injector. Without it, the plugin does nothing.

## Installation

1. Install **JavaScript Injector** by following the instructions in [its README](https://github.com/n00bcodr/Jellyfin-JavaScript-Injector).
2. Build the plugin (see [Building](#building)) and copy `JellyfinSonarrFlag.dll` into a new `SonarrFlag` folder in Jellyfin's `plugins` directory.
3. Restart Jellyfin.
4. Check that **Sonarr Flag** is listed as **Active** under **Dashboard → Plugins**, then reload your browser tab.

## Configuration

1. Go to **Dashboard → Plugins → Sonarr Flag**.
2. Enter the **Sonarr URL**, as Jellyfin's server reaches it, including any URL base. For example: `http://localhost:8989` or `http://sonarr:8989/sonarr`.
3. Enter the **API key**, from Sonarr's **Settings → General → Security**.
4. Click **Test connection**. It should report Sonarr's version.
5. Click **Save**.

## Usage

- Open the ⋮ menu of an episode and choose **Mark as Broken**, or click the <sub>heart_broken</sub> button while an episode is playing. Playback pauses while the confirmation is open.
- Confirm. A message tells you whether Sonarr accepted the request, or why it didn't.
- Sonarr's **Activity** and **History** pages show the blocklisted release, the deleted file and the search.

## Caveats

- Episodes are matched to Sonarr by the series' TVDB, IMDb or TMDB id, then by season and episode number. Shows whose Jellyfin numbering differs from Sonarr's (for example, absolute or DVD ordering) can match the wrong episode or none.
- The release that is blocklisted is the grab behind the latest import, or the latest grab if there's no import in Sonarr's history. A file that was imported manually has no grab, so nothing is blocklisted and Sonarr may download the same release again. The confirmation message warns you when that happens.
- Jellyfin removes the episode from its library on the next library scan or when Sonarr notifies it, and adds it back once Sonarr imports the replacement.
- The buttons are only available in the web client.

## Building

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. From the repository root, run:
   ```sh
   dotnet publish -c Release -o publish
   ```
3. The built plugin is `publish/JellyfinSonarrFlag.dll`.

## Running the tests

```sh
dotnet test tests/JellyfinSonarrFlag.Tests
cd tests/Web && npm ci && npm test
```

## License

Copyright (C) 2026 Redeven

This plugin is licensed under the [GNU General Public License v3.0](LICENSE).
