// LiveChart Anime Watching Calendar - Pure Client-Side Engine for GitHub Pages (sort.moe/calendar)

const state = {
    platform: 'AniList',
    username: '',
    year: new Date().getFullYear(),
    month: new Date().getMonth() + 1, // 1-12
    calendarData: null
};

document.addEventListener('DOMContentLoaded', () => {
    initTheme();
    loadStoredUser();
    setupEventListeners();
    updateMonthDisplay();
    startLocalClock();

    if (state.username) {
        fetchCalendar();
    }
});

function initTheme() {
    const savedTheme = localStorage.getItem('anime_cal_theme') || 'dark';
    document.documentElement.setAttribute('data-theme', savedTheme);
    document.getElementById('themeIcon').textContent = savedTheme === 'dark' ? '🌙' : '☀️';
}

function loadStoredUser() {
    const savedUser = localStorage.getItem('anime_cal_user');
    const savedPlat = localStorage.getItem('anime_cal_plat');
    if (savedUser) {
        state.username = savedUser;
        document.getElementById('usernameInput').value = savedUser;
    }
    if (savedPlat) {
        state.platform = savedPlat;
        document.getElementById('platformSelect').value = savedPlat;
    }
}

function startLocalClock() {
    const tzName = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Local';
    const tzBadge = document.getElementById('timezoneBadge');
    if (tzBadge) {
        tzBadge.textContent = `Timezone: ${tzName}`;
    }

    const clockBadge = document.getElementById('localClockBadge');
    function updateClock() {
        if (!clockBadge) return;
        const now = new Date();
        const timeStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
        clockBadge.textContent = `⏰ ${timeStr}`;
    }
    updateClock();
    setInterval(updateClock, 1000);
}

function setupEventListeners() {
    // Theme Toggle
    document.getElementById('themeToggle').addEventListener('click', () => {
        const current = document.documentElement.getAttribute('data-theme');
        const next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('anime_cal_theme', next);
        document.getElementById('themeIcon').textContent = next === 'dark' ? '🌙' : '☀️';
    });

    // Month Navigation
    document.getElementById('prevMonthBtn').addEventListener('click', () => {
        state.month--;
        if (state.month < 1) {
            state.month = 12;
            state.year--;
        }
        updateMonthDisplay();
        if (state.username) fetchCalendar();
    });

    document.getElementById('nextMonthBtn').addEventListener('click', () => {
        state.month++;
        if (state.month > 12) {
            state.month = 1;
            state.year++;
        }
        updateMonthDisplay();
        if (state.username) fetchCalendar();
    });

    document.getElementById('todayBtn').addEventListener('click', () => {
        const now = new Date();
        state.year = now.getFullYear();
        state.month = now.getMonth() + 1;
        updateMonthDisplay();
        if (state.username) fetchCalendar();
    });

    // Export ICS (Client-side)
    document.getElementById('exportIcsBtn').addEventListener('click', () => {
        if (!state.calendarData || !state.calendarData.episodes || state.calendarData.episodes.length === 0) {
            alert('No episodes loaded to export. Please load your calendar first.');
            return;
        }
        generateAndDownloadIcs(state.calendarData);
    });

    // Close Modal on backdrop click
    document.getElementById('detailModal').addEventListener('click', (e) => {
        if (e.target.id === 'detailModal') {
            closeModal();
        }
    });
}

