# Pacman GPS - Real-World Multiplayer Pacman Game

## Context
Et multiplayer GPS-baseret Pacman-spil hvor prikker placeres automatisk på rigtige veje/stier via OpenStreetMap-data. Spillere går rundt med deres telefon og samler prikker op ved at gå forbi dem. Bygger videre på erfaring og patterns fra paaskejagt-projektet (`C:/Projects/paaskejagt`).

---

## Tech Stack (samme som paaskejagt)
- **Backend**: ASP.NET Core 10.0 (minimal APIs)
- **Frontend**: Razor Pages + Vanilla JavaScript (inline, som paaskejagt)
- **Real-time**: SignalR WebSockets med auto-reconnect
- **Kort**: Leaflet.js med CartoDB Dark Matter tiles
- **Database**: JSON-filer (en per spil) + in-memory state
- **Kort-data**: OpenStreetMap Overpass API
- **Geo-beregninger**: Custom C# (Haversine + interpolation langs veje)
- **PWA**: manifest + service worker (som paaskejagt)
- **Deployment**: Docker → Synology NAS via Watchtower + Cloudflare

---

## Game Design

### Prik-generering
1. Admin sætter centrum på kort (klik) + radius (slider/input)
2. Server kalder Overpass API: henter alle veje/stier/fortove inden for radius
3. Server interpolerer punkter langs vej-geometrier med 15m mellemrum
4. De-duplikering af prikker der er for tætte (< 5m, ved vejkryds)
5. ~2-3% af prikker udpeges som power pellets
6. Alt gemmes i spillets JSON-fil

### Multi-game
- Hvert spil har unikt 6-tegns alfanumerisk ID
- Spillere joiner via QR-kode eller indtaster game code
- SignalR Groups isolerer spil fra hinanden
- Admin kan oprette og styre mange spil samtidig
- Admin vælger hvilket spil de vil styre fra en liste

### Synlighed
- Alle prikker synlige fra start (ingen fog-of-war)

### Opsamling
- Automatisk: gå inden for 10m af en prik = den spises
- Server-side validering (klienten sender position, serveren tjekker afstand)
- Hver normal prik = 10 point

### Slut-betingelse (admin vælger)
- **Tidsgrænse**: Admin sætter tid (f.eks. 30/60/90 min)
- **Alle prikker spist**: Spillet slutter når alt er samlet
- **Manuelt stop**: Admin trykker stop når de vil

### Teams (valgfrit)
- Admin vælger om spillet er individuelt eller team-baseret
- Ved teams: 2-4 hold med Pacman-farver (gul, pink, cyan, orange)
- Teams deler samlet score
- Teammedlemmer kan se hinandens positioner tydeligt

---

## Power-ups (4 typer, alle aktiveret)

Power pellets placeres på kortet som større pulserende prikker. **Hver type har sin egen farve, og pellets skifter farve/type med jævne mellemrum** (f.eks. hvert 20. sekund cykler de mellem typerne - så man kan vente på den type man vil have).

| Type | Farve | Effekt | Varighed |
|------|-------|--------|----------|
| **Score Multiplier** | Gul/guld | 2x point på alle prikker | 3 min |
| **Magnet** | Grøn | Dobbelt opsamlingsradius (20m) | 3 min |
| **Ghost Mode** | Blå | Usynlig for andre spillere på kortet | 2 min |
| **Stjæl-point** | Rød | Når du er inden for 15m af en anden spiller, stjæler du 10% af deres point | 2 min |

Farvecyklus-animation på power pellets skaber taktisk gameplay: "Skal jeg tage den nu, eller vente til den skifter til den type jeg vil have?"

---

## Bonus Items (klassiske Pacman-frugter)

Spawner periodisk på tilfældige vej-positioner under spillet. Forsvinder efter 60 sek.

| Frugt | Point | Spawn-frekvens |
|-------|-------|----------------|
| Cherry | 100 | Hyppig |
| Strawberry | 300 | Normal |
| Orange | 500 | Sjælden |
| Apple | 700 | Meget sjælden |
| Melon | 1000 | Ultra sjælden |

Server-side `BonusSpawnerService` (IHostedService) styrer spawning med timer.

---

## AI-spøgelser (valgfrit, admin aktiverer)

