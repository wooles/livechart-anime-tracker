// Anime Watching Calendar - Frontend Logic (English & Full Width & Local Time)

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
    const savedTheme = localStorage.getItem('theme') || 'dark';
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
        localStorage.setItem('theme', next);
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

    // Export ICS
    document.getElementById('exportIcsBtn').addEventListener('click', () => {
        if (!state.username) {
            alert('Please enter a username first.');
            document.getElementById('usernameInput').focus();
            return;
        }
        const url = `/api/export/ics?platform=${encodeURIComponent(state.platform)}&username=${encodeURIComponent(state.username)}&year=${state.year}&month=${state.month}`;
        window.location.href = url;
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

async function fetchCalendar() {
    showLoading(`Loading calendar from ${state.platform} for ${state.username}...`);
    try {
        const url = `/api/calendar/month?platform=${encodeURIComponent(state.platform)}&username=${encodeURIComponent(state.username)}&year=${state.year}&month=${state.month}`;
        const res = await fetch(url);
        if (!res.ok) {
            const err = await res.json();
            throw new Error(err.error || 'Failed to load calendar.');
        }

        const data = await res.json();
        state.calendarData = data;
        renderCalendar(data);
    } catch (err) {
        console.error(err);
        alert('Error: ' + err.message);
    } finally {
        hideLoading();
    }
}

function formatLocalTime(isoString) {
    if (!isoString) return '--:--';
    try {
        const d = new Date(isoString);
        return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false });
    } catch {
        return '--:--';
    }
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

        // Apply dynamic density scaling based on episode count in this cell
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