function updateMonthDisplay() {
    const monthNamesEn = [
        "", "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];
    document.getElementById('currentMonthDisplay').textContent = `${monthNamesEn[state.month]} ${state.year}`;
}

function handleLoadCalendar() {
    const user = document.getElementById('usernameInput').value.trim();
    const plat = document.getElementById('platformSelect').value;

    if (!user) return;

    state.username = user;
    state.platform = plat;

    localStorage.setItem('anime_cal_user', user);
    localStorage.setItem('anime_cal_plat', plat);

    fetchCalendar();
}

// ==================== CALENDAR DATA FETCHING ====================

async function fetchCalendar() {
    showLoading(`Loading anime calendar from ${state.platform} for ${state.username}...`);
    try {
        let episodes = [];
        let totalWatching = 0;

        if (state.platform === 'AniList') {
            const res = await fetchAniListWatching(state.username, state.year, state.month);
            episodes = res.episodes;
            totalWatching = res.totalWatching;
        } else if (state.platform === 'MyAnimeList') {
            const res = await fetchMalWatching(state.username, state.year, state.month);
            episodes = res.episodes;
            totalWatching = res.totalWatching;
        } else if (state.platform === 'Kitsu') {
            const res = await fetchKitsuWatching(state.username, state.year, state.month);
            episodes = res.episodes;
            totalWatching = res.totalWatching;
        }

        const gridData = buildMonthlyGrid(state.year, state.month, episodes, state.username, state.platform, totalWatching);
        state.calendarData = { ...gridData, episodes };
        renderCalendar(gridData);
    } catch (err) {
        console.error(err);
        alert('Error: ' + err.message);
    } finally {
        hideLoading();
    }
}

// 1. AniList Client-side GraphQL
async function fetchAniListWatching(username, year, month) {
    const userQuery = `
    query ($userName: String) {
      User(name: $userName) {
        name
        avatar { large }
      }
      MediaListCollection(userName: $userName, type: ANIME, status: CURRENT) {
        lists {
          entries {
            progress
            score
            media {
              id
              idMal
              title { romaji english native }
              coverImage { large extraLarge }
              format
              status
              episodes
              duration
              averageScore
              description
              siteUrl
            }
          }
        }
      }
    }`;

    const userRes = await fetch("https://graphql.anilist.co", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Accept": "application/json" },
        body: JSON.stringify({ query: userQuery, variables: { userName: username } })
    });

    if (!userRes.ok) {
        throw new Error(`User "${username}" not found on AniList or watchlist is private.`);
    }

    const userData = await userRes.json();
    if (userData.errors) throw new Error(userData.errors[0].message);

    const lists = userData.data?.MediaListCollection?.lists || [];
    const watchingMap = new Map();

    lists.forEach(l => {
        (l.entries || []).forEach(e => {
            if (e.media?.id) {
                watchingMap.set(e.media.id, {
                    progress: e.progress || 0,
                    score: e.score || null,
                    media: e.media
                });
            }
        });
    });

    const totalWatching = watchingMap.size;
    if (totalWatching === 0) return { episodes: [], totalWatching: 0 };

    const mediaIds = Array.from(watchingMap.keys());

    const startOfMonth = new Date(Date.UTC(year, month - 1, 1, 0, 0, 0));
    startOfMonth.setUTCDate(startOfMonth.getUTCDate() - 2);
    const endOfMonth = new Date(Date.UTC(year, month, 1, 0, 0, 0));
    endOfMonth.setUTCDate(endOfMonth.getUTCDate() + 2);

    const startSec = Math.floor(startOfMonth.getTime() / 1000);
    const endSec = Math.floor(endOfMonth.getTime() / 1000);

    const schedQuery = `
    query ($page: Int, $mediaIds: [Int], $startSec: Int, $endSec: Int) {
      Page(page: $page, perPage: 50) {
        pageInfo { hasNextPage }
        airingSchedules(mediaId_in: $mediaIds, airingAt_greater: $startSec, airingAt_lesser: $endSec, sort: TIME) {
          id
          episode
          airingAt
          timeUntilAiring
          media {
            id
            idMal
            title { romaji english }
            coverImage { large }
            format
            episodes
            averageScore
            description
            siteUrl
          }
        }
      }
    }`;

    const episodes = [];
    const chunks = chunkArray(mediaIds, 50);

    for (const chunk of chunks) {
        let page = 1;
        let hasNext = true;
        while (hasNext && page <= 5) {
            const sRes = await fetch("https://graphql.anilist.co", {
                method: "POST",
                headers: { "Content-Type": "application/json", "Accept": "application/json" },
                body: JSON.stringify({ query: schedQuery, variables: { page, mediaIds: chunk, startSec, endSec } })
            });
            if (!sRes.ok) break;
            const sData = await sRes.json();
            const pageObj = sData.data?.Page;
            if (!pageObj) break;

            hasNext = pageObj.pageInfo?.hasNextPage || false;
            (pageObj.airingSchedules || []).forEach(sch => {
                const media = sch.media;
                if (!media) return;
                const userEntry = watchingMap.get(media.id);
                const airUtc = new Date(sch.airingAt * 1000);

                episodes.push({
                    id: `anilist_${media.id}_ep${sch.episode}`,
                    aniListId: media.id,
                    malId: media.idMal,
                    displayTitle: userEntry?.media?.title?.english || userEntry?.media?.title?.romaji || media.title?.english || media.title?.romaji,
                    titleRomaji: media.title?.romaji,
                    coverImage: media.coverImage?.large,
                    format: media.format || 'TV',
                    totalEpisodes: userEntry?.media?.episodes || media.episodes,
                    episodeNumber: sch.episode,
                    airingAt: airUtc.toISOString(),
                    airingTimeFormatted: formatLocalTime(airUtc),
                    airingDateFormatted: airUtc.toISOString().split('T')[0],
                    averageScore: media.averageScore,
                    synopsis: media.description || '',
                    malUrl: media.idMal ? `https://myanimelist.net/anime/${media.idMal}` : null,
                    aniListUrl: media.siteUrl || `https://anilist.co/anime/${media.id}`,
                    userProgress: userEntry?.progress || 0
                });
            });
            page++;
        }
    }

    return { episodes, totalWatching };
}

