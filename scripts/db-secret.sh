#!/usr/bin/env bash
# Stores the Postgres connection string in Secret Manager for the app (see docs/DEPLOYMENT.md).
#
#   ./scripts/db-secret.sh <gcp-project-id> [secret-name]
#
# Npgsql format, e.g. for Neon (use the pooled host, ending -pooler):
#   Host=ep-xxx-pooler.eu-west-2.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=YOUR_PASSWORD;SSL Mode=Require
#
# Then set the GitHub repository variable DB_SECRET to the secret name and push.
set -euo pipefail
PROJECT_ID="${1:?usage: $0 <gcp-project-id> [secret-name]}"
SECRET="${2:-db-connection}"
"$(dirname "$0")/set-secret.sh" "$PROJECT_ID" "$SECRET" "Postgres connection string"
echo
echo "Now set the GitHub repository variable DB_SECRET = ${SECRET}, then push or re-run the workflow."
