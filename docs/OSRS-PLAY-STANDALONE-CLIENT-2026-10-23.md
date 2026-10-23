# OSRS Play v0.7.0: Independent client milestone

- Native .NET 10/WinForms 2D game surface; manual tile-click navigation
- Custom TCP client/server on 127.0.0.1, randomly bound port and
  per-session random authorization key (never logged).
- Separate authoritative local world: geometry, obstacles, ore rocks,
  rock respawn, mining XP, inventory capacity, sale and simulated gold.
- Automatic task cycle: BFS path → mine → inventory full → market →
  sell → mine. Inputs and outcomes are checked server-side.
- Start/Stop is enforced by the server for all automatic commands;
  drawing can be disabled while the game loop continues.
- Same standalone OSRS Play EXE, same automatic update channel.
- Integration tests verify actual TCP requests, unauthorized request
  rejection, movement validation, server-side stop, mining and sale.

**Not connected to official RuneScape.** OSRS auth, official server
protocol, assets, content, game mechanics, and authorized interface
are outside this current implementation. Live OSRS functionality
requires a genuine approved specification; sandbox tests do not
establish compatibility with Jagex or exclusion from bans.