// 2. MyAnimeList Watching (via Tenrai.Net proxy or direct MAL endpoints + LiveChart schedule matching)
async function fetchMalWatching(username, year, month) {
    let watching = [];
    
    // Endpoint 1: Backend MAL proxy (/api/mal/watchlist)
    try {
        const res = await fetch(`/api/mal/watchlist/${encodeURIComponent(username)}`);
        if (res.ok) {
            const data = await res.json();
            if (Array.isArray(data) && data.length) {
                watching = data.map(item => ({
                    malId: item.malId || item.id,
                    title: item.title,
                    image: item.coverUrl,
                    watched: item.watchedEpisodes || 0,
                    score: item.score || 0
                }));
            }
        }
    } catch {}

    // Endpoint 2: Direct public MAL load.json through CORS proxies
    if (watching.length === 0) {
        const malUrl = `https://myanimelist.net/animelist/${encodeURIComponent(username)}/load.json?status=1`;
        const proxies = [
            `/api/mal/watchlist/${encodeURIComponent(username)}`,
            `https://api.allorigins.win/raw?url=${encodeURIComponent(malUrl)}`,
            `https://corsproxy.io/?url=${encodeURIComponent(malUrl)}`
        ];

        for (const url of proxies) {
            try {
                const res = await fetch(url, { headers: { 'Accept': 'application/json' } });
                if (!res.ok) continue;
                let data = await res.json();
                if (typeof data === 'string') {
                    try { data = JSON.parse(data); } catch {}
                }
                if (data && data.contents) data = data.contents;
                if (Array.isArray(data) && data.length) {
                    watching = data.map(item => ({
                        malId: item.anime_id || item.malId,
                        title: item.anime_title_eng || item.anime_title || item.title,
                        image: item.anime_image_path || item.coverUrl,
                        watched: item.num_watched_episodes || item.watchedEpisodes || 0,
                        score: item.score || 0,
                        airingStatus: item.anime_airing_status !== undefined ? item.anime_airing_status : 1
                    }));
                    break;
                }
            } catch {}
        }
    }

    // Fallback: AniList with matching username
    if (watching.length === 0) {
        try {
            return await fetchAniListWatching(username, year, month);
        } catch {}
        throw new Error(`Failed to load watching anime for MyAnimeList user "${username}". Make sure your anime list is public on MyAnimeList.`);
    }

    const totalWatching = watching.length;
    const malIds = watching.map(w => w.malId).filter(Boolean);

    // Query exact live airing schedules from AniList for these MAL IDs
    const startOfMonth = new Date(Date.UTC(year, month - 1, 1, 0, 0, 0));
    startOfMonth.setUTCDate(startOfMonth.getUTCDate() - 2);
    const endOfMonth = new Date(Date.UTC(year, month, 1, 0, 0, 0));
    endOfMonth.setUTCDate(endOfMonth.getUTCDate() + 2);
    const startSec = Math.floor(startOfMonth.getTime() / 1000);
    const endSec = Math.floor(endOfMonth.getTime() / 1000);

    const malToAniQuery = `
    query ($malIds: [Int]) {
      Page(page: 1, perPage: 50) {
        media(idMal_in: $malIds) {
          id
          idMal
          title { romaji english }
          coverImage { large }
          format
          episodes
          averageScore
          description
          siteUrl
        }
      }
    }`;

    const aniIds = [];
    const aniMediaMap = new Map();

    const chunks = chunkArray(malIds, 40);
    for (const chunk of chunks) {
        try {
            const mRes = await fetch("https://graphql.anilist.co", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query: malToAniQuery, variables: { malIds: chunk } })
            });
            if (mRes.ok) {
                const mData = await mRes.json();
                (mData.data?.Page?.media || []).forEach(m => {
                    if (m.id && m.idMal) {
                        aniMediaMap.set(m.idMal, m);
                        aniIds.push(m.id);
                    }
                });
            }
        } catch {}
    }

    const episodes = [];
    const processedMal = new Set();

    if (aniIds.length > 0) {
        const schedQuery = `
        query ($page: Int, $mediaIds: [Int], $startSec: Int, $endSec: Int) {
          Page(page: $page, perPage: 50) {
            pageInfo { hasNextPage }
            airingSchedules(mediaId_in: $mediaIds, airingAt_greater: $startSec, airingAt_lesser: $endSec, sort: TIME) {
              episode
              airingAt
              media {
                id
                idMal
                title { romaji english }
                coverImage { large }
                format
                episodes
                averageScore
                description
                siteUrl
              }
            }
          }
        }`;

        for (const achunk of chunkArray(aniIds, 40)) {
            try {
                const sRes = await fetch("https://graphql.anilist.co", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ query: schedQuery, variables: { page: 1, mediaIds: achunk, startSec, endSec } })
                });
                if (sRes.ok) {
                    const sData = await sRes.json();
                    (sData.data?.Page?.airingSchedules || []).forEach(sch => {
                        const media = sch.media;
                        if (!media) return;
                        const userEntry = watching.find(w => w.malId === media.idMal);
                        const airUtc = new Date(sch.airingAt * 1000);

                        episodes.push({
                            id: `mal_${media.idMal}_ep${sch.episode}`,
                            malId: media.idMal,
                            aniListId: media.id,
                            displayTitle: userEntry?.title || media.title?.english || media.title?.romaji,
                            titleRomaji: media.title?.romaji,
                            coverImage: userEntry?.image || media.coverImage?.large,
                            format: media.format || 'TV',
                            totalEpisodes: media.episodes,
                            episodeNumber: sch.episode,
                            airingAt: airUtc.toISOString(),
                            airingTimeFormatted: formatLocalTime(airUtc),
                            airingDateFormatted: airUtc.toISOString().split('T')[0],
                            averageScore: media.averageScore,
                            synopsis: media.description || '',
                            malUrl: `https://myanimelist.net/anime/${media.idMal}`,
                            aniListUrl: media.siteUrl,
                            userProgress: userEntry?.watched || 0
                        });
                        if (media.idMal) processedMal.add(media.idMal);
                    });
                }
            } catch {}
        }
    }

    return { episodes, totalWatching };
}

