// Anime Schedule - Weekly Airing Timeline (.NET 8 + Tenrai.Net)

const BACKEND_API_URL = 'https://livechart-anime-tracker.onrender.com';

const state = {
    platform: 'AniList',
    username: '',
    startDate: getTodayMidnight(),
    calendarData: null,
    allEpisodes: [],
    loadedMonths: new Set() // Tracks "YYYY-MM"
};

function getTodayMidnight() {
    const d = new Date();
    d.setHours(0, 0, 0, 0);
    return d;
}

document.addEventListener('DOMContentLoaded', () => {
    initTheme();
    loadStoredUser();
    setupEventListeners();
    startLiveTickers();

    if (state.username) {
        handleLoadCalendar();
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

function setupEventListeners() {
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
    }
}

async function handleLoadCalendar() {
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
    state.allEpisodes = [];
    state.loadedMonths = new Set();

    localStorage.setItem('anime_cal_user', user);
    localStorage.setItem('anime_cal_plat', plat);

    showLoading(`Loading anime schedule for ${state.username} (${state.platform})...`);

    try {
        const y = state.startDate.getFullYear();
        const m = state.startDate.getMonth() + 1;
        
        // Fetch current month + next month for seamless navigation
        await fetchMonthData(y, m, false, true);
        
        const nextMonthDate = new Date(state.startDate);
        nextMonthDate.setMonth(nextMonthDate.getMonth() + 1);
        await fetchMonthData(nextMonthDate.getFullYear(), nextMonthDate.getMonth() + 1, false, true);

        renderSchedule();
    } catch (err) {
        console.error("Fetch schedule error:", err);
        alert(err.message);
    } finally {
        hideLoading();
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
    card.title = `${ep.displayTitle} (Episode ${ep.episodeNumber})`;
    card.addEventListener('click', () => openDetailModal(ep));

    // Cover Poster
    const poster = document.createElement('img');
    poster.className = 'card-poster';
    poster.src = ep.coverImage || 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="40" height="56"><rect width="100%" height="100%" fill="%23222"/></svg>';
    poster.alt = ep.displayTitle;
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
    titleEl.textContent = ep.displayTitle;
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
    const timeStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true });
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
    } else if (diffSec > -3600) {
        return { text: 'Airing Now', statusClass: 'live' };
    } else {
        return { text: 'Aired', statusClass: 'aired' };
    }
}

function formatLocalTime(isoStringOrDate) {
    if (!isoStringOrDate) return '--:--';
    try {
        const d = new Date(isoStringOrDate);
        return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true });
    } catch {
        return '--:--';
    }
}

// ==================== DETAIL MODAL ====================

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
    const isPlan = (ep.listStatus === 'PlanToWatch' || ep.listStatus === 'PLANNING');
    document.getElementById('modalStatusBadge').textContent = isPlan ? '🔖 Plan to Watch' : '📺 Watching';
    document.getElementById('modalProgress').textContent = `Progress: ${ep.userProgress || 0}/${ep.totalEpisodes || '?'}`;
    document.getElementById('modalScore').textContent = ep.averageScore ? `⭐ Score: ${ep.averageScore.toFixed(0)}%` : '⭐ No score';

    document.getElementById('modalSynopsis').innerHTML = ep.synopsis ? stripHtml(ep.synopsis) : 'No description available for this series.';

    // Links
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
