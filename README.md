# Planning Poker — Home Assistant Add-on

Real-time collaborative estimation tool for agile teams, running as a Home Assistant add-on.

## Features

- **Real-time voting** — powered by SignalR WebSockets, all updates are instant
- **5 estimation scales** — Fibonacci, T-Shirt, Powers of 2, Sequential, Risk
- **Up to 18 players** per room
- **No sign-up required** — share a room code or link and start estimating
- **Owner controls** — reveal cards, re-vote, accept estimates, advance to next question
- **Post-reveal voting** — participants can change their vote after cards are revealed
- **Consensus & average** — automatic calculation after reveal, with smart pre-selection in accept dropdown
- **Coffee break card** — optional break voting so players can signal when they need a pause
- **Session timer** — optional time limit that auto-calculates time per card
- **"Thinking" indicator** — player cards wobble when someone is hovering over their voting options
- **Export results** — download estimation results as CSV or JSON
- **Mobile-friendly** — responsive UI that works on phones and tablets

## How It Works

1. **Create a room** — pick a scale, paste your questions (one per line), and hit Continue
2. **Share the link** — send the room code or invite URL to your team
3. **Vote** — everyone picks an estimate card; cards stay hidden until the owner reveals
4. **Reveal & discuss** — see all votes, consensus, and average; re-vote if needed
5. **Accept & continue** — lock in the estimate and move to the next question
6. **Export** — after all questions are estimated, download results as CSV/JSON

## Installation on Home Assistant

### 1. Add the repository

1. Open **Home Assistant** → **Settings** → **Add-ons** → **Add-on Store**
2. Click **Menu (...)** → **Repositories**
3. Add: `https://github.com/MaxMinsk/PokerPlanning`
4. Click **Add** → **Close**

### 2. Install the add-on

1. Refresh the Add-on Store page
2. Find **Planning Poker** in the list
3. Click **Install**
4. Click **Start**
5. Check the **Log** tab — you should see `Now listening on: http://0.0.0.0:5000`

### 3. Access the app

- **Local network:** `http://<your-ha-ip>:5000`
- **External access:** configure a Cloudflare Tunnel or reverse proxy to expose port 5000 on a subdomain

## External Access via Cloudflare Tunnel

If you already have Cloudflare Tunnel set up for Home Assistant:

1. Go to **Cloudflare Zero Trust** → **Networks** → **Tunnels** → your tunnel → **Configure**
2. Add a **Public Hostname**:
   - **Subdomain:** `planningpoker` (or your choice)
   - **Domain:** your domain
   - **Type:** HTTP
   - **URL:** `localhost:5000`
3. Ensure **WebSockets** are enabled (Cloudflare Dashboard → your domain → Network)

## Tech Stack

- **.NET 9** — ASP.NET Core backend
- **SignalR** — real-time WebSocket communication
- **Vanilla JS** — lightweight frontend, no framework dependencies
- **Docker** — multi-stage build (Alpine-based)
- **GitHub Actions** — CI/CD pipeline, auto-builds and pushes to GHCR

## Architecture

```
Browser ←──SignalR WebSocket──→ ASP.NET Core (.NET 9)
                                  ├── PokerHub (SignalR Hub)
                                  ├── RoomService (in-memory state)
                                  └── wwwroot/ (static SPA)
```

Rooms are stored in-memory — they do not survive add-on restarts. This is by design for a lightweight prototype.

## Development

```bash
# Clone
git clone https://github.com/MaxMinsk/PokerPlanning.git
cd PokerPlanning/src/PokerPlanning

# Run locally
dotnet run

# Open http://localhost:5000
```

## Updating

1. The add-on version is managed in `planning-poker/config.yaml`
2. On every push to `main`, GitHub Actions builds a new Docker image and pushes it to GHCR
3. In Home Assistant: **Settings** → **Add-ons** → **Planning Poker** → **Update**

## License

MIT
