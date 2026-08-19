// Anime Schedule - Weekly Airing Timeline (.NET 8 + Tenrai.Net)

const BACKEND_API_URL = 'https://livechart-anime-tracker.onrender.com';

const state = {
    platform: 'MyAnimeList',
    username: '',
    startDate: getTodayMidnight(),
    calendarData: null,
    allEpisodes: [],
    loadedMonths: new Set(), // Tracks "YYYY-MM"
    titleLang: localStorage.getItem('anime_cal_title_lang') || 'english' // 'english' or 'romaji'
};

function getTodayMidnight() {
    const d = new Date();
    d.setHours(0, 0, 0, 0);
    return d;
}

function getAnimeDisplayTitle(ep) {
    if (!ep) return '';
    if (state.titleLang === 'romaji') {
        return ep.titleRomaji || ep.titleEnglish || ep.displayTitle || '';
    } else {
        return ep.titleEnglish || ep.titleRomaji || ep.displayTitle || '';
    }
}

function updateTitleLangButton() {
    const btnLabel = document.getElementById('titleLangLabel');
    if (btnLabel) {
        btnLabel.textContent = (state.titleLang === 'romaji') ? '🇯🇵 Romaji' : '🇬🇧 English';
    }
}

document.addEventListener('DOMContentLoaded', () => {
    initTheme();
    loadStoredUser();
    updateTitleLangButton();
    setupEventListeners();
    startLiveTickers();

    restoreLocalCache();

    if (state.username) {
        handleLoadCalendar(false);
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

function restoreLocalCache() {
    try {
        const savedUser = localStorage.getItem('anime_cal_user') || state.username;
        const savedPlat = localStorage.getItem('anime_cal_plat') || state.platform;
        if (!savedUser) return;
        const cacheKey = `anime_cal_cache_${savedPlat}_${savedUser}`;
        const raw = localStorage.getItem(cacheKey);
        if (raw) {
            const parsed = JSON.parse(raw);
            if (parsed && Array.isArray(parsed.episodes) && parsed.episodes.length > 0) {
                state.allEpisodes = parsed.episodes;
                state.loadedMonths = new Set(parsed.loadedMonths || []);
                renderSchedule();
            }
        }
    } catch (e) {
        console.warn("Could not restore local cache:", e);
    }
}

function saveLocalCache() {
    try {
        if (!state.username || state.allEpisodes.length === 0) return;
        const cacheKey = `anime_cal_cache_${state.platform}_${state.username}`;
        localStorage.setItem(cacheKey, JSON.stringify({
            episodes: state.allEpisodes,
            loadedMonths: Array.from(state.loadedMonths),
            time: Date.now()
        }));
    } catch (e) {
        console.warn("Storage quota exceeded or storage disabled:", e);
    }
}

function setupEventListeners() {
    const userForm = document.getElementById('userForm');
    if (userForm) {
        userForm.addEventListener('submit', (e) => {
            e.preventDefault();
            handleLoadCalendar(true);
        });
    }

    const loadBtn = document.getElementById('loadBtn');
    if (loadBtn) {
        loadBtn.addEventListener('click', (e) => {
            e.preventDefault();
            handleLoadCalendar(true);
        });
    }

    const platformSelect = document.getElementById('platformSelect');
    if (platformSelect) {
        platformSelect.addEventListener('change', () => {
            handleLoadCalendar(true);
        });
    }

    const titleToggle = document.getElementById('titleLangToggle');
    if (titleToggle) {
        titleToggle.addEventListener('click', () => {
            state.titleLang = (state.titleLang === 'english' ? 'romaji' : 'english');
            localStorage.setItem('anime_cal_title_lang', state.titleLang);
            updateTitleLangButton();
            renderSchedule();
        });
    }

    document.getElementById('themeToggle').addEventListener('click', () => {
        const current = document.documentElement.getAttribute('data-theme');
        const next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('anime_cal_theme', next);
        document.getElementById('themeIcon').textContent = next === 'dark' ? '🌙' : '☀️';
    });

    // Navigation Controls: << < Today > >>
    document.getElementById('navWeekPrevBtn').addEventListener('click', () => {
        shiftDays(-7);
    });

    document.getElementById('navDayPrevBtn').addEventListener('click', () => {
        shiftDays(-1);
    });

    document.getElementById('navTodayBtn').addEventListener('click', () => {
        state.startDate = getTodayMidnight();
        renderSchedule();
        checkAndFetchMonthIfNeeded();
    });

    document.getElementById('navDayNextBtn').addEventListener('click', () => {
        shiftDays(1);
    });

    document.getElementById('navWeekNextBtn').addEventListener('click', () => {
        shiftDays(7);
    });

    document.getElementById('exportIcsBtn').addEventListener('click', () => {
        if (!state.allEpisodes || state.allEpisodes.length === 0) {
            alert('No episodes loaded to export. Please load your schedule first.');
            return;
        }
        const currentY = state.startDate.getFullYear();
        const currentM = state.startDate.getMonth() + 1;
        const exportUrl = `${BACKEND_API_URL}/api/export/ics?platform=${encodeURIComponent(state.platform)}&username=${encodeURIComponent(state.username)}&year=${currentY}&month=${currentM}`;
        window.open(exportUrl, '_blank');
    });

    document.getElementById('detailModal').addEventListener('click', (e) => {
        if (e.target.id === 'detailModal') {
            closeModal();
        }
    });
}

function shiftDays(n) {
    const next = new Date(state.startDate);
    next.setDate(next.getDate() + n);
    state.startDate = next;
    renderSchedule();
    checkAndFetchMonthIfNeeded();
}

async function checkAndFetchMonthIfNeeded() {
    const startY = state.startDate.getFullYear();
    const startM = state.startDate.getMonth() + 1;
    const keyStart = `${startY}-${startM}`;

    const endDate = new Date(state.startDate);
    endDate.setDate(endDate.getDate() + 6);
    const endY = endDate.getFullYear();
    const endM = endDate.getMonth() + 1;
    const keyEnd = `${endY}-${endM}`;

    let needsRerender = false;
    if (!state.loadedMonths.has(keyStart)) {
        await fetchMonthData(startY, startM, false);
        needsRerender = true;
    }
    if (!state.loadedMonths.has(keyEnd)) {
        await fetchMonthData(endY, endM, false);
        needsRerender = true;
    }
    if (needsRerender) {
        renderSchedule();
        saveLocalCache();
    }
}

async function handleLoadCalendar(showSpinner = true) {
    let user = document.getElementById('usernameInput').value.trim();
    const plat = document.getElementById('platformSelect').value;

    if (!user) {
        if (plat === 'All') {
            user = 'All Airing Anime';
        } else {
            user = 'wooles';
            document.getElementById('usernameInput').value = 'wooles';
        }
    }

    state.username = user;
    state.platform = plat;

    localStorage.setItem('anime_cal_user', user);
    localStorage.setItem('anime_cal_plat', plat);

    if (showSpinner) {
        state.allEpisodes = [];
        state.loadedMonths.clear();
        showLoading(`Loading anime schedule for ${state.username} (${state.platform})...`);
    } else if (state.allEpisodes.length === 0) {
        restoreLocalCache();
    }

    try {
        const y = state.startDate.getFullYear();
        const m = state.startDate.getMonth() + 1;
        
        // Fetch current month + next month for seamless navigation
        await fetchMonthData(y, m, false, true);
        
        const nextMonthDate = new Date(state.startDate);
        nextMonthDate.setMonth(nextMonthDate.getMonth() + 1);
        await fetchMonthData(nextMonthDate.getFullYear(), nextMonthDate.getMonth() + 1, false, true);

        renderSchedule();
        saveLocalCache();
    } catch (err) {
        console.error("Fetch schedule error:", err);
        if (showSpinner) {
            alert(err.message);
        }
    } finally {
        if (showSpinner) {
            hideLoading();
        }
    }
}

async function fetchMonthData(year, month, reRender = true, forceRefresh = false) {
    if (!state.username) return;
    const monthKey = `${year}-${month}`;
    if (!forceRefresh && state.loadedMonths.has(monthKey)) return;

    const isSameOrigin = window.location.origin.includes('onrender.com') || window.location.origin.includes('localhost:5000');
    const apiBase = isSameOrigin ? '' : BACKEND_API_URL;
    const refreshParam = forceRefresh ? '&refresh=true' : '';
    const primaryUrl = `${apiBase}/api/calendar/month?platform=${encodeURIComponent(state.platform)}&username=${encodeURIComponent(state.username)}&year=${year}&month=${month}${refreshParam}`;

    let response;
    try {
        response = await fetch(primaryUrl);
    } catch (fetchErr) {
        throw new Error(`Cannot connect to server. If the server was sleeping, it takes ~30 seconds to wake up. Please wait a moment and click Load again.`);
    }

    if (!response.ok) {
        const errJson = await response.json().catch(() => ({}));
        throw new Error(errJson.error || `Server error (${response.status})`);
    }

    const data = await response.json();
    state.calendarData = data;
    state.loadedMonths.add(monthKey);

    // Merge all episodes by unique identity
    (data.days || []).forEach(d => {
        (d.episodes || []).forEach(ep => {
            const existingIdx = state.allEpisodes.findIndex(e => 
                (e.id && ep.id && e.id === ep.id) ||
                (e.malId && ep.malId && e.malId === ep.malId && e.episodeNumber === ep.episodeNumber) ||
                (e.aniListId && ep.aniListId && e.aniListId === ep.aniListId && e.episodeNumber === ep.episodeNumber)
            );

            if (existingIdx >= 0) {
                state.allEpisodes[existingIdx] = ep;
            } else {
                state.allEpisodes.push(ep);
            }
        });
    });

    if (reRender) {
        renderSchedule();
    }
}

// ==================== 7-COLUMN WEEKLY SCHEDULE RENDERER ====================

function renderSchedule() {
    const grid = document.getElementById('scheduleGrid');
    document.getElementById('initialState').classList.add('hidden');
    grid.classList.remove('hidden');

    // Update Stats Bar
    if (state.calendarData) {
        const statsBar = document.getElementById('calendarStats');
        statsBar.classList.remove('hidden');
        document.getElementById('statsUsername').textContent = state.username;
        document.getElementById('statsPlatform').textContent = state.platform;
        document.getElementById('statsWatchingCount').textContent = state.calendarData.totalWatchingAnime || state.allEpisodes.length;
        
        const tzName = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Local';
        const tzBadge = document.getElementById('timezoneBadge');
        if (tzBadge) tzBadge.textContent = tzName;
    }

    grid.innerHTML = '';

    const todayMid = getTodayMidnight();
    const todayMidTime = todayMid.getTime();
    const now = new Date();

    const monthShortNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    const dayShortNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    // Render 7 consecutive day columns starting from state.startDate
    for (let i = 0; i < 7; i++) {
        const colDate = new Date(state.startDate);
        colDate.setDate(colDate.getDate() + i);

        const colYear = colDate.getFullYear();
        const colMonth = colDate.getMonth(); // 0-11
        const colDay = colDate.getDate();
        const colDayOfWeek = colDate.getDay();

        const isToday = (colDate.getTime() === todayMidTime);

        // Header text e.g. "Wed Aug 19"
        const headerTitle = `${dayShortNames[colDayOfWeek]} ${monthShortNames[colMonth]} ${colDay}`;

        const colEl = document.createElement('div');
        colEl.className = 'day-column' + (isToday ? ' is-today' : '');

        // Column Header
        const colHeader = document.createElement('div');
        colHeader.className = 'day-column-header';

        const titleSpan = document.createElement('span');
        titleSpan.className = 'day-header-title';
        titleSpan.textContent = headerTitle;
        colHeader.appendChild(titleSpan);

        const countSpan = document.createElement('span');
        countSpan.className = 'day-ep-count';
        colHeader.appendChild(countSpan);

        colEl.appendChild(colHeader);

        // Column Body (Episode Cards Container)
        const colBody = document.createElement('div');
        colBody.className = 'day-column-body';

        // Filter all episodes matching this local date
        const dayEps = state.allEpisodes.filter(ep => {
            const epDate = new Date(ep.airingAt);
            return epDate.getFullYear() === colYear &&
                   epDate.getMonth() === colMonth &&
                   epDate.getDate() === colDay;
        }).sort((a, b) => new Date(a.airingAt) - new Date(b.airingAt));

        countSpan.textContent = dayEps.length > 0 ? `${dayEps.length} eps` : '';

        // If today: calculate chronological insertion of the Blue "NOW" Indicator
        let indicatorInserted = false;

        dayEps.forEach(ep => {
            const epAirDate = new Date(ep.airingAt);

            // Insert live NOW indicator before the first upcoming episode of today
            if (isToday && !indicatorInserted && epAirDate > now) {
                colBody.appendChild(createTimeIndicatorElement());
                indicatorInserted = true;
            }

            colBody.appendChild(createAnimeCard(ep));
        });

        // If today and all episodes have aired or no episodes, append indicator at bottom
        if (isToday && !indicatorInserted) {
            colBody.appendChild(createTimeIndicatorElement());
        }

        colEl.appendChild(colBody);
        grid.appendChild(colEl);
    }

    // On mobile / tablet viewports, smoothly center today's column in the viewport
    setTimeout(() => {
        const todayCol = grid.querySelector('.day-column.is-today');
        if (todayCol && window.innerWidth <= 1024) {
            todayCol.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
        }
    }, 60);
}

function createTimeIndicatorElement() {
    const indicator = document.createElement('div');
    indicator.className = 'current-time-indicator';
    indicator.id = 'currentTimeIndicator';

    const pill = document.createElement('div');
    pill.className = 'time-indicator-pill';
    pill.textContent = getLiveTimeFormatted();

    const line = document.createElement('div');
    line.className = 'time-indicator-line';

    indicator.appendChild(pill);
    indicator.appendChild(line);
    return indicator;
}

function createAnimeCard(ep) {
    const card = document.createElement('div');
    const statusClass = (ep.listStatus === 'PlanToWatch' || ep.listStatus === 'PLANNING') ? 'status-plantowatch' : 'status-watching';
    card.className = `anime-card ${statusClass}`;
    
    const displayTitle = getAnimeDisplayTitle(ep);
    card.title = `${displayTitle} (Episode ${ep.episodeNumber})`;
    card.addEventListener('click', () => openDetailModal(ep));

    // Cover Poster
    const poster = document.createElement('img');
    poster.className = 'card-poster';
    poster.src = ep.coverImage || 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="40" height="56"><rect width="100%" height="100%" fill="%23222"/></svg>';
    poster.alt = displayTitle;
    poster.loading = 'lazy';
    card.appendChild(poster);

    // Content container
    const content = document.createElement('div');
    content.className = 'card-content';

    // Top row: Airing Time & Countdown
    const timeRow = document.createElement('div');
    timeRow.className = 'card-time-row';

    const airTimeSpan = document.createElement('span');
    airTimeSpan.className = 'card-air-time';
    airTimeSpan.textContent = formatLocalTime(ep.airingAt);
    timeRow.appendChild(airTimeSpan);

    const countdownSpan = document.createElement('span');
    const countdownInfo = getCountdownInfo(ep.airingAt);
    countdownSpan.className = `card-countdown ${countdownInfo.statusClass}`;
    countdownSpan.textContent = countdownInfo.text;
    timeRow.appendChild(countdownSpan);

    content.appendChild(timeRow);

    // Title
    const titleEl = document.createElement('div');
    titleEl.className = 'card-title';
    titleEl.textContent = displayTitle;
    content.appendChild(titleEl);

    // Bottom row: Format + Bookmark
    const footer = document.createElement('div');
    footer.className = 'card-footer';

    const formatBadge = document.createElement('span');
    formatBadge.className = 'card-ep-badge';
    formatBadge.textContent = `EP${ep.episodeNumber} • ${ep.format || 'TV'} (JP)`;
    footer.appendChild(formatBadge);

    const bookmark = document.createElement('span');
    const isPlan = (ep.listStatus === 'PlanToWatch' || ep.listStatus === 'PLANNING');
    bookmark.className = `card-bookmark-icon ${isPlan ? 'plantowatch' : 'watching'}`;
    bookmark.textContent = isPlan ? '🔖' : '📺';
    bookmark.title = isPlan ? 'Plan to Watch' : 'Watching';
    footer.appendChild(bookmark);

    content.appendChild(footer);
    card.appendChild(content);

    return card;
}

// ==================== LIVE COUNTDOWN & CLOCK TICKERS ====================

function startLiveTickers() {
    function updateClock() {
        const clockBadge = document.getElementById('localClockBadge');
        if (clockBadge) {
            const now = new Date();
            clockBadge.textContent = `⏰ ${now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })}`;
        }

        // Update live indicator pill text
        const pill = document.querySelector('#currentTimeIndicator .time-indicator-pill');
        if (pill) {
            pill.textContent = getLiveTimeFormatted();
        }

        // Update all countdown badges on cards
        document.querySelectorAll('.anime-card').forEach(card => {
            // Re-eval countdowns
        });
    }

    updateClock();
    setInterval(updateClock, 1000);

    // Keepalive ping to keep Render server awake 24/7 while tab is open
    setInterval(() => {
        fetch(`${BACKEND_API_URL}/api/status`).catch(() => {});
    }, 4 * 60 * 1000);
}

function getLiveTimeFormatted() {
    const now = new Date();
    const timeStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false });
    const tzShort = Intl.DateTimeFormat().resolvedOptions().timeZone.split('/')[1] || 'Local';
    return `${timeStr} ${tzShort}`;
}

function getCountdownInfo(airingAt) {
    if (!airingAt) return { text: '', statusClass: '' };
    const now = new Date();
    const air = new Date(airingAt);
    const diffSec = Math.floor((air.getTime() - now.getTime()) / 1000);

    if (diffSec > 0) {
        const days = Math.floor(diffSec / 86400);
        const hours = Math.floor((diffSec % 86400) / 3600);
        const mins = Math.floor((diffSec % 3600) / 60);

        if (days > 0) {
            return { text: `${days}d ${hours}h`, statusClass: '' };
        } else if (hours > 0) {
            return { text: `${hours}h ${mins}m`, statusClass: '' };
        } else {
            return { text: `${mins}m`, statusClass: '' };
        }
    } else {
        return { text: '', statusClass: '' };
    }
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

// ==================== DETAIL MODAL ====================

function openDetailModal(ep) {
    const displayTitle = getAnimeDisplayTitle(ep);
    document.getElementById('modalAnimeTitle').textContent = displayTitle;
    const subTitle = (state.titleLang === 'romaji') ? (ep.titleEnglish || ep.displayTitle) : (ep.titleRomaji || ep.displayTitle);
    document.getElementById('modalRomajiTitle').textContent = (subTitle && subTitle !== displayTitle) ? `Alt: ${subTitle}` : '';
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
        minute: '2-digit',
        hour12: false
    }) + ` (${tzName})`;

    document.getElementById('modalFormat').textContent = `Format: ${ep.format || 'TV'}`;
    const isPlan = (ep.listStatus === 'PlanToWatch' || ep.listStatus === 'PLANNING');
    document.getElementById('modalStatusBadge').textContent = isPlan ? '🔖 Plan to Watch' : '📺 Watching';
    document.getElementById('modalProgress').textContent = `Progress: ${ep.userProgress || 0}/${ep.totalEpisodes || '?'}`;
    document.getElementById('modalScore').textContent = ep.averageScore ? `⭐ Score: ${ep.averageScore.toFixed(0)}%` : '⭐ No score';

    document.getElementById('modalSynopsis').innerHTML = ep.synopsis ? stripHtml(ep.synopsis) : 'No description available for this series.';

    // STREAMING SERVICES (Crunchyroll, Disney+, Netflix, HBO Max, Prime Video, ADN, etc.)
    const streamContainer = document.getElementById('modalStreamingLinks');
    if (streamContainer) {
        streamContainer.innerHTML = '';
        const searchTitle = ep.titleEnglish || ep.displayTitle || ep.titleRomaji;
        const q = encodeURIComponent(searchTitle);

        const popularPlatforms = [
            { id: 'crunchyroll', name: 'Crunchyroll', icon: '🟠', urlMatch: 'crunchyroll.com', searchUrl: `https://www.crunchyroll.com/search?q=${q}` },
            { id: 'disneyplus', name: 'Disney+', icon: '🏰', urlMatch: 'disneyplus.com', searchUrl: `https://www.disneyplus.com/search?q=${q}` },
            { id: 'netflix', name: 'Netflix', icon: '🔴', urlMatch: 'netflix.com', searchUrl: `https://www.netflix.com/search?q=${q}` },
            { id: 'max', name: 'Max', icon: '🟣', urlMatch: 'max.com', searchUrl: `https://www.max.com/search?q=${q}` },
            { id: 'primevideo', name: 'Prime Video', icon: '📦', urlMatch: 'primevideo.com', searchUrl: `https://www.amazon.com/s?k=${q}&i=instant-video` },
            { id: 'adn', name: 'ADN', icon: '🇫🇷', urlMatch: 'animationdigitalnetwork.fr', searchUrl: `https://animationdigitalnetwork.fr/video?search=${q}` },
            { id: 'hidive', name: 'HIDIVE', icon: '💎', urlMatch: 'hidive.com', searchUrl: `https://www.hidive.com/search?q=${q}` },
            { id: 'bilibili', name: 'Bilibili', icon: '📺', urlMatch: 'bilibili', searchUrl: `https://www.bilibili.tv/en/search-result?q=${q}` },
            { id: 'youtube', name: 'YouTube', icon: '▶️', urlMatch: 'youtube.com', searchUrl: `https://www.youtube.com/results?search_query=${q}+anime` }
        ];

        const renderedPlatforms = new Set();

        // 1. Direct verified streaming links from AniList / API
        if (Array.isArray(ep.streamingLinks) && ep.streamingLinks.length > 0) {
            ep.streamingLinks.forEach(link => {
                const siteName = link.site || 'Stream';
                const directUrl = link.url;
                if (!directUrl) return;

                // Find matching platform definition
                const matchedDef = popularPlatforms.find(p => 
                    siteName.toLowerCase().includes(p.id) || 
                    siteName.toLowerCase().includes(p.name.toLowerCase()) || 
                    directUrl.toLowerCase().includes(p.urlMatch)
                );

                const btn = document.createElement('a');
                btn.href = directUrl;
                btn.target = '_blank';
                btn.rel = 'noopener noreferrer';

                if (matchedDef) {
                    btn.className = `streaming-btn ${matchedDef.id}`;
                    btn.innerHTML = `<span>${matchedDef.icon}</span> <span>${matchedDef.name}</span>`;
                    renderedPlatforms.add(matchedDef.id);
                } else {
                    btn.className = 'streaming-btn';
                    btn.innerHTML = `<span>📺</span> <span>${siteName}</span>`;
                }

                streamContainer.appendChild(btn);
            });
        }

        // 2. For requested popular streaming services not directly linked, add quick search button
        const priorityServices = ['crunchyroll', 'disneyplus', 'netflix', 'max', 'primevideo', 'adn'];
        priorityServices.forEach(pId => {
            if (!renderedPlatforms.has(pId)) {
                const p = popularPlatforms.find(x => x.id === pId);
                if (p) {
                    const btn = document.createElement('a');
                    btn.href = p.searchUrl;
                    btn.target = '_blank';
                    btn.rel = 'noopener noreferrer';
                    btn.className = `streaming-btn search-fallback`;
                    btn.title = `Search for "${searchTitle}" on ${p.name}`;
                    btn.innerHTML = `<span>${p.icon}</span> <span>${p.name} 🔍</span>`;
                    streamContainer.appendChild(btn);
                }
            }
        });
    }

    // Database Links
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

function addLink(container, url, text) {
    const a = document.createElement('a');
    a.href = url;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.textContent = text;
    container.appendChild(a);
}

function closeModal() {
    document.getElementById('detailModal').classList.add('hidden');
}

function stripHtml(html) {
    if (!html) return '';
    const tmp = document.createElement('div');
    tmp.innerHTML = html;
    return tmp.textContent || tmp.innerText || '';
}

function showLoading(msg = 'Loading...') {
    document.getElementById('loadingText').textContent = msg;
    document.getElementById('loadingOverlay').classList.remove('hidden');
}

function hideLoading() {
    document.getElementById('loadingOverlay').classList.add('hidden');
}