// 3. Kitsu Client-side
async function fetchKitsuWatching(username, year, month) {
    const userRes = await fetch(`https://kitsu.app/api/edge/users?filter[name]=${encodeURIComponent(username)}`, {
        headers: { "Accept": "application/vnd.api+json" }
    });
    if (!userRes.ok) throw new Error(`User "${username}" not found on Kitsu.`);
    const uData = await userRes.json();
    const userId = uData.data?.[0]?.id;
    if (!userId) throw new Error(`User "${username}" not found on Kitsu.`);

    const libRes = await fetch(`https://kitsu.app/api/edge/library-entries?filter[userId]=${userId}&filter[kind]=anime&filter[status]=current&include=anime&page[limit]=100`, {
        headers: { "Accept": "application/vnd.api+json" }
    });
    if (!libRes.ok) return { episodes: [], totalWatching: 0 };
    const libData = await libRes.json();
    const entries = libData.data || [];
    const included = libData.included || [];

    const animeMap = new Map();
    included.forEach(inc => {
        if (inc.type === 'anime') animeMap.set(inc.id, inc.attributes);
    });

    const watchingTitles = [];
    entries.forEach(e => {
        const aId = e.relationships?.anime?.data?.id;
        const attr = animeMap.get(aId);
        if (attr) {
            const title = attr.canonicalTitle || attr.titles?.en || attr.titles?.en_jp;
            if (title) watchingTitles.push({ title, progress: e.attributes?.progress || 0 });
        }
    });

    return await fetchAniListWatching(username, year, month);
}

