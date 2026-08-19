// LiveChart Anime Watching Calendar - sort.moe/calendar

const BACKEND_API_URL = 'https://livechart-anime-tracker.onrender.com';

const state = {
    platform: 'AniList',
    username: '',
    year: new Date().getFullYear(),
    month: new Date().getMonth() + 1, // 1-12
    calendarData: null,
    uploadedMalEntries: null
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
    document.getElementById('themeToggle').addEventListener('click', () => {
        const current = document.documentElement.getAttribute('data-theme');
        const next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('anime_cal_theme', next);
        document.getElementById('themeIcon').textContent = next === 'dark' ? '🌙' : '☀️';
    });

    document.getElementById('prevMonthBtn').addEventListener('click', () => {
        state.month--;
        if (state.month < 1) {
            state.month = 12;
            state.year--;
        }
        updateMonthDisplay();
        if (state.uploadedMalEntries || state.username) fetchCalendar();
    });

    document.getElementById('nextMonthBtn').addEventListener('click', () => {
        state.month++;
        if (state.month > 12) {
            state.month = 1;
            state.year++;
        }
        updateMonthDisplay();
        if (state.uploadedMalEntries || state.username) fetchCalendar();
    });

    document.getElementById('todayBtn').addEventListener('click', () => {
        const now = new Date();
        state.year = now.getFullYear();
        state.month = now.getMonth() + 1;
        updateMonthDisplay();
        if (state.uploadedMalEntries || state.username) fetchCalendar();
    });

    document.getElementById('exportIcsBtn').addEventListener('click', () => {
        if (!state.calendarData || !state.calendarData.days) {
            alert('No episodes loaded to export. Please load your calendar first.');
            return;
        }
        // Direct download from backend
        const exportUrl = `${BACKEND_API_URL}/api/export/ics?platform=${encodeURIComponent(state.platform)}&username=${encodeURIComponent(state.username)}&year=${state.year}&month=${state.month}`;
        window.open(exportUrl, '_blank');
    });

    // MAL XML File Upload listener
    const xmlInput = document.getElementById('malXmlFileInput');
    if (xmlInput) {
        xmlInput.addEventListener('change', handleMalXmlFile);
    }

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
    state.uploadedMalEntries = null;

    localStorage.setItem('anime_cal_user', user);
    localStorage.setItem('anime_cal_plat', plat);

    fetchCalendar();
}

async function handleMalXmlFile(event) {
    const file = event.target.files?.[0];
    if (!file) return;

    showLoading(`Parsing MyAnimeList XML export file...`);
    try {
        const text = await file.text();
        const parser = new DOMParser();
        const xmlDoc = parser.parseFromString(text, "text/xml");
        const animeNodes = xmlDoc.getElementsByTagName("anime");

        const watchingList = [];
        let usernameFound = xmlDoc.getElementsByTagName("user_name")?.[0]?.textContent || 'MyAnimeList User';

        for (let i = 0; i < animeNodes.length; i++) {
            const node = animeNodes[i];
            const status = node.getElementsByTagName("my_status")?.[0]?.textContent;
            if (status === "1" || status === "Watching") {
                const malId = parseInt(node.getElementsByTagName("series_animedb_id")?.[0]?.textContent || "0", 10);
                const title = node.getElementsByTagName("series_title")?.[0]?.textContent || "";
                const watched = parseInt(node.getElementsByTagName("my_watched_episodes")?.[0]?.textContent || "0", 10);
                const score = parseFloat(node.getElementsByTagName("my_score")?.[0]?.textContent || "0");

                if (malId > 0 && title) {
                    watchingList.push({
                        malId,
                        title,
                        watched,
                        score
                    });
                }
            }
        }

        if (watchingList.length === 0) {
            throw new Error('No currently "Watching" anime found in the uploaded MAL XML file.');
        }

        state.platform = 'MyAnimeList';
        state.username = usernameFound;
        state.uploadedMalEntries = watchingList;
        document.getElementById('platformSelect').value = 'MyAnimeList';
        document.getElementById('usernameInput').value = usernameFound;

        showLoading(`Matching ${watchingList.length} watching anime with LiveChart broadcast schedules...`);
        const { episodes, totalWatching } = await fetchSchedulesForMalList(watchingList, state.year, state.month);

        const gridData = buildMonthlyGrid(state.year, state.month, episodes, usernameFound, 'MyAnimeList (XML)', totalWatching);
        state.calendarData = { ...gridData, episodes };
        renderCalendar(gridData);
    } catch (err) {
        console.error(err);
        alert('XML Import Error: ' + err.message);
    } finally {
        hideLoading();
        event.target.value = '';
    }
}

