# PROJECT_CONTEXT.md — anime-sorter (sort.moe) & livechart-anime-tracker

## 1. Ecosystem Overview
* **Primary App:** `anime-sorter` (Live: [https://sort.moe/](https://sort.moe/)) — Interactive pairwise merge-ranking anime tool.
* **Companion App:** `livechart-anime-tracker` (Live: [https://livechart-anime-tracker.onrender.com/](https://livechart-anime-tracker.onrender.com/)) — Full-screen monthly anime watching calendar with exact LiveChart broadcasting schedules powered by .NET 8 + `Tenrai.Net 3.1.0`.
* **Author / GitHub:** `wooles` ([https://github.com/wooles](https://github.com/wooles))
  * Sorter Repo: `https://github.com/wooles/anime-sorter.git`
  * Calendar Repo: `https://github.com/wooles/livechart-anime-tracker.git`

---

## 2. Multi-Project Architecture
* **anime-sorter (`C:\Users\Piotrek\Desktop\sorter`)**:
  * **Frontend:** Standalone HTML5 / CSS3 / Vanilla JS SPA.
  * **Storage:** Browser `localStorage` (caching metadata/covers, persisting list items, language, and theme).
  * **Integrations:** Watchlist import (AniList, Kitsu, MyAnimeList XML/API), pairwise merge sort, ties, season picker, Litterbox export, direct navigation button to Calendar (`https://livechart-anime-tracker.onrender.com/`).
* **livechart-anime-tracker (`C:\Users\Piotrek\Desktop\kalendarz`)**:
  * **Backend:** .NET 8 Minimal API (C#) using **Tenrai.Net 3.1.0** and **Ical.Net 4.3.1**.
  * **Cloud Deployment:** Hosted on Render.com (`Dockerfile` + `render.yaml`) with dynamic port binding.
  * **Features:** 7-column calendar (Mon-Sun), minute-accurate LiveChart schedules, timezone conversion (`Europe/Warsaw`), watching list exclusivity, `.ics` exports, direct navigation button to Sorter (`https://sort.moe`).

---

## 3. Workflow Trigger & Auto-Onboarding Rule ("kontynuuj anisort")
Whenever the user types `"kontynuuj"`, `"kontynuuj anisort"`, `"kontynuuj kalendarz"`, or similar in any session:
1. **Recognize Dual-Project Scope:** Immediately recognize that the user is working on both `anime-sorter` and `livechart-anime-tracker`.
2. **Synchronize Git Repositories:**
   * In `C:\Users\Piotrek\Desktop\sorter`: check if `.git` exists, clone if missing (`git clone https://github.com/wooles/anime-sorter.git .`), or pull latest (`git pull origin main`).
   * In `C:\Users\Piotrek\Desktop\kalendarz`: check if `.git` exists, clone if missing (`git clone https://github.com/wooles/livechart-anime-tracker.git .`), or pull latest (`git pull origin main`).
3. **Execute Dependency & Workspace Setup:**
   * Run `.\setup.ps1` to restore .NET SDK packages (`dotnet restore` for `LiveChartTracker.csproj` + `Tenrai.Net 3.1.0` and `MalProxy.csproj`).
   * Verify Python, Git, and .NET environment tools.
   * Ensure `.vscode/` configurations (extensions recommendations and editor settings) are populated in both directories.
4. **Verify Live Endpoints & Bindings:**
   * Sorter (`sort.moe`) ➔ Calendar (`https://livechart-anime-tracker.onrender.com/`)
   * Calendar ➔ Sorter (`https://sort.moe/`)
5. **Report Complete Readiness:** Provide a concise status summary to the user confirming both repositories are synced, packages restored, and ready for work.

---

## 4. Coding & Modification Guidelines
* **Complete Code Only:** Always maintain and deliver complete files without placeholders or omission comments.
* **List Reset Behavior in Sorter:** Any new import or manual add action must clear existing entries (`entries = []`) to prevent duplicate bloat.
* **Tenrai.Net Integrity:** Preserve `Tenrai.Net` package integration for MyAnimeList interactions in the .NET backend.
* **Port Compatibility:** Always bind to dynamic `$PORT` environment variable (`Environment.GetEnvironmentVariable("PORT") ?? "5000"`) for cloud hosting.
