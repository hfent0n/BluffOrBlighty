#!/usr/bin/env bash
# Stores the Redis connection string in Secret Manager and lets the app's service account read it.
# Safe to re-run: it adds a new version of the secret each time.
#
#   ./scripts/redis-secret.sh <gcp-project-id> [secret-name]
#
# You'll be prompted for the connection string (it isn't echoed or saved in your shell history).
# Paste the connection string itself (not this command). Either form works:
#   rediss://default:YOUR_PASSWORD@your-db.upstash.io:6379        (Upstash's "Redis URL"; rediss = TLS)
#   your-db.upstash.io:6379,password=YOUR_PASSWORD,ssl=True       (StackExchange.Redis format)
#
# Then set the GitHub repository variable REDIS_SECRET to the secret name and push.
set -euo pipefail

PROJECT_ID="${1:?usage: $0 <gcp-project-id> [secret-name]}"
SECRET="${2:-redis-connection}"
RUNTIME_SA="bluff-or-blighty-run@${PROJECT_ID}.iam.gserviceaccount.com"
G=(--project "$PROJECT_ID" --quiet)

read -r -s -p "Redis connection string: " CONN
echo
CONN="$(printf '%s' "$CONN" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
[[ -n "$CONN" ]] || { echo "error: empty connection string" >&2; exit 1; }

# Catch the easy mistakes before storing anything (the app also refuses to start with a bad one).
# The value itself is never printed.
if [[ "$CONN" =~ ^rediss?://([^@/]*@)?([A-Za-z0-9.-]+)(:([0-9]+))?/?$ ]]; then
  HOST="${BASH_REMATCH[2]}:${BASH_REMATCH[4]:-6379}"
  [[ "$CONN" == rediss://* ]] && HOST="$HOST (TLS)" || HOST="$HOST (no TLS)"
elif [[ "$CONN" =~ ^([A-Za-z0-9.-]+):([0-9]+)(,[^[:space:]]*)?$ ]]; then
  HOST="${BASH_REMATCH[1]}:${BASH_REMATCH[2]}"
  [[ "$CONN" == *ssl=[Tt]rue* ]] && HOST="$HOST (TLS)" || HOST="$HOST (no TLS: add ,ssl=True for Upstash)"
else
  echo "error: that doesn't look like a Redis connection string (was a command pasted by mistake?)." >&2
  echo "Expected  rediss://default:PASSWORD@host:6379  or  host:6379,password=PASSWORD,ssl=True" >&2
  exit 1
fi
echo "Redis host: $HOST"

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