- Virtuelle spøgelser der bevæger sig langs veje på kortet
- 4 spøgelser med klassiske Pacman-farver: Blinky (rød), Pinky (pink), Inky (cyan), Clyde (orange)
- Bevæger sig langs vej-geometri med variabel hastighed
- Vises for alle spillere på kortet i real-time

### Spøgelse-adfærd
- Følger vej-netværket (kan ikke gå udenfor veje)
- Skifter retning ved kryds (semi-random, med tendens mod nærmeste spiller)
- Server-side simulation via background service

### Fange-effekt (admin vælger)
- **Mist point**: Spilleren mister 20% af sine point
- **Frys i 30 sek**: Opsamling deaktiveres i 30 sekunder
- **Begge dele**: Mist 10% point + fryses i 20 sek

### Spise spøgelser
- Når en spiller har "Stjæl-point" power-up aktiv, kan de også "spise" spøgelser
- At spise et spøgelse giver 200 bonus-point
- Spøgelset respawner efter 30 sek på en tilfældig position

---

## Scoreboard
- Real-time via SignalR
- Viser alle spillere (eller teams) sorteret efter point
- Medals for top 3
- Viser: point, antal prikker spist, power-ups brugt, bonusser samlet
- Tilgængelig både som overlay i spillet og som standalone side

---

## Visuelt Tema

### Kort
- CartoDB Dark Matter tiles (sort/mørkegrå baggrund = Pacman-maze feel)
- `L.map('map', { preferCanvas: true })` for performance med tusindvis af prikker

### Prikker
- Normale prikker: små gule `CircleMarker` (8px)
- Power pellets: store pulserende cirkler (16px) med farveskift-animation
- Samlede prikker forsvinder med kort fade-animation

### Spillere
- Pacman-formede div markers med CSS (border-radius trick eller SVG)
- Mund-animation (åbne/lukke) når spilleren bevæger sig
- Retning baseret på device orientation / bevægelsesretning
- Hver spiller/team har unik farve

### Spøgelser
- Klassiske Pacman-spøgelse SVG/CSS ikoner
- Blinky=rød, Pinky=pink, Inky=cyan, Clyde=orange
- Når "spiselige" (power-up aktiv): blå, blinkende

### UI
- "Press Start 2P" retro font
- Mørk baggrund med neon-farver
- Score-display i header
- Aktive power-ups med countdown-timer i HUD
- Subtil CRT scanline effekt

### Lyd (Web Audio API, som paaskejagt)
- "Waka waka" ved prik-opsamling
- Power-up jingle
- Frugt-opsamlings-lyd
- Spøgelse-spist lyd
- Game start siren
- Game over fanfare

---

## Filstruktur

```
C:\Projects\claude\pacman\
  pacman.csproj
  Program.cs                          # API endpoints + DI setup

  Hubs/
    PacmanHub.cs                      # SignalR hub med game groups

  Services/
    GameManagerService.cs             # Game CRUD, state, file I/O, dot collection
    OverpassService.cs                # Overpass API client (hent veje)
    DotGeneratorService.cs            # Vej-data → prik-placering med 15m interval
    BonusSpawnerService.cs            # IHostedService: tidsbaseret bonus-spawning
    GhostService.cs                   # IHostedService: AI-spøgelse simulation
    GeoMathService.cs                 # Haversine, bearing, interpolateAlong

  Models/
    GameState.cs                      # Komplet spiltilstand
    Dot.cs                            # Prik (position, collected, collectedBy)
    Player.cs                         # Spiller (navn, score, position, powerups, team)
    PowerUp.cs                        # Power pellet (type, position, farve-cyklus)
    BonusItem.cs                      # Frugt-bonus (type, point, spawn/expiry)
    Ghost.cs                          # AI-spøgelse (position, retning, hastighed)
    GameSettings.cs                   # Admin-konfigurerbare indstillinger
    Team.cs                           # Hold (navn, farve, spillere)

  Pages/
    Index.cshtml                      # Landing page: join spil
    Join.cshtml                       # Enter game code / QR scan / navn
    Play.cshtml                       # Hoved-spilvisning
    Admin.cshtml                      # Admin: opret/styr spil
    Scoreboard.cshtml                 # Standalone scoreboard

  wwwroot/
    manifest.json                     # PWA manifest
    sw.js                             # Service worker
    favicon.svg                       # Pacman ikon
    css/
      pacman-theme.css                # Delt CSS (font, farver)
    lib/
      leaflet/leaflet.js + leaflet.css
      microsoft-signalr/signalr.min.js

  data/                               # Docker volume mount
    games.json                        # Game index (alle spil)
    games/                            # Per-game state
      {gameId}.json

  Dockerfile
  docker-compose.yml
  docker-compose.synology.yml
  .github/workflows/docker-publish.yml
  .gitignore
  appsettings.json
  appsettings.Production.json
```

