# OSRS Live Diagnostics

Standalone Java diagnostics application for Windows. No RuneLite runtime dependency.

**Important:** This is a diagnostic simulator, not an authorized Jagex/OSRS game client. It does not authenticate to or automate the official game.

## Releases and updates

The Windows application checks [update-manifest.properties](update-manifest.properties) on each start. A new release is downloaded from GitHub, verified using SHA-256, self-tested, backed up, and installed while the program is not running. Existing logs remain untouched. On download failure, the installed version keeps working.

The `source/osrslive-src.zip` archive contains the complete source code and Windows launcher scripts. The GitHub Actions workflow builds the JAR, runs automated checks, publishes a downloadable Windows ZIP and JAR as a release, then updates the manifest.

Install Java 17+ and run `START-OSRS-Diagnostics.cmd` from the extracted Windows ZIP. `Log kopieren` copies the current diagnostics directly to clipboard.

Official RuneLite integration has been removed from the default client. Its old 'bridge' was only a loopback receiver for external diagnostic events; it never connected to Jagex.