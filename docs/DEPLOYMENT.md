# CI/CD: GitHub Actions → Google Cloud Run

Everything is in [`.github/workflows/ci-cd.yml`](../.github/workflows/ci-cd.yml).

| Trigger | What runs |
|---|---|
| Pull request | Server build and tests, client lint and build, Docker image build (not pushed) |
| Push to `main` | All of the above, then **deploy to Cloud Run** if everything passed |
| Manual (`workflow_dispatch`) | Same as a push to the branch you pick. Deploys only from `main` |

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
| `--max-instances=1` | Games are held in memory. A second instance would split players across separate game stores. |
| `--session-affinity` | Keeps a browser on the same instance (matters if instances are ever increased). |
| `--timeout=3600` | SignalR uses a long-lived WebSocket, and Cloud Run closes requests at the timeout. Clients reconnect and rejoin automatically. |
| `--min-instances=0` | Costs nothing when nobody is playing. The first visitor after a quiet spell waits a few seconds for a cold start. |
| `--allow-unauthenticated` | It's a public website. |

**Known limitation:** deploying, or Cloud Run scaling to zero, restarts the instance, and that
ends any game in progress. To fix this properly, move game state to Redis (Memorystore) or Postgres
and add a SignalR backplane. Then `--max-instances` can be raised. This fits with the accounts
work planned in [IDEAS.md](IDEAS.md).

## Later: Postgres

When the database is needed, use Cloud SQL for PostgreSQL and connect with the Cloud SQL connector.
Add `--add-cloudsql-instances=PROJECT:REGION:INSTANCE` to the deploy flags and give the runtime
service account `roles/cloudsql.client`. Keep the connection password in Secret Manager
(`--set-secrets`).
