#!/usr/bin/env bash
# One-time Google Cloud setup for deploying from GitHub Actions to Cloud Run.
# Safe to re-run: existing resources are left alone.
#
#   ./scripts/gcp-setup.sh <gcp-project-id> <github-owner/repo> [region]
#
# Creates:
#   - Artifact Registry Docker repo "bluff-or-blighty"
#   - Service account "github-deployer"       (used by GitHub Actions to deploy)
#   - Service account "bluff-or-blighty-run"  (the identity the app runs as; no roles)
#   - Workload Identity Federation pool/provider "github", locked to your repo,
#     so GitHub can sign in without any JSON key
set -euo pipefail

PROJECT_ID="${1:?usage: $0 <gcp-project-id> <github-owner/repo> [region]}"
GITHUB_REPO="${2:?usage: $0 <gcp-project-id> <github-owner/repo> [region]}"
REGION="${3:-europe-west2}" # London

fail() { printf 'error: %s\n' "$*" >&2; exit 1; }

# Project IDs are lowercase letters, digits and hyphens. A display name ("BluffOrBlighty") or the
# docs placeholder makes gcloud fail with an unhelpful INVALID_ARGUMENT, so check first.
if [[ ! "$PROJECT_ID" =~ ^[a-z][a-z0-9-]{4,28}[a-z0-9]$ ]]; then
  echo "Your projects (use the PROJECT_ID column):" >&2
  gcloud projects list --format "table(projectId,name)" >&2 || true
  fail "'$PROJECT_ID' isn't a valid project ID. Use the lowercase ID, not the display name."
fi
[[ "$GITHUB_REPO" =~ ^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$ ]] ||
  fail "'$GITHUB_REPO' should look like owner/repo, e.g. your-user/BluffOrBlighty."
gcloud projects describe "$PROJECT_ID" --format 'value(projectId)' >/dev/null ||
  fail "Can't access project '$PROJECT_ID'. Check the ID and that 'gcloud auth login' used the right account."

REPOSITORY="bluff-or-blighty"
DEPLOY_SA_NAME="github-deployer"
RUNTIME_SA_NAME="bluff-or-blighty-run"
POOL="github"
PROVIDER="github"

DEPLOY_SA="${DEPLOY_SA_NAME}@${PROJECT_ID}.iam.gserviceaccount.com"
RUNTIME_SA="${RUNTIME_SA_NAME}@${PROJECT_ID}.iam.gserviceaccount.com"
PROJECT_NUMBER="$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')"
G=(--project "$PROJECT_ID" --quiet)

step() { printf '\n==> %s\n' "$*"; }

step "Enabling APIs"
gcloud services enable run.googleapis.com artifactregistry.googleapis.com \
  iam.googleapis.com iamcredentials.googleapis.com sts.googleapis.com secretmanager.googleapis.com "${G[@]}"

step "Artifact Registry repo: $REPOSITORY ($REGION)"
gcloud artifacts repositories describe "$REPOSITORY" --location "$REGION" "${G[@]}" >/dev/null 2>&1 ||
  gcloud artifacts repositories create "$REPOSITORY" --repository-format docker \
    --location "$REGION" --description "Bluff or Blighty images" "${G[@]}"

step "Service accounts"
gcloud iam service-accounts describe "$DEPLOY_SA" "${G[@]}" >/dev/null 2>&1 ||
  gcloud iam service-accounts create "$DEPLOY_SA_NAME" --display-name "GitHub Actions deployer" "${G[@]}"
gcloud iam service-accounts describe "$RUNTIME_SA" "${G[@]}" >/dev/null 2>&1 ||
  gcloud iam service-accounts create "$RUNTIME_SA_NAME" --display-name "Bluff or Blighty (Cloud Run runtime)" "${G[@]}"

step "Deployer permissions"
gcloud projects add-iam-policy-binding "$PROJECT_ID" --member "serviceAccount:$DEPLOY_SA" \
  --role roles/run.admin --condition None --quiet >/dev/null
gcloud artifacts repositories add-iam-policy-binding "$REPOSITORY" --location "$REGION" \
  --member "serviceAccount:$DEPLOY_SA" --role roles/artifactregistry.writer "${G[@]}" >/dev/null
# Lets the deployer start the service as the runtime account (and nothing else).
gcloud iam service-accounts add-iam-policy-binding "$RUNTIME_SA" \
  --member "serviceAccount:$DEPLOY_SA" --role roles/iam.serviceAccountUser "${G[@]}" >/dev/null

step "Workload Identity Federation (GitHub repo: $GITHUB_REPO)"
gcloud iam workload-identity-pools describe "$POOL" --location global "${G[@]}" >/dev/null 2>&1 ||
  gcloud iam workload-identity-pools create "$POOL" --location global --display-name "GitHub Actions" "${G[@]}"
CONDITION="assertion.repository == '${GITHUB_REPO}'"
if gcloud iam workload-identity-pools providers describe "$PROVIDER" --location global \
  --workload-identity-pool "$POOL" "${G[@]}" >/dev/null 2>&1; then
  # Re-running with a different repo (e.g. a typo the first time) moves the lock to the new repo.
  gcloud iam workload-identity-pools providers update-oidc "$PROVIDER" --location global \
    --workload-identity-pool "$POOL" --attribute-condition "$CONDITION" "${G[@]}" >/dev/null
else
  gcloud iam workload-identity-pools providers create-oidc "$PROVIDER" --location global \
    --workload-identity-pool "$POOL" --display-name "GitHub" \
    --issuer-uri "https://token.actions.githubusercontent.com" \
    --attribute-mapping "google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.ref=assertion.ref" \
    --attribute-condition "$CONDITION" "${G[@]}"
fi
gcloud iam service-accounts add-iam-policy-binding "$DEPLOY_SA" --role roles/iam.workloadIdentityUser \
  --member "principalSet://iam.googleapis.com/projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/${POOL}/attribute.repository/${GITHUB_REPO}" \
  "${G[@]}" >/dev/null

cat <<OUT

Done. Add these as GitHub repository variables (Settings → Secrets and variables → Actions → Variables):

  GCP_PROJECT_ID    = ${PROJECT_ID}
  GCP_REGION        = ${REGION}
  GCP_WIF_PROVIDER  = projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/${POOL}/providers/${PROVIDER}
  GCP_DEPLOY_SA     = ${DEPLOY_SA}

Or with the GitHub CLI:

  gh variable set GCP_PROJECT_ID   --repo ${GITHUB_REPO} --body '${PROJECT_ID}'
  gh variable set GCP_REGION       --repo ${GITHUB_REPO} --body '${REGION}'
  gh variable set GCP_WIF_PROVIDER --repo ${GITHUB_REPO} --body 'projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/${POOL}/providers/${PROVIDER}'
  gh variable set GCP_DEPLOY_SA    --repo ${GITHUB_REPO} --body '${DEPLOY_SA}'
OUT
