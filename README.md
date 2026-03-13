# legoracersonline
Open source LEGO Racers Online project including the LEGO Racers API.

## Recent updates (2026-03-14)

- Added explicit TCP packet framing (4-byte length prefix) in both client and server network layers.
- Improved TCP receive handling to support partial reads and multiple packets in a stream.
- Added per-participant TCP pending buffer on server side for robust packet assembly.
- Fixed thread-safety issues around server participant list access with locking and local snapshots.
- Fixed UDP endpoint handling on server side to avoid shared endpoint race conditions.
- Normalized coordinate serialization and parsing using invariant culture in client and server.
- Fixed short read size in memory manager (`ReadShort` now reads 2 bytes instead of 4).
- Added working documentation and notes files for implementation planning.
