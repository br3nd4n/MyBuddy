# Real-time Collaborative 3D Geospatial Analytics Platform — Demo Scaffold

One‑line summary
A production‑oriented scaffold that demonstrates a Rust → WebAssembly 3D renderer hosted by an ASP.NET Core server with Redis‑backed distributed synchronization, Dockerized CI/CD, and Playwright end‑to‑end tests.

Why this repo
- Shows a full‑stack, performance‑focused pipeline: native WASM client (Rust), WebGL 3D renderer, real‑time sync, and scalable server patterns.
- Demonstrates distributed synchronization using Redis pub/sub and persisted room state for multi‑instance deployments.
- Includes reproducible builds, Docker multi‑stage images, GitHub Actions CI/CD, and automated E2E tests.

Repository layout
- wasm-client/
  - Cargo.toml — Rust package for the WASM client
  - src/lib.rs — Rust → WASM renderer entrypoint (interactive cube demo)
  - static/ — static assets (index.html, bootstrap.js, generated wasm pkg)
- server/
  - RealTimeServer.csproj — ASP.NET Core project
  - Program.cs — server app: serves static files, /ws WebSocket sync endpoint, Redis integration
  - Dockerfile — multi‑stage image (build WASM, publish .NET, embed static)
- e2e/
  - Playwright tests + config that exercise page load and distributed sync
- .github/workflows/
  - ci.yml — CI: build WASM and .NET, upload artifacts
  - cd.yml — CD: build/push Docker image (GHCR) and optional SSH deploy
  - e2e.yml — E2E: run Playwright tests against docker-compose stack
- docker-compose.yml — local orchestration (server + redis)
- architecture.mmd — Mermaid architecture diagram
- .dockerignore — reduce docker context size

Quickstart (local)
1. Build & run with Docker Compose (recommended):
   - docker compose up --build
   - Open http://localhost:5000
   - WebSocket endpoint: ws://localhost:5000/ws
2. Build WASM manually (iterating on client):
   - Install wasm-pack: https://rustwasm.github.io/wasm-pack/installer/
   - cd wasm-client
   - wasm-pack build --release --target web --out-dir ./static/pkg
   - Serve the server (see docker compose) or run the ASP.NET app from Visual Studio / CLI.

Run the server locally (dotnet CLI)
- From repository root:
  - dotnet build server/RealTimeServer.csproj
  - dotnet run --project server/RealTimeServer.csproj
- In Visual Studio, use __Debug > Start Debugging__ (or attach to the running browser if debugging WASM).

Testing
- Unit / integration: add tests to each project and wire into CI.
- End‑to‑end (Playwright):
  - docker compose up -d --build
  - cd e2e && npm ci
  - npx playwright install --with-deps
  - npm run test:e2e
- CI automatically runs builds and E2E via the workflows in __.github/workflows__.

CI / CD
- CI workflow (__.github/workflows/ci.yml__) builds the WASM and .NET projects and uploads artifacts.
- CD workflow (__.github/workflows/cd.yml__) builds the Docker image and pushes to GHCR on pushes to __main__. Optional SSH deploy uses repo secrets (__SSH_HOST__, __SSH_USER__, __SSH_KEY__).
- E2E workflow (__.github/workflows/e2e.yml__) boots docker-compose and runs Playwright tests.

Architecture & extension points
- Static assets (.wasm + JS) are served by the ASP.NET app for demo convenience; in production prefer CDN hosting for static artifacts and a separate API gateway.
- Redis (pub/sub + state) provides lightweight distributed sync. Replace the state write/replace logic with a CRDT (Automerge, Yjs) or append log (Redis Streams / Kafka) for richer conflict resolution and replay.
- Renderer is intentionally minimal (WebGL cube). Next steps: migrate math to glam, use wgpu/WebGPU for performance, implement tile LOD streaming, and integrate client‑side CRDTs for collaborative scene editing.

What to show to interviewers
- Live demo URL or local demo with docker compose.
- Short screencast: cold load, multi‑client sync (join → op → propagate), renderer interaction (drag to orbit).
- Architecture diagram (architecture.mmd) and a one‑page design tradeoffs summary.
- CI/CD run and E2E test results.

Notes & troubleshooting
- Ensure the .wasm file is served with MIME type `application/wasm` (the Dockerfile places static content into the app's wwwroot).
- If building WASM locally, ensure the wasm output is placed in wasm-client/static/pkg before starting the server (or mount static folder into the container in __docker-compose.yml__).
- For Playwright E2E, the tests expect the server on http://localhost:5000; adjust docker compose ports if necessary.

Next recommended tasks
- Integrate a CRDT library and client bindings to replace full‑state writes with operational patches.
- Replace WebGL math with glam and add a scene graph and LOD streaming.
- Add Helm/Terraform manifests and a Kubernetes deploy job in the CD pipeline.

License & CONTRIBUTING
- Add your chosen license file and a CONTRIBUTING.md with repo conventions, code style, and CI gating rules.

If you want, I can generate:
- A compact one‑page README diagram (Mermaid) for the repository top-level.
- A CONTRIBUTING.md and ISSUE_TEMPLATE.md.
- A migration to glam/wgpu for the renderer and updated Cargo.toml.