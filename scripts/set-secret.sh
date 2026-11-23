#!/usr/bin/env bash
# Stores a value in Secret Manager and lets the app's service account read it.
# Safe to re-run: it adds a new version of the secret each time.
#
#   ./scripts/set-secret.sh <gcp-project-id> <secret-name> "<what to paste>"
#
# You'll be prompted for the value (it isn't echoed or saved in your shell history).
# Used by redis-secret.sh and db-secret.sh.
set -euo pipefail

PROJECT_ID="${1:?usage: $0 <gcp-project-id> <secret-name> [prompt]}"
SECRET="${2:?usage: $0 <gcp-project-id> <secret-name> [prompt]}"
PROMPT="${3:-Secret value}"
RUNTIME_SA="bluff-or-blighty-run@${PROJECT_ID}.iam.gserviceaccount.com"
G=(--project "$PROJECT_ID" --quiet)

read -r -s -p "${PROMPT}: " VALUE
echo
[[ -n "$VALUE" ]] || { echo "error: empty value" >&2; exit 1; }

gcloud services enable secretmanager.googleapis.com "${G[@]}"

if ! gcloud secrets describe "$SECRET" "${G[@]}" >/dev/null 2>&1; then
  gcloud secrets create "$SECRET" --replication-policy automatic "${G[@]}"
fi
printf '%s' "$VALUE" | gcloud secrets versions add "$SECRET" --data-file=- "${G[@]}"

gcloud secrets add-iam-policy-binding "$SECRET" \
  --member "serviceAccount:$RUNTIME_SA" --role roles/secretmanager.secretAccessor "${G[@]}" >/dev/null

echo "Stored secret '${SECRET}' (readable by ${RUNTIME_SA})."
