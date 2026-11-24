# CI/CD: GitHub Actions → Google Cloud Run

Everything is in [`.github/workflows/ci-cd.yml`](../.github/workflows/ci-cd.yml).

| Trigger | What runs |
|---|---|
| Pull request | Server build and tests (including Redis integration tests), client lint and build, Docker image build (not pushed) |
| Push to `main` | All of the above, then **deploy to Cloud Run** if everything passed |
| Manual (`workflow_dispatch`) | Same as a push to the branch you pick. Deploys only from `main` |

### What stops a broken commit reaching players

1. **Tests with a real Redis.** The server job starts a Redis container. `RedisIntegrationTests`
   run two server instances against it, with players connecting over WebSockets as in production:
   a join on one instance must reach a player on the other (the backplane), and a game must
   survive its server shutting down. In CI a missing Redis fails these tests; locally they skip
   unless `REDIS_TEST_CONNECTION` is set:

   ```bash
   docker compose up -d redis
   ```

   ```bash
   REDIS_TEST_CONNECTION=localhost:6379 dotnet test
   ```

2. **The new revision is tested before it gets traffic.** It's deployed with no traffic under a
   `candidate` tag, with the real production settings and secrets. If it can't start (for example
   it can't reach Redis), the deploy fails right there. Otherwise
   [`scripts/smoke-test.mjs`](../scripts/smoke-test.mjs) runs against the candidate's own URL:
   - health, including Redis being reachable;
   - the page being served;
   - two players creating and joining a game over WebSockets.

   Traffic moves to the new revision only if all of that passes; otherwise players stay on the
   previous one. To try the smoke test yourself: `node scripts/smoke-test.mjs <url> [redis|memory]`.

The app ships as one container: the [`Dockerfile`](../Dockerfile) builds the React client and
copies it into the .NET API's `wwwroot`. The API then serves the page, `/api/*` and the SignalR hub
from the same origin.

## One-time setup

1. **Create a GitHub repo and push to it.**
2. **Create or choose a Google Cloud project** with billing enabled. Then sign in locally:
   ```bash
   gcloud auth login
   ```
3. **Run the setup script** (safe to run again):
   ```bash
   ./scripts/gcp-setup.sh YOUR_PROJECT_ID your-github-user/BluffOrBlighty europe-west2
   ```
   It enables the APIs and creates:
   - an Artifact Registry repo;
   - a deployer service account, which can deploy Cloud Run and push images, and nothing else;
   - a runtime service account for the app, which has no roles;
   - a Workload Identity Federation provider. GitHub Actions signs in with short-lived OIDC
     tokens instead of a JSON key, and only workflows from **your repo** can use it.
4. **Add the four repository variables** the script prints (`GCP_PROJECT_ID`, `GCP_REGION`,
   `GCP_WIF_PROVIDER`, `GCP_DEPLOY_SA`) under *Settings → Secrets and variables → Actions → Variables*.
   None of them are secret.
5. *(Optional)* Add a **`production` environment** under *Settings → Environments* with required
   reviewers if you want to approve each deploy by hand. The deploy job already targets that environment.
6. *(Optional)* Set a repository variable `VITE_MAP_STYLE_URL` to use a different map style.

Push to `main`. The run summary links to the Cloud Run URL, and the last step checks `/api/health`.

## Cloud Run settings, and why

| Flag | Why |
|---|---|
| `--max-instances=1` | Without Redis, games are held in memory and a second instance would split them up. With Redis this can be raised: the Redis backplane links instances. |
| `--session-affinity` | Keeps a browser on the same instance (matters if instances are ever increased). |
| `--timeout=3600` | SignalR uses a long-lived WebSocket, and Cloud Run closes requests at the timeout. Clients reconnect and rejoin automatically. |
| `--min-instances=0` | Costs nothing when nobody is playing. The first visitor after a quiet spell waits a few seconds for a cold start. |
| `--allow-unauthenticated` | It's a public website. |

## Games that survive deploys (Redis)

Without Redis, games live in the server's memory, and a deploy or a scale-to-zero ends any game in
progress. With Redis configured:

- **Game state** is saved to Redis after every move (`bluff:game:{code}`, holding `{version}|{json}`,
  expiring after 6 hours idle). Saves are optimistic: a save from an out-of-date copy is rejected and retried,
  so two instances can't overwrite each other while a deploy overlaps old and new revisions.
- **SignalR's Redis backplane** delivers messages to players connected to the other instance.
- **Light on Redis**, since hosted Redis bills per command (Upstash counts each call inside a script
  too). Each instance keeps the games it's serving in memory and only reads Redis after a restart or
  when another instance has saved since. A move is one save, 3 commands. A typical 3-player game
  measured 68 commands in total (it was 118 before this change), and an idle instance pings about
  once a minute. Upstash's free 500,000 commands a month is roughly 7,000 such games.
- **On shutdown** the server doesn't mark its dropped connections as "away"; those players are
  about to reconnect. The client retries for up to 2 minutes, then rejoins with its saved token on
  whichever instance it reaches.

### Setting it up

1. **Get a Redis.** It needs Lua scripting (`EVAL`) and pub/sub, which most hosted Redis services
   support. [Upstash](https://upstash.com) has a free tier and a London (eu-west-2) region; Google's
   Memorystore works too but has a monthly minimum. Copy the connection string, in either form:

   ```text
   rediss://default:YOUR_PASSWORD@your-db.upstash.io:6379
   your-db.upstash.io:6379,password=YOUR_PASSWORD,ssl=True
   ```

   (`rediss://` with two s's means TLS, which Upstash requires.)

2. **Store it in Secret Manager** (prompts for the string without echoing it, and gives the app's
   service account read access):

   ```bash
   ./scripts/redis-secret.sh YOUR_PROJECT_ID
   ```

   At the hidden prompt, paste the connection string itself. The script rejects anything that
   doesn't look like one, and prints the host it stored (never the password).

3. **Set the GitHub repository variable** `REDIS_SECRET` to the secret name (`redis-connection` by
   default) and push. The deploy adds `--set-secrets=Redis__ConnectionString=…`, and the smoke test
   fails unless `/api/health` reports `"store":"redis"`.

The stored state includes each player's rejoin token, so keep the Redis private (password + TLS).

**If Redis is misconfigured or unreachable** the app refuses to start (after 15 seconds of trying),
so Cloud Run rejects the new revision and keeps the previous one serving. The deploy job fails
with "Can't connect to Redis at …" in the Cloud Run logs. `/api/health` also returns 503 if Redis
drops while running.

### Trying it locally

```bash
docker compose up -d redis
```

```bash
Redis__ConnectionString=localhost:6379 dotnet run --project server/BluffOrBlighty.Api
```

`/api/health` shows which store is in use. Without the variable, games are kept in memory.

## Later: Postgres

When the database is needed, use Cloud SQL for PostgreSQL and connect with the Cloud SQL connector.
Add `--add-cloudsql-instances=PROJECT:REGION:INSTANCE` to the deploy flags and give the runtime
service account `roles/cloudsql.client`. Keep the connection password in Secret Manager
(`--set-secrets`).
