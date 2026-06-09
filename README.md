# Jellyboxd Sync — Jellyfin plugin

Two-way sync between your **Jellyfin** server and your **Jellyboxd** account, plus
a rating UI inside the Jellyfin web client.

- **Movies & whole series** → watched, rating and favourite sync **both ways**.
- **Seasons & episodes** → watched sync.
- A **“Ma note” rating widget** under the cast on each detail page, and a
  **rating badge** on poster cards.

## Install (as a Jellyfin plugin repository)

1. Jellyfin → **Dashboard → Plugins → Repositories → +**
   - **Name:** `Jellyboxd`
   - **URL:** `https://github.com/Lachrize/jellyfin-plugin-jellyboxd/raw/main/manifest.json`
2. **Catalog → Jellyboxd Sync → Install**, then **restart Jellyfin**.

## Configure

1. In **Jellyboxd → Paramètres → Jellyfin**, click **Générer mon token** and copy it.
2. In **Jellyfin → Dashboard → Plugins → Jellyboxd Sync**:
   - **Jellyboxd URL:** your Jellyboxd address (e.g. `https://app.jellyboxd…`)
   - **Your Jellyboxd sync token:** paste the token from step 1
3. **Save.** Your Jellyfin activity now flows to Jellyboxd, and changes you make
   in Jellyboxd flow back to your server.

> Requires Jellyfin **10.11+** (.NET 9).

## Develop

- Needs the **.NET 9 SDK** (Jellyfin 10.11 targets `net9.0`).
- `./build-release.sh` — builds, packages `dist/`, and prints the MD5 to put in `manifest.json`.
- `./deploy.sh` — builds and installs into a local Jellyfin (dev only; stops/starts the app).
