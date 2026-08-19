# 📅 Anime Monthly Calendar (LiveChart Anime Tracker)

[![Live Web App](https://img.shields.io/badge/Live-sort.moe/calendar-blue?style=for-the-badge&logo=google-chrome&logoColor=white)](https://sort.moe/calendar)
[![Backend API](https://img.shields.io/badge/API-onrender.com-blue?style=for-the-badge&logo=render&logoColor=white)](https://livechart-anime-tracker.onrender.com/)
[![Anime Sorter](https://img.shields.io/badge/🏆_Sorter-sort.moe-6c5ce7?style=for-the-badge)](https://sort.moe/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Tenrai.Net](https://img.shields.io/badge/Tenrai.Net-3.1.0-2ecc71?style=for-the-badge)](https://www.nuget.org/packages/Tenrai.Net)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](https://opensource.org/licenses/MIT)

A high-performance **.NET 8** web application providing an ultra-compact, full-screen **Weekly & Monthly Anime Watching Calendar**. Automatically aggregates upcoming episode releases **exclusively for series currently in the user's watching list**, mapped to exact Japanese TV broadcast minutes from LiveChart.

👉 **Anime Watching Calendar:** **[https://sort.moe/calendar](https://sort.moe/calendar)**  
👉 **Live Cloud API:** **[https://livechart-anime-tracker.onrender.com/](https://livechart-anime-tracker.onrender.com/)**  
👉 **Anime Ranking Sorter:** **[https://sort.moe/](https://sort.moe/)**

---

## ✨ Features

- 📅 **Compact Full-Width Calendar Grid**:
  - 100% responsive full-screen 7-column grid (Monday to Sunday) with fixed row heights and sticky weekday headers.
  - Dynamic density scaling (`.condensed-medium`, `.condensed-tight`) to fit high-volume release days seamlessly.
  - One-line compact episode strips: `⏰ 18:30 | Ep. 8 | Title [5/12]`.
- 📺 **Watching Series Exclusivity**:
  - Zero clutter: displays only shows the user is currently watching.
  - Automatically filters out anime that concluded prior to the target month.
- ⏰ **Accurate LiveChart Broadcast Schedules & Timezone Conversion**:
  - Integrates minute-exact broadcasting schedules from Japanese television stations / LiveChart schedules.
  - Automatically detects the user's browser timezone (e.g. `Europe/Warsaw, UTC+2`) and converts all showtimes.
  - Live ticking local clock in the navigation bar.
- 🌐 **Multi-Platform Watchlist Aggregation**:
  - **MyAnimeList**: High-speed, rate-limit resilient watchlist extraction powered by **Tenrai.Net 3.1.0**.
  - **AniList**: Real-time GraphQL synchronization.
  - **Kitsu**: Full JSON:API v3 library parsing.
- 🔍 **Episode Detail Modal**:
  - Click any episode chip to view the episode title, air date & local time, format, score, progress, synopsis, and direct external links to LiveChart, MAL, AniList, and Kitsu.
- 📥 **RFC 5545 iCalendar (.ics) Export**:
  - Download standard `.ics` calendar files with reminder alarms for Google Calendar, Apple Calendar, and Outlook.
- 🏆 **Ecosystem Integration**:
  - Integrated navigation button to **[Anime Sorter (sort.moe)](https://sort.moe/)**.

---

## 🛠️ Architecture & Tech Stack

- **Backend**: .NET 8 ASP.NET Core Minimal API (C#), `Tenrai.Net 3.1.0`, `Ical.Net 4.3.1`.
- **Frontend**: Vanilla JavaScript (ES6+), Modern CSS3 Grid & Variables, Dark/Light theme toggle.
- **Containerization**: Multi-stage `Dockerfile` (Debian-based ASP.NET Core runtime with full ICU/timezone support) and `render.yaml`.
- **Deployment**: Hosted on Render.com with dynamic port binding and zero-config deployment.

---



## 🔗 Related Projects

* 🏆 **[wooles/anime-sorter](https://github.com/wooles/anime-sorter)** — Anime ranking and pairwise merge sort tool hosted on [sort.moe](https://sort.moe/).

---

## 📄 License

Distributed under the [MIT License](LICENSE).