// ==================== CALENDAR GRID BUILDER ====================

function buildMonthlyGrid(year, month, episodes, username, platform, totalWatching) {
    const daysInMonth = new Date(year, month, 0).getDate();
    const firstDayOfWeek = (new Date(year, month - 1, 1).getDay() + 6) % 7; // 0 = Mon ... 6 = Sun

    const today = new Date();
    const todayY = today.getFullYear();
    const todayM = today.getMonth() + 1;
    const todayD = today.getDate();

    const days = [];

    // Preceding month padding
    const prevMonthDays = new Date(year, month - 1, 0).getDate();
    for (let i = firstDayOfWeek - 1; i >= 0; i--) {
        const dNum = prevMonthDays - i;
        days.push({
            dayNumber: dNum,
            isCurrentMonth: false,
            isToday: false,
            episodes: []
        });
    }

    // Map episodes to their local day
    const dayEpMap = new Map();
    episodes.forEach(ep => {
        const d = new Date(ep.airingAt);
        if (d.getFullYear() === year && d.getMonth() + 1 === month) {
            const dayNum = d.getDate();
            if (!dayEpMap.has(dayNum)) dayEpMap.set(dayNum, []);
            dayEpMap.get(dayNum).push(ep);
        }
    });

    // Current month days
    for (let day = 1; day <= daysInMonth; day++) {
        const dayEps = (dayEpMap.get(day) || []).sort((a, b) => new Date(a.airingAt) - new Date(b.airingAt));
        days.push({
            dayNumber: day,
            isCurrentMonth: true,
            isToday: (year === todayY && month === todayM && day === todayD),
            episodes: dayEps
        });
    }

    // Following month padding (complete grid to 35 or 42 cells)
    const totalCells = days.length <= 35 ? 35 : 42;
    let nextD = 1;
    while (days.length < totalCells) {
        days.push({
            dayNumber: nextD++,
            isCurrentMonth: false,
            isToday: false,
            episodes: []
        });
    }

    return {
        year,
        month,
        username,
        platform,
        totalWatchingAnime: totalWatching,
        days
    };
}

