# Self-hosting Noctis

`noctis-server` is Noctis without the window: the same library scanner, the same
OpenSubsonic API the desktop app exposes under Settings → Integrations, and the same
device sync, packaged as a console program for a NAS, a VPS or a Docker host. Any
Subsonic-compatible client (Symfonium, substreamer, Feishin, Amperfy, DSub, …) can
browse, search, stream, star and scrobble against it.

It reads and writes the exact data directory layout the desktop app uses
(`library.json`, `library.db`, `artwork/`, `playlists.json`, `server/users.db`,
`sync/`), so a data directory copied from a desktop install works as-is, and the
other way round.

## Docker

```sh
docker build -t noctis-server .
docker run -d --name noctis -p 4747:4747 \
  -v /path/to/music:/music:ro \
  -v noctis-data:/data \
  noctis-server
docker exec -it noctis /app/noctis-server user add alice
```

The container listens on plain HTTP (`NOCTIS_TLS=0`): put a TLS-terminating reverse
proxy in front of it for anything beyond your LAN. Set `NOCTIS_TLS=1` to let it use
its own self-signed certificate instead; the SHA-256 fingerprint is printed at start
and is what a client pins.

The first scan runs when the container starts and repeats every hour
(`NOCTIS_RESCAN_MINUTES`). Changes inside `/music` are also picked up live where the
bind mount delivers file notifications; the periodic scan covers hosts where it does
not.

## Bare metal

Download `noctis-server-linux-x64.tar.gz` or `noctis-server-linux-arm64.tar.gz`
from a release, unpack, and:

```sh
./noctis-server user add alice --admin
./noctis-server serve --data /var/lib/noctis --music /srv/music --port 4747
```

Options fall back to environment variables when absent:

| Option | Environment | Default |
|---|---|---|
| `--data DIR` | `NOCTIS_DATA_DIR` | `~/.config/Noctis` (Linux), `%APPDATA%\Noctis` (Windows) |
| `--music DIR[;DIR]` | `NOCTIS_MUSIC` | the music folders saved in `settings.json` |
| `--port N` | `NOCTIS_PORT` | `4747` |
| `--no-tls` | `NOCTIS_TLS=0` | self-signed TLS on |
| `--rescan-minutes N` | `NOCTIS_RESCAN_MINUTES` | `60` |
| `--no-scan` | | scan on start and periodically |
| `--no-sync` | | device sync on |

`user add` and `user passwd` prompt for the password, or take it from
`NOCTIS_PASSWORD` for scripts. `user apikey NAME` prints a fresh API key for clients
that prefer one over a password. Passwords are stored as PBKDF2-SHA256 hashes and
never leave the server.

## What clients get

- Browse by artist, album, genre, folder; search; random songs; starred items.
- Streaming and download of the original files, with range requests for seeking.
- Cover art per album.
- Playlists: list, create, update, delete.
- Star/unstar and scrobble, written back to the library that the desktop app also reads.
- Noctis device sync (favourites, ratings, play counts, playlists; newest change wins)
  for signed-in Noctis clients.

Not there yet: transcoding on `stream` (`maxBitRate` and `format` are ignored, the
original file is sent), cover-art resizing (`size` is ignored), per-user favourites
and play counts (the library state is shared by all accounts), and a browser UI.

## Running the desktop app and the server on one data directory

Do not point both at the same directory at the same time: each keeps the library in
memory and writes it back, and the last writer wins. Use one or the other per
directory, or give the server its own copy.