---

## API Endpoints

### Public
- `GET /api/games/{id}` - Spil-metadata
- `GET /api/games/{id}/dots` - Alle prikker (initial load)
- `GET /api/games/{id}/scoreboard` - Scoreboard
- `POST /api/games/{id}/join` - Tilmeld spiller `{name, team?}`
- `POST /api/games/{id}/collect` - Manuel collection trigger (backup)
- `POST /api/games/{id}/heartbeat` - Spiller heartbeat `{name}`

### Admin (kræver `X-Admin-Password` header)
- `POST /api/admin/login` - Valider password
- `GET /api/admin/games` - Liste alle spil
- `POST /api/admin/games` - Opret nyt spil `{name, centerLat, centerLng, radius, settings}`
- `POST /api/admin/games/{id}/start` - Start spil
- `POST /api/admin/games/{id}/stop` - Stop spil
- `POST /api/admin/games/{id}/reset` - Nulstil spil
- `DELETE /api/admin/games/{id}` - Slet spil
- `PUT /api/admin/games/{id}/settings` - Opdater indstillinger

### SignalR Events (server → client)
- `PlayerJoined`, `PlayerLeft`, `PlayerMoved`
- `DotsCollected`, `PowerUpCollected`, `PowerUpExpired`
- `BonusSpawned`, `BonusDespawned`, `BonusCollected`
- `GhostMoved`, `GhostCaughtPlayer`, `GhostEaten`
- `GameStarted`, `GameEnded`, `ScoreboardUpdate`

---

## Implementerings-rækkefølge

### Fase 1: Fundament
1. Projekt-skelet: `pacman.csproj`, `Program.cs` med minimal setup, `appsettings.json`
2. Models: Alle POCO klasser
3. `GeoMathService`: Haversine, bearing, interpolateAlong (ren matematik)

### Fase 2: Prik-generering
4. `OverpassService`: HTTP client til Overpass API
5. `DotGeneratorService`: Vej-data → prikker med 15m interval + power-up placering
6. Test med en kendt lokation (f.eks. Rådhuspladsen)

### Fase 3: Game Engine
7. `GameManagerService`: CRUD, fil-I/O, per-game locking, dot collection, scoring
8. `PacmanHub`: SignalR hub med game groups
9. API endpoints i Program.cs

### Fase 4: Frontend - Join Flow
10. `Index.cshtml`: Landing page med Pacman-tema
11. `Join.cshtml`: Game code input, QR scan, navn-indtastning

### Fase 5: Frontend - Spilvisning
12. `Play.cshtml`: Leaflet kort med dark tiles
13. Prik-rendering (CircleMarkers, power pellets med animation)
14. GPS tracking + automatisk opsamling
15. Multiplayer: andre spillere som Pacman-figurer
16. Power-up HUD med countdown
17. Scoreboard overlay

### Fase 6: Admin
18. `Admin.cshtml`: Login, spilliste, opret spil (kort med centrum+radius)
19. QR-kode generering for hvert spil
20. Game dashboard: start/stop/reset, live oversigt

### Fase 7: Bonus & Spøgelser
21. `BonusSpawnerService`: Periodisk frugt-spawning
22. `GhostService`: AI-spøgelse simulation langs veje
23. Frontend: spøgelse-rendering, fange/spise mekanik

### Fase 8: Polish & Deploy
24. Lyd-effekter (Web Audio)
25. Animationer (prik-fade, power-up glow, confetti)
26. PWA manifest + service worker
27. Docker + deployment pipeline
28. Test på rigtig mobil
