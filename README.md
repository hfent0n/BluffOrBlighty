# Bluff or Blighty

A party game about silly British place names. Each player finds real places on the map,
renames one of them with a made-up name, and everyone else tries to spot the fake.

## How a game works

1. **Lobby**: one player creates a game and shares the 4-letter code. The host picks a map:
   "Anywhere", or a preset area such as North Norfolk or Swaledale. Players then have to stay
   inside that area.
2. **Picking**: everyone at once scrolls the map and clicks **3 real place labels** within
   the map's area and within 15 km of each other, picks one to **rename**, and types a fake name.
3. **Guessing**: one round per player. The map zooms out to their 3 places. The base map's
   own labels there are hidden and replaced with game labels in the same style, so the fake
   sits where a real village really is and looks just like its neighbours. Everyone else votes.
4. **Scoring**: +1 for spotting the fake, and +1 to the author for each person fooled.

**Campaign:** a single-player ladder of 8 named opponents, from Doris the Parish Newsletter Editor
to the Cartographer.
- Each level has its own map and a bot that gets better at spotting fakes and at writing them
  (obvious → local → crafty).
- Matches are 3 legs (best of 5 at the end). Winning unlocks the next level.
- Up to 3 stars: win, win by 2+, spot every fake.
- Progress is saved in the browser for now.

**Bots:** "Play against a bot" on the home page starts a solo game, and the host can add up to 3
bots (Easy / Normal / Hard) to any lobby.
- Bots read place labels from the same vector tiles the map shows (`server/BluffOrBlighty.Api/Bots`).
- They pick 3 real places, rename one with a made-up name built from local name parts, and vote.
- To vote, they check whether each option exists on the map. Harder bots check more often.

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
  maps/               maps browser, card artwork (UK outline from Natural Earth)
  screens/            Home, Lobby, Submit, Guess, Finished
server/BluffOrBlighty.Api/
  Game/GameSession.cs all game rules (phases, validation, scoring, per-player views)
  Game/GameManager.cs in-memory game store + idle cleanup
  Game/MapPresets.cs  preset map areas (also served at /api/maps)
  Bots/               tile place source, fake-name generator, bot decisions, bot driver
  Hubs/GameHub.cs     thin SignalR layer; pushes a "state" view to each player
```

## Deployment

GitHub Actions tests every PR and deploys `main` to Google Cloud Run as a single container.
See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the one-time setup (`scripts/gcp-setup.sh`).

Ideas for later are in [docs/IDEAS.md](docs/IDEAS.md).
