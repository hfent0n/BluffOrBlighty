# Future ideas

Ideas for later. Sections marked **Built** are done; the rest is not built yet.

## Preset maps

> **Built:** the host picks from a maps screen in the lobby. Presets live in
> `server/BluffOrBlighty.Api/Game/MapPresets.cs`, and picks are limited to the preset's radius.
> Still to do: saved and shared community maps.

Let the host pick a starting area from a list, so a game doesn't begin with everyone scrolling around the whole country.

- Each preset is a name, a centre point, a zoom level and a radius, e.g. "North Norfolk", "The Cotswolds", "Yorkshire Dales", "Cornish coast", "Welsh Marches".
- Players can only pick places inside the preset's area. The map is limited with MapLibre `maxBounds`, and the server checks every pick against the area as well as against the current spread limit (`GameSession.MaxSpreadKm`).
- Presets can start as a JSON file in the API. Later they can move to Postgres so people can save and share their own.

## Custom mode

The host builds their own setup:

- **Location**: click the map or search for a place to set the centre.
- **Radius**: how far from the centre players can pick, shown as a circle on the map.
- **How many real and how many fake** places each player submits. Today it's fixed at 2 real and 1 fake. This could be N real and M fake, e.g. 3 real + 1 fake, or 2 real + 2 fake where you have to find both.
  - Scoring with more than one fake: a point for each fake found, perhaps minus points for wrong picks.
  - The server's `SubmissionRequest` and `Round` would hold lists instead of a fixed 2 + 1.
- **Other options**: a timer for the submit phase, a timer for voting, a points-to-win target, and whether players can pan and zoom the map while guessing.

## Shared location vs. separate locations

The game plays differently depending on whether everyone starts in the same place, so this should be a mode the host chooses:

- **Same location ("Local knowledge")**: everyone is dropped in the same area. Players may remember real labels they saw while picking their own places, which makes spotting fakes easier. The bluff gets harder, and so does picking a place to rename. You might pick one you think nobody else noticed.
  - Possible extra rule: you can't pick a place someone else has already picked, or picks are shown to everyone afterwards.
  - Memory is part of the game here, so the guessing view could allow more panning.
- **Separate locations ("Strangers")**: each player is dropped somewhere different, either random or from a pool of presets, so nobody has seen anyone else's area. This is the pure bluffing version and matches how the game works now.
  - The server would hand out a starting area per player when the submit phase begins.

## Bot campaign: levels and saved progress

> **Built so far:** a bot you can play against (home page, "Play against a bot") or add to any
> lobby. It comes in Easy, Normal and Hard. Bots pick real places from the map tiles, invent fakes
> from local name parts, and check the map to spot fakes (Hard checks more often).

Next, turn this into a single-player campaign:

- **Levels**: a ladder of named bot opponents (e.g. "Parish Councillor" → "County Surveyor" →
  "Ordnance Survey Grandmaster"). Each level sets:
  - how often the bot recognises fakes (`BotBrain.Knowledge`);
  - how good its own fakes are, for example Easy uses obvious ones like "Muddlecombe-on-Sea"
    while Hard copies the area's naming patterns and avoids the prefix/suffix tells;
  - which maps are available, unlocking new areas as you progress.
- **Win condition**: beat the bot over a best-of-N match (e.g. first to 5) to unlock the next level.
- **Progress saved to an account**: needs the user accounts work below. Store per user:
  - highest level beaten;
  - per-level best score and stars;
  - maps unlocked.

  This lives in Postgres: `users`, `campaign_progress(user_id, level, best_score, stars, completed_at)`.
- **Guests**: guests can play level 1; progress is offered for saving when they sign up.
- **Daily challenge** (later): everyone plays the same bot, map and seed, with a leaderboard.

## Other ideas noted so far

- User accounts with game history and stats such as "most people fooled" (Postgres).
- Check names against the OS Open Names gazetteer: confirm real picks exist, and warn when a "fake" name turns out to be real.
- Hide road, farm, POI and station labels near the renamed place during guessing, since they often contain its original name.
- A reveal animation: the fake label fades back into the real name.
