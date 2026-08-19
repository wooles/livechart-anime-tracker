# PROJECT_CONTEXT.md — anime-sorter (sort.moe) & livechart-anime-tracker

## 1. Ecosystem Overview
* **Primary App:** `anime-sorter` (Live: [https://sort.moe/](https://sort.moe/)) — Interactive pairwise merge-ranking anime sorting tool.
* **Companion App:** `livechart-anime-tracker` (Live: [https://sort.moe/calendar/](https://sort.moe/calendar/) & [https://livechart-anime-tracker.onrender.com/](https://livechart-anime-tracker.onrender.com/)) — Full-screen monthly anime release calendar with exact LiveChart broadcasting schedules powered by .NET 8 backend and GitHub Pages frontend.
* **Author / GitHub:** `wooles` ([https://github.com/wooles](https://github.com/wooles))
  * Sorter & Calendar Web Repo: `https://github.com/wooles/anime-sorter.git` (GitHub Pages at `sort.moe` and `sort.moe/calendar/`)
  * Calendar Backend Repo: `https://github.com/wooles/livechart-anime-tracker.git` (Render at `livechart-anime-tracker.onrender.com`)

---

## 2. Multi-Project Architecture
* **anime-sorter & calendar frontend (`C:\Users\Piotrek\Desktop\sorter`)**:
  * **Root (`/`):** Anime Sorter (HTML5 / CSS3 / Vanilla JS SPA) — Watchlist import (AniList, Kitsu, MyAnimeList XML/API), pairwise merge sort, season picker, Litterbox export, direct navigation to `/calendar`.
  * **Subfolder (`/calendar`):** Anime Calendar frontend — Weekly 7-day timeline view, 0ms instant local cache restoration, dark/light theme, English/Romaji title switcher, 24h European time format, timezone autodetection, .ics export.
* **livechart-anime-tracker (`C:\Users\Piotrek\Desktop\kalendarz`)**:
  * **Backend:** .NET 8 Minimal API (C#) using **Tenrai.Net**, **AniList GraphQL**, **Kitsu JSON:API**, and **Ical.Net**.
  * **Cloud Deployment:** Hosted on Render.com (`Dockerfile` + `render.yaml`) with dynamic port binding.
  * **Performance:** Single-query batch GraphQL paring (sub-second response <1s), in-memory caching, season cap safeguards, hiatus/delay tracking.

---

## 3. Workflow Trigger & Auto-Onboarding Rule ("kontynuuj anisort")
Whenever the user types `"kontynuuj"`, `"kontynuuj anisort"`, `"kontynuuj kalendarz"`, or similar in ANY session or on a new machine:
1. **Dual-Project Scope:** Immediately recognize that the user is working on both `anime-sorter` (`sort.moe`) and `livechart-anime-tracker` (`kalendarz`).
2. **Synchronize Git Repositories:**
   * In `sorter`: verify `git pull origin main`.
   * In `kalendarz`: verify `git pull origin main`.
3. **Build & Validate:**
   * Run `dotnet build LiveChartTracker.csproj` to ensure all .NET dependencies and packages compile cleanly.
4. **Report Readiness:** Provide a concise status summary confirming both repositories are synchronized and ready to continue immediately without asking the user for re-explanations.

---

## 4. Key Guidelines & Technical Conventions
* **Always Deliver Complete Code:** No placeholder snippets or omitted lines.
* **Dual Repository Sync:** Any frontend changes in `kalendarz/wwwroot/` should be synchronized with `sorter/calendar/` so both GitHub Pages (`sort.moe/calendar`) and the Render standalone view remain identical.
* **Preserve Dynamic Port Binding:** Always bind backend to `Environment.GetEnvironmentVariable("PORT") ?? "5000"`.
* **Zero Layout Shifts:** Keep tabular numbers (`font-variant-numeric: tabular-nums`) and fixed min-widths on time badges.