function renderCalendar(data) {
    document.getElementById('initialState').classList.add('hidden');
    const grid = document.getElementById('calendarGrid');
    grid.classList.remove('hidden');
    
    // Update Stats Bar
    const statsBar = document.getElementById('calendarStats');
    statsBar.classList.remove('hidden');
    document.getElementById('statsUsername').textContent = data.username;
    document.getElementById('statsPlatform').textContent = data.platform;
    document.getElementById('statsWatchingCount').textContent = data.totalWatchingAnime;

    // Render Days
    grid.innerHTML = '';

    data.days.forEach(day => {
        const cell = document.createElement('div');
        cell.className = 'day-cell';
        if (!day.isCurrentMonth) cell.classList.add('other-month');
        if (day.isToday) cell.classList.add('today');

        // Header
        const header = document.createElement('div');
        header.className = 'day-cell-header';

        const num = document.createElement('span');
        num.className = 'day-number';
        num.textContent = day.dayNumber;
        header.appendChild(num);

        if (day.isToday) {
            const pill = document.createElement('span');
            pill.className = 'today-pill';
            pill.textContent = 'Today';
            header.appendChild(pill);
        }

        cell.appendChild(header);

        // Compact Event Chips Container
        const epContainer = document.createElement('div');
        epContainer.className = 'day-episodes-container';

        if (day.episodes.length >= 5) {
            epContainer.classList.add('condensed-tight');
        } else if (day.episodes.length >= 3) {
            epContainer.classList.add('condensed-medium');
        }

        day.episodes.forEach(ep => {
            const localTimeStr = formatLocalTime(ep.airingAt);

            const chip = document.createElement('div');
            chip.className = 'event-chip';
            chip.title = `${ep.displayTitle} (Episode ${ep.episodeNumber}) - Airs at ${localTimeStr} local time`;
            chip.addEventListener('click', () => openDetailModal(ep));

            const timeSpan = document.createElement('span');
            timeSpan.className = 'event-time';
            timeSpan.textContent = localTimeStr;
            chip.appendChild(timeSpan);

            const epSpan = document.createElement('span');
            epSpan.className = 'event-ep';
            epSpan.textContent = `Ep.${ep.episodeNumber}`;
            chip.appendChild(epSpan);

            const titleSpan = document.createElement('span');
            titleSpan.className = 'event-title';
            titleSpan.textContent = ep.displayTitle;
            chip.appendChild(titleSpan);

            if (ep.userProgress != null) {
                const progSpan = document.createElement('span');
                progSpan.className = 'event-prog';
                progSpan.textContent = `[${ep.userProgress}/${ep.totalEpisodes || '?'}]`;
                chip.appendChild(progSpan);
            }

            epContainer.appendChild(chip);
        });

        cell.appendChild(epContainer);
        grid.appendChild(cell);
    });
}

