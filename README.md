# Bluff or Blighty

A party game about silly British place names. Each player finds real places on the map,
renames one of them with a made-up name, and everyone else tries to spot the fake.

## How a game works

1. **Lobby**: one player creates a game and shares the 4-letter code.
2. **Picking**: everyone at once scrolls the map and clicks **3 real place labels** within
   15 km of each other, picks one to **rename**, and types a fake name.
3. **Guessing**: one round per player. The map zooms out to their 3 places. The base map's
   own labels there are hidden and replaced with game labels in the same style, so the fake
   sits where a real village really is and looks just like its neighbours. Everyone else votes.
4. **Scoring**: +1 for spotting the fake, and +1 to the author for each person fooled.

## Stack

| Part | Tech |
|---|---|
| `client/` | React + Vite + TypeScript, [MapLibre GL JS](https://maplibre.org/) |
| `server/BluffOrBlighty.Api` | ASP.NET Core (.NET 10), SignalR hub at `/hub/game` |
| `server/BluffOrBlighty.Tests` | xUnit tests for the game rules |
| `docker-compose.yml` | Postgres + PostGIS (not used yet) |

## Running locally

Start the API (port 5080):

```bash
dotnet run --project server/BluffOrBlighty.Api
```

Start the client (port 5173; it proxies `/hub` to the API):

```bash
npm --prefix client install && npm --prefix client run dev
```

Open http://localhost:5173 in two browser windows to play against yourself. Each tab is its
own player, and a refresh rejoins the same game.

Run the tests:

```bash
dotnet test
```

## Map tiles

The default style is [OpenFreeMap](https://openfreemap.org/) "liberty", which needs no API key.
To use another provider, copy `client/.env.example` to `client/.env.local` and set
`VITE_MAP_STYLE_URL`. The style must use the **OpenMapTiles** schema (a `place` source-layer)
and expression-based filters. MapTiler styles qualify.

### How the map works

- **Picking** (`client/src/map/places.ts` → `placeAt`): `queryRenderedFeatures` on the
  `place` symbol layers returns the clicked label's name, its exact point, and its style layer.
- **Guessing** (`client/src/map/GuessMap.tsx`):
  - `hideBaseLabelsNear` adds a `within` filter to every place layer, removing base labels within about 110 m of the 3 points.
  - A GeoJSON source then draws all 3 labels, using layers copied from the matching base layers.
  - Labels are hidden by *position*, so the browser never receives the renamed place's real name until the reveal.
  - The map is non-interactive while guessing.

## Layout

```
client/src/
  useGame.ts          SignalR connection, rejoin on refresh, actions
  map/places.ts       label picking, hiding, overlay helpers
  map/PickerMap.tsx   submit-phase map
  map/GuessMap.tsx    guessing-phase map
  screens/            Home, Lobby, Submit, Guess, Finished
server/BluffOrBlighty.Api/
  Game/GameSession.cs all game rules (phases, validation, scoring, per-player views)
  Game/GameManager.cs in-memory game store + idle cleanup
  Hubs/GameHub.cs     thin SignalR layer; pushes a "state" view to each player
```

Ideas for later are in [docs/IDEAS.md](docs/IDEAS.md).
