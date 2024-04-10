# Intro Skipper (beta)
<div align="center">
    <p>
        <img alt="Plugin Banner" src="https://raw.githubusercontent.com/jumoog/intro-skipper/master/images/logo.png" />
    </p>
    <p>
        Analyzes the audio of television episodes to detect and skip over intros.
    </p>
</div>

## ⚠️ Warning for Jellyfin 10.9 ⚠️ ##

**The current version is a beta version, so please save your old files before upgrading from `10.8` to `10.9`.
Changes are possible during the beta phase!**

## System requirements

* Jellyfin 10.8.4 (or newer)
* Jellyfin's [fork](https://github.com/jellyfin/jellyfin-ffmpeg) of `ffmpeg` must be installed, version `5.0.1-5` or newer
  * `jellyfin/jellyfin` 10.8.z container: preinstalled
  * `linuxserver/jellyfin` 10.8.z container: preinstalled
  * Debian Linux based native installs: provided by the `jellyfin-ffmpeg5` package
  * MacOS native installs: build ffmpeg with chromaprint support ([instructions](#installation-instructions-for-macos))

## Detection parameters

Show introductions will be detected if they are:

* Located within the first 25% of an episode or the first 10 minutes, whichever is smaller
* Between 15 seconds and 2 minutes long

Ending credits will be detected if they are shorter than 4 minutes.

These parameters can be configured by opening the plugin settings

## Installation

### Step 1: Install the plugin
1. Add this plugin repository to your server: `https://raw.githubusercontent.com/jumoog/intro-skipper/master/manifest.json`
	- 10.8: Please use `https://raw.githubusercontent.com/jumoog/intro-skipper/10.8/manifest.json` instead
2. Install the Intro Skipper plugin from the General section
3. Restart Jellyfin
### Step 2: Configure the plugin
4. OPTIONAL: Enable automatic skipping or skip button
    1. Go to Dashboard -> Plugins -> Intro Skipper
    2. Check "Automatically skip intros" or "Show skip intro button" and click Save
5. Go to Dashboard -> Scheduled Tasks -> Analyze Episodes and click the play button
6. After a season has completed analyzing, play some episodes from it and observe the results
    1. Status updates are logged before analyzing each season of a show

## Troubleshooting
#### Scheduled tasks fail instantly
- Verify that Intro Skipper can detect ffmpeg with Chromaprint
    - Dashboard -> Plugins -> Intro Skipper -> Support Bundle Info
- Verify that ffmpeg is installed and detected by jellyfin
    - Dashboard -> Playback -> FFmpeg path
- Verify that Chromaprint is enabled in ffmpeg (`--enable-chromaprint`)

#### Skip button is not visible
- Verify you have successfully completed the scheduled task at least once
- Clear your browser cache and reload the Jellyfin server webpage
- Fix any permission mismatches between the web folder and Jellyfin server

    * <b>Docker -</b> the container is being run as a non-root user while having been built as a root user, causing the web files to be owned by root. To solve this, you can remove any lines like `User: 1000:1000`, `GUID:`, `PID:`, etc. from the jellyfin docker compose file.

    * <b>Install from distro repositories -</b> the jellyfin-server will execute as `jellyfin` user while the web files will be owned by `root`, `www-data`, etc. This can <i>likely</i> be fixed by adding the `jellyfin` user (or whichever user executes the jellyfin server) to the same group that owns the jellyfin-web folders. **You should only do this if they are owned by a group other than root**.

## Installation (MacOS)

1. Build ffmpeg with chromaprint support using brew:
    - macOS 12 or newer can install the [portable jellyfin-ffmpeg](https://github.com/jellyfin/jellyfin-ffmpeg)

```
brew uninstall --force --ignore-dependencies ffmpeg
brew install chromaprint amiaopensource/amiaos/decklinksdk
brew tap homebrew-ffmpeg/ffmpeg
brew install homebrew-ffmpeg/ffmpeg/ffmpeg --with-chromaprint
brew link --overwrite ffmpeg
```

2. Open ~/.config/jellyfin/encoding.xml and add or edit the following lines
    - Replace [FFMPEG_PATH] with the path returned by `whereis ffmpeg`

```
<EncoderAppPath>[FFMPEG_PATH]</EncoderAppPath>
<EncoderAppPathDisplay>[FFMPEG_PATH]</EncoderAppPathDisplay>
```

4. Follow the [general installation instructions](#installation) above

## Documentation

Documentation about how the API works can be found in [api.md](docs/api.md).
