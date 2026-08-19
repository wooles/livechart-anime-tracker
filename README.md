# 📅 Anime Monthly Calendar (Kalendarz Miesięczny Oglądanych Anime)

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Tenrai.Net](https://img.shields.io/badge/Tenrai.Net-3.1.0-blue)](https://www.nuget.org/packages/Tenrai.Net)
[![AniList API](https://img.shields.io/badge/AniList-GraphQL-02A9FF?logo=anilist&logoColor=white)](https://graphql.anilist.co)
[![Kitsu API](https://img.shields.io/badge/Kitsu-REST_v3-FD755C)](https://kitsu.app)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Aplikacja w technologii **.NET 8 (C#)** wyświetlająca przejrzysty **miesięczny kalendarz (siatkę dni Poniedziałek – Niedziela)** z naniesionymi premierami odcinków **wyłącznie dla anime oznaczonych przez użytkownika jako oglądane (Watching)**.

Aplikacja pobiera dane bezpośrednio z API:
- **AniList** (GraphQL API – dokładne godziny emisji każdego odcinka w danym miesiącu)
- **Kitsu** (JSON:API v3)
- **MyAnimeList** (za pośrednictwem biblioteki **Tenrai.Net 3.1.0** oraz endpointów MAL)

---

## ✨ Cechy i Funkcjonalności

- 📅 **Czysty Kalendarz Miesiąca (Siatka 7 Kolumn Pn-Nd)**:
  - Przejrzysty układ dni miesiąca z wyróżnieniem bieżącego dnia ("Dziś").
  - Wygodna nawigacja: `Poprzedni miesiąc`, `Następny miesiąc`, przycisk `Dzisiaj`.
- 📺 **Wyłącznie Oglądane Serie (Watching)**:
  - Brak zbędnego szumu informacyjnego – kalendarz pobiera z konta użytkownika tylko te serie, które aktualnie ogląda.
  - Na kafelkach każdego dnia widnieje: miniaturka plakatu, godzina emisji (np. `18:30`), numer odcinka (np. `Odc. 8`), tytuł i postęp (np. `5/12`).
- 🔍 **Kompaktowy Podgląd Szczegółów**:
  - Kliknięcie w odcinek otwiera estetyczne okno ze szczegółami: data i godzina premiery, opis fabuły (synopsis), ocena, format oraz bezpośrednie linki do MAL, AniList, Kitsu i LiveChart.me.
- 📥 **Eksport do Kalendarza (.ics / iCalendar)**:
  - Generowanie pliku `.ics` dla wybranego miesiąca z przypomnieniami o premierach.
- 💻 **Wsparcie dla CLI i Aplikacji Webowej**:
  - Działa jako aplikacja w przeglądarce pod adresem `http://localhost:5000` oraz w terminalu.

---

## 🚀 Uruchomienie

### Szybki start:
Uruchom plik **`run.bat`** (lub `run.ps1`) w katalogu projektu.
Aplikacja uruchomi się i otworzy w przeglądarce pod adresem **http://localhost:5000**.

### Z wiersza poleceń (.NET CLI):
```bash
# Uruchomienie serwera WWW
dotnet run --server

# Wyświetlenie kalendarza w konsoli
dotnet run -- --calendar anilist wooles 2026 8

# Eksport do pliku .ics
dotnet run -- --export-ics anilist wooles kalendarz.ics
```

---

## 📡 Endpointy REST API

| Metoda | Endpoint | Opis |
| :--- | :--- | :--- |
| `GET` | `/api/calendar/month?platform={p}&username={u}&year={y}&month={m}` | Zwraca siatkę dni miesiąca z naniesionymi odcinkami oglądanych serii. |
| `GET` | `/api/export/ics?platform={p}&username={u}&year={y}&month={m}` | Pobiera plik kalendarza `.ics` dla wskazanego miesiąca. |
| `GET` | `/api/status` | Stan zdrowia i wersje bibliotek. |

---

## 📄 Licencja
Projekt udostępniony na licencji MIT.