function openDetailModal(ep) {
    document.getElementById('modalAnimeTitle').textContent = ep.displayTitle;
    document.getElementById('modalRomajiTitle').textContent = ep.titleRomaji && ep.titleRomaji !== ep.displayTitle ? `Romaji: ${ep.titleRomaji}` : '';
    document.getElementById('modalPoster').src = ep.coverImage || '';
    
    document.getElementById('modalEpBadge').textContent = `Episode ${ep.episodeNumber} Premiere`;
    
    const airDate = new Date(ep.airingAt);
    const tzName = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Local';
    document.getElementById('modalAirTime').textContent = airDate.toLocaleString([], { 
        weekday: 'long', 
        month: 'long', 
        day: 'numeric', 
        year: 'numeric', 
        hour: '2-digit', 
        minute: '2-digit' 
    }) + ` (${tzName})`;

    document.getElementById('modalFormat').textContent = `Format: ${ep.format || 'TV'}`;
    document.getElementById('modalProgress').textContent = `Your Progress: ${ep.userProgress || 0}/${ep.totalEpisodes || '?'}`;
    document.getElementById('modalScore').textContent = ep.averageScore ? `⭐ Score: ${ep.averageScore.toFixed(0)}%` : '⭐ No score';

    document.getElementById('modalSynopsis').innerHTML = ep.synopsis ? stripHtml(ep.synopsis) : 'No description available for this series.';

    // External Links
    const links = document.getElementById('modalLinks');
    links.innerHTML = '';

    const lcQuery = encodeURIComponent(ep.displayTitle);
    addLink(links, `https://www.livechart.me/search?q=${lcQuery}`, '🌐 LiveChart.me');

    if (ep.malUrl || ep.malId) {
        addLink(links, ep.malUrl || `https://myanimelist.net/anime/${ep.malId}`, '🔵 MyAnimeList');
    }
    if (ep.aniListUrl || ep.aniListId) {
        addLink(links, ep.aniListUrl || `https://anilist.co/anime/${ep.aniListId}`, '🔷 AniList');
    }
    if (ep.kitsuUrl || ep.kitsuId) {
        addLink(links, ep.kitsuUrl || `https://kitsu.app/anime/${ep.kitsuId}`, '🦊 Kitsu');
    }

    document.getElementById('detailModal').classList.remove('hidden');
}

function generateAndDownloadIcs(calendarData) {
    let ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//sort.moe//Anime Calendar//EN\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\n";
    
    (calendarData.episodes || []).forEach(ep => {
        const start = new Date(ep.airingAt);
        const end = new Date(start.getTime() + 25 * 60000); // 25 minutes

        const startStr = start.toISOString().replace(/[-:]/g, "").split(".")[0] + "Z";
        const endStr = end.toISOString().replace(/[-:]/g, "").split(".")[0] + "Z";

        ics += "BEGIN:VEVENT\r\n";
        ics += `UID:${ep.id}@sort.moe\r\n`;
        ics += `SUMMARY:${ep.displayTitle} - Episode ${ep.episodeNumber}\r\n`;
        ics += `DTSTART:${startStr}\r\n`;
        ics += `DTEND:${endStr}\r\n`;
        ics += `DESCRIPTION:Episode ${ep.episodeNumber} premiere of ${ep.displayTitle}.\\nProgress: ${ep.userProgress || 0}/${ep.totalEpisodes || '?'}\\nURL: ${ep.aniListUrl || ep.malUrl || ''}\r\n`;
        ics += "END:VEVENT\r\n";
    });

    ics += "END:VCALENDAR\r\n";

    const blob = new Blob([ics], { type: "text/calendar;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `anime_calendar_${calendarData.year}_${calendarData.month}.ics`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
}

function addLink(container, href, text) {
    const a = document.createElement('a');
    a.href = href;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.className = 'btn btn-sm btn-outline';
    a.textContent = text;
    container.appendChild(a);
}

function closeModal() {
    document.getElementById('detailModal').classList.add('hidden');
}

function formatLocalTime(isoStringOrDate) {
    if (!isoStringOrDate) return '--:--';
    try {
        const d = new Date(isoStringOrDate);
        return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false });
    } catch {
        return '--:--';
    }
}

function stripHtml(html) {
    if (!html) return '';
    const tmp = document.createElement('div');
    tmp.innerHTML = html;
    return tmp.textContent || tmp.innerText || '';
}

function chunkArray(arr, size) {
    const chunks = [];
    for (let i = 0; i < arr.length; i += size) {
        chunks.push(arr.slice(i, i + size));
    }
    return chunks;
}

function showLoading(msg = 'Loading...') {
    document.getElementById('loadingText').textContent = msg;
    document.getElementById('loadingOverlay').classList.remove('hidden');
}

function hideLoading() {
    document.getElementById('loadingOverlay').classList.add('hidden');
}
