#!/usr/bin/env bash
# Stores the Redis connection string in Secret Manager and lets the app's service account read it.
# Safe to re-run: it adds a new version of the secret each time.
#
#   ./scripts/redis-secret.sh <gcp-project-id> [secret-name]
#
# You'll be prompted for the connection string (it isn't echoed or saved in your shell history).
# StackExchange.Redis format, e.g. for Upstash:
#   your-db.upstash.io:6379,password=YOUR_PASSWORD,ssl=True,abortConnect=False
#
# Then set the GitHub repository variable REDIS_SECRET to the secret name and push.
set -euo pipefail

PROJECT_ID="${1:?usage: $0 <gcp-project-id> [secret-name]}"
SECRET="${2:-redis-connection}"
RUNTIME_SA="bluff-or-blighty-run@${PROJECT_ID}.iam.gserviceaccount.com"
G=(--project "$PROJECT_ID" --quiet)

read -r -s -p "Redis connection string: " CONN
echo
[[ -n "$CONN" ]] || { echo "error: empty connection string" >&2; exit 1; }

gcloud services enable secretmanager.googleapis.com "${G[@]}"

if ! gcloud secrets describe "$SECRET" "${G[@]}" >/dev/null 2>&1; then
  gcloud secrets create "$SECRET" --replication-policy automatic "${G[@]}"
fi
printf '%s' "$CONN" | gcloud secrets versions add "$SECRET" --data-file=- "${G[@]}"

gcloud secrets add-iam-policy-binding "$SECRET" \
  --member "serviceAccount:$RUNTIME_SA" --role roles/secretmanager.secretAccessor "${G[@]}" >/dev/null

cat <<OUT

Done. Now set the GitHub repository variable:

  REDIS_SECRET = ${SECRET}

The next deploy will use Redis; its health check fails if the app isn't on Redis.
OUT
