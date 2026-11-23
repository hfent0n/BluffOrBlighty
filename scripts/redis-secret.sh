#!/usr/bin/env bash
# Stores the Redis connection string in Secret Manager for the app (see docs/DEPLOYMENT.md).
#
#   ./scripts/redis-secret.sh <gcp-project-id> [secret-name]
#
# StackExchange.Redis format, e.g. for Upstash:
#   your-db.upstash.io:6379,password=YOUR_PASSWORD,ssl=True,abortConnect=False
#
# Then set the GitHub repository variable REDIS_SECRET to the secret name and push.
set -euo pipefail
PROJECT_ID="${1:?usage: $0 <gcp-project-id> [secret-name]}"
SECRET="${2:-redis-connection}"
"$(dirname "$0")/set-secret.sh" "$PROJECT_ID" "$SECRET" "Redis connection string"
echo
echo "Now set the GitHub repository variable REDIS_SECRET = ${SECRET}, then push or re-run the workflow."
