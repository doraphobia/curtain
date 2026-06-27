# Sync-In Workflow (Manual Default + Optional Auto)

This repository is the primary source of truth for the importer.

Consumer repositories can contribute updates in two modes:

1. Manual trigger in this repo (default).
2. Optional auto trigger via `repository_dispatch` (only after explicit secure setup).

## Workflow Entry

- `.github/workflows/receive-consumer-sync.yml`
- Supported triggers:
  - `workflow_dispatch` (always enabled)
  - `repository_dispatch` with type `figma-importer-sync` (disabled by default)

## Manual Mode (Default)

1. In `RedHong01/Figma-Importer`, open Actions.
2. Run `Consumer Sync Proposal`.
3. Fill:
   - `source_repo` (example: `RedHong01/AltControl2_TeamC_MushroomGame`)
   - `source_ref` (example: `main`, `feature/xyz`, tag, or SHA)
   - `package_path` (example: `Packages/com.redhong01.figma_to_unity_importer`)
   - `reason` (optional)
4. Workflow copies package content into this repo and opens a PR.
5. Review and merge manually.

## Optional Auto Mode (Disabled by Default)

Set these in `RedHong01/Figma-Importer`:

1. Repository variable `ENABLE_AUTO_SYNC_DISPATCH=true`.
2. Repository secret `DISPATCH_SHARED_SECRET` (required in auto mode).
   - Incoming payload must include matching `dispatch_secret`.
3. Optional repository variable `ALLOWED_SYNC_SOURCE_REPOS` as comma-separated `owner/repo`.
   - If omitted, any source repo is accepted as long as dispatch token + shared secret are valid.
   - Example: `RedHong01/AltControl2_TeamC_MushroomGame,YourOrg/AnotherGameRepo`
4. Optional secret `SYNC_SOURCE_TOKEN` with read access when source repos are private.

Without step 1, auto dispatch requests are rejected.

## Consumer Repo Auto Trigger Example

Consumer repo can send:

```yaml
name: Propose Figma Importer Sync

on:
  workflow_dispatch:

jobs:
  dispatch:
    runs-on: ubuntu-latest
    steps:
      - name: Send dispatch to Figma-Importer
        env:
          TARGET_OWNER: RedHong01
          TARGET_REPO: Figma-Importer
          DISPATCH_TOKEN: ${{ secrets.FIGMA_IMPORTER_SYNC_TOKEN }}
          DISPATCH_SHARED_SECRET: ${{ secrets.FIGMA_IMPORTER_DISPATCH_SHARED_SECRET }}
          SOURCE_REPO: ${{ github.repository }}
          SOURCE_REF: ${{ github.ref_name }}
          SOURCE_SHA: ${{ github.sha }}
        run: |
          set -euo pipefail

          payload="$(jq -n \
            --arg source_repo "$SOURCE_REPO" \
            --arg source_ref "$SOURCE_REF" \
            --arg source_sha "$SOURCE_SHA" \
            --arg package_path "Packages/com.redhong01.figma_to_unity_importer" \
            --arg reason "Auto sync proposal from consumer repo" \
            --arg dispatch_secret "$DISPATCH_SHARED_SECRET" \
            '{event_type:"figma-importer-sync", client_payload:{source_repo:$source_repo, source_ref:$source_ref, source_sha:$source_sha, package_path:$package_path, reason:$reason, dispatch_secret:$dispatch_secret}}')"

          curl -fsSL -X POST \
            -H "Accept: application/vnd.github+json" \
            -H "Authorization: Bearer ${DISPATCH_TOKEN}" \
            "https://api.github.com/repos/${TARGET_OWNER}/${TARGET_REPO}/dispatches" \
            -d "$payload"
```

Required consumer secret:

- `FIGMA_IMPORTER_SYNC_TOKEN` with permission to call dispatch API on `RedHong01/Figma-Importer`.

Recommended consumer secret:

- `FIGMA_IMPORTER_DISPATCH_SHARED_SECRET` matching target repo `DISPATCH_SHARED_SECRET`.

## Governance Recommendation

1. Keep branch protection and required reviews on `main`.
2. Treat every sync as a PR proposal, never direct merge to `main`.
3. Bump `package.json` version only when you choose to release.
