# Static minimap references

The four PNGs in `native/MinimapAssets` cover world x `[2880,3456)`,
y `[3072,3584)`, planes 0–3. They are north-up, four pixels per tile.
Global atlas coordinates: `x=(worldX-2880)*4`, `y=(3584-worldY-1)*4`.
No Java installation or game-cache access is needed by the shipped application.
The loader checks each embedded PNG against a pinned SHA-256 and dimensions.

Generated on 2026-10-26 from a **copy** of the local official static cache,
using RuneLite `net.runelite:cache:1.13.4` and its published runtime dependencies.
The modern unnamed MAPS index stores terrain in file 0 and locations in file 1;
this generator rejects the older named/encrypted format. No client memory,
account files, credentials or runtime character state are read.
The original cache and dependency binaries are not included.

With those dependencies in `lib/` and a copied static cache in `cache-copy/`:

```text
mvn org.apache.maven.plugins:maven-dependency-plugin:3.8.1:copy-dependencies -DoutputDirectory=lib
javac -cp "lib/*" BuildMinimapAtlas.java NativeObjects.java
java -Dorg.slf4j.simpleLogger.defaultLogLevel=error -Xmx2g -cp "lib/*;." BuildMinimapAtlas cache-copy atlas 45 48 53 55
```

`cache-input-sha256.json` records only input filenames and checksums for this
generation, so a later cache can be distinguished without distributing it.

Terrain uses RuneLite's `MapImageDumper`, with objects, labels and icons disabled.
`NativeObjects` supplies minimap sprite centering and boundary placement, based
on the static scene renderer in [OpenOSRS, pinned source](https://github.com/open-osrs/runelite/blob/915fb55c0a2a1c000dc04a431e68f3bbb29a40cf/runescape-client/src/main/java/Decimator.java).
The corresponding BSD notice is distributed in
`native/Licenses/MinimapRenderer-BSD-2-Clause.txt` and embedded in the EXE.
RuneLite source: [MapImageDumper 1.13.4](https://github.com/runelite/runelite/blob/runelite-parent-1.13.4/cache/src/main/java/net/runelite/cache/MapImageDumper.java).
Game graphics remain Jagex's property; the BSD notice applies to renderer code.

These are approximate static references: terrain colour variation, dynamic
objects, bridges, doors and runtime transforms can differ from the live client.
Generated-image regression tests establish coordinate/transform consistency,
not live recognition. The matcher must still pass feature consensus, edge
agreement, floor disambiguation and two fresh stationary observations before
movement. Do not lower those thresholds to accommodate reference differences.

Regeneration after a cache update requires reviewing the images, updating the
four `WorldMapData.MinimapHashes`, and rerunning navigation plus live tests.
The separate Explv world-map references remain in use for the large map.