async function fetchCalendar() {
    showLoading(`Loading anime calendar from ${state.platform} for ${state.username}...`);
    try {
        // Detect if already running on Render or localhost
        const isSameOrigin = window.location.origin.includes('onrender.com') || window.location.origin.includes('localhost:5000');
        const apiBase = isSameOrigin ? '' : BACKEND_API_URL;
        const primaryUrl = `${apiBase}/api/calendar/month?platform=${encodeURIComponent(state.platform)}&username=${encodeURIComponent(state.username)}&year=${state.year}&month=${state.month}`;
        
        try {
            const apiRes = await fetch(primaryUrl);
            if (apiRes.ok) {
                const apiData = await apiRes.json();
                state.calendarData = apiData;
                renderCalendar(apiData);
                return;
            } else {
                const errData = await apiRes.json().catch(() => ({}));
                if (errData && errData.error) {
                    throw new Error(errData.error);
                }
                throw new Error(`Server returned status ${apiRes.status}`);
            }
        } catch (netErr) {
            if (netErr.message && !netErr.message.includes('fetch') && !netErr.message.includes('NetworkError') && !netErr.message.includes('Failed to fetch')) {
                throw netErr;
            }
            console.warn("Backend unreachable, trying direct client query:", netErr);
        }

        // 2. Client-side fallback for AniList and Kitsu
        let episodes = [];
        let totalWatching = 0;

        if (state.platform === 'AniList') {
            const res = await fetchAniListWatching(state.username, state.year, state.month);
            episodes = res.episodes;
            totalWatching = res.totalWatching;
        } else if (state.platform === 'Kitsu') {
            const res = await fetchKitsuWatching(state.username, state.year, state.month);
            episodes = res.episodes;
            totalWatching = res.totalWatching;
        } else {
            throw new Error(`Could not connect to backend server at ${BACKEND_API_URL}. The server may be waking up from free-tier sleep (takes ~30s). Please try again in a few seconds.`);
        }

        const gridData = buildMonthlyGrid(state.year, state.month, episodes, state.username, state.platform, totalWatching);
        state.calendarData = { ...gridData, episodes };
        renderCalendar(gridData);
    } catch (err) {
        console.error(err);
        alert(err.message);
    } finally {
        hideLoading();
    }
}

// AniList GraphQL (Direct browser support)
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

// Reusable LiveChart Schedule Matcher for MAL IDs
async function fetchSchedulesForMalList(watching, year, month) {
    const totalWatching = watching.length;
    const malIds = watching.map(w => w.malId).filter(Boolean);

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
    for (const chunk of chunkArray(malIds, 40)) {
        try {
            const mRes = await fetch("https://graphql.anilist.co", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ query: malToAniQuery, variables: { malIds: chunk } })
            });
            if (mRes.ok) {
                const mData = await mRes.json();
                (mData.data?.Page?.media || []).forEach(m => {
                    if (m.id && m.idMal) aniIds.push(m.id);
                });
            }
        } catch {}
    }

    const episodes = [];
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
                    });
                }
            } catch {}
        }
    }

    return { episodes, totalWatching };
}

// Kitsu Client-side
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
