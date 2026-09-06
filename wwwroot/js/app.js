// Anime Schedule - Weekly Airing Timeline (.NET 8 + Tenrai.Net)

const BACKEND_API_URL = 'https://livechart-anime-tracker.onrender.com';

const state = {
    platform: 'MyAnimeList',
    username: '',
    startDate: getTodayMidnight(),
    calendarData: null,
    allEpisodes: [],
    loadedMonths: new Set(), // Tracks "YYYY-MM"
    titleLang: localStorage.getItem('anime_cal_title_lang') || 'english', // 'english' or 'romaji'
    hiddenAnimeMap: new Map() // key -> animeObj
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

// ==================== HIDDEN ANIME STATE & HANDLERS ====================

function getAnimeKey(ep) {
    if (!ep) return '';
    if (ep.aniListId) return `ani_${ep.aniListId}`;
    if (ep.malId) return `mal_${ep.malId}`;
    if (ep.kitsuId) return `kitsu_${ep.kitsuId}`;
    const t = (ep.titleRomaji || ep.titleEnglish || ep.displayTitle || '').trim().toLowerCase();
    return `title_${t}`;
}

function isAnimeHidden(ep) {
    if (!ep || !state.hiddenAnimeMap || state.hiddenAnimeMap.size === 0) return false;
    const key = getAnimeKey(ep);
    if (state.hiddenAnimeMap.has(key)) return true;
    if (ep.aniListId && state.hiddenAnimeMap.has(`ani_${ep.aniListId}`)) return true;
    if (ep.malId && state.hiddenAnimeMap.has(`mal_${ep.malId}`)) return true;
    return false;
}

function loadHiddenAnime() {
    try {
        const raw = localStorage.getItem('anime_cal_hidden_series');
        if (raw) {
            const list = JSON.parse(raw);
            if (Array.isArray(list)) {
                state.hiddenAnimeMap = new Map(list.map(item => [item.key, item]));
            }
        }
    } catch (e) {
        console.warn("Could not load hidden anime:", e);
        state.hiddenAnimeMap = new Map();
    }
}

function saveHiddenAnime() {
    try {
        const arr = Array.from(state.hiddenAnimeMap.values());
        localStorage.setItem('anime_cal_hidden_series', JSON.stringify(arr));
    } catch (e) {
        console.warn("Could not save hidden anime:", e);
    }
}

function updateHiddenAnimeButton() {
    const count = state.hiddenAnimeMap ? state.hiddenAnimeMap.size : 0;
    const labelEl = document.getElementById('hiddenAnimeLabel');
    if (labelEl) {
        labelEl.textContent = `Hidden (${count})`;
    }
    const countModalEl = document.getElementById('hiddenModalCount');
    if (countModalEl) {
        countModalEl.textContent = count;
    }
}

function hideAnimeSeries(ep) {
    if (!ep) return;
    const key = getAnimeKey(ep);
    const titleEng = ep.titleEnglish || ep.titleRomaji || ep.displayTitle || '';
    const titleRom = ep.titleRomaji || ep.titleEnglish || ep.displayTitle || '';
    const animeObj = {
        key: key,
        titleEnglish: titleEng,
        titleRomaji: titleRom,
        displayTitle: titleEng || titleRom,
        coverImage: ep.coverImage || '',
        aniListId: ep.aniListId || null,
        malId: ep.malId || null,
        kitsuId: ep.kitsuId || null,
        format: ep.format || 'TV',
        totalEpisodes: ep.totalEpisodes || null,
        hiddenAt: Date.now()
    };

    state.hiddenAnimeMap.set(key, animeObj);
    saveHiddenAnime();
    updateHiddenAnimeButton();
    renderSchedule();
}

function restoreHiddenAnime(key) {
    if (state.hiddenAnimeMap.has(key)) {
        state.hiddenAnimeMap.delete(key);
        saveHiddenAnime();
        updateHiddenAnimeButton();
        renderHiddenAnimeList();
        renderSchedule();
    }
}

function restoreAllHiddenAnime() {
    if (state.hiddenAnimeMap.size === 0) return;
    state.hiddenAnimeMap.clear();
    saveHiddenAnime();
    updateHiddenAnimeButton();
    renderHiddenAnimeList();
    renderSchedule();
}

function openHiddenAnimeModal() {
    renderHiddenAnimeList();
    const modal = document.getElementById('hiddenAnimeModal');
    if (modal) {
        modal.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
    }
}

function closeHiddenAnimeModal() {
    const modal = document.getElementById('hiddenAnimeModal');
    if (modal) {
        modal.classList.add('hidden');
        document.body.style.overflow = '';
    }
}

function renderHiddenAnimeList() {
    const listEl = document.getElementById('hiddenAnimeList');
    const restoreAllBtn = document.getElementById('restoreAllHiddenBtn');
    if (!listEl) return;

    listEl.innerHTML = '';
    const items = Array.from(state.hiddenAnimeMap.values());

    if (items.length === 0) {
        listEl.innerHTML = `
            <div class="hidden-empty-state">
                <div class="hidden-empty-icon">✨</div>
                <p><strong>No hidden anime</strong></p>
                <p style="font-size: 0.75rem; color: var(--text-muted); margin-top: 4px;">
                    Click the eye icon on any episode card to hide that series from the schedule.
                </p>
            </div>
        `;
        if (restoreAllBtn) {
            restoreAllBtn.style.display = 'none';
        }
        return;
    }

    if (restoreAllBtn) {
        restoreAllBtn.style.display = 'inline-flex';
        restoreAllBtn.textContent = 'Restore All';
    }

    // Sort alphabetically by current title preference
    items.sort((a, b) => {
        const titleA = state.titleLang === 'romaji' ? (a.titleRomaji || a.titleEnglish) : (a.titleEnglish || a.titleRomaji);
        const titleB = state.titleLang === 'romaji' ? (b.titleRomaji || b.titleEnglish) : (b.titleEnglish || b.titleRomaji);
        return titleA.localeCompare(titleB);
    });

    items.forEach(item => {
        const itemEl = document.createElement('div');
        itemEl.className = 'hidden-anime-item';

        const displayTitle = state.titleLang === 'romaji' 
            ? (item.titleRomaji || item.titleEnglish || item.displayTitle)
            : (item.titleEnglish || item.titleRomaji || item.displayTitle);

        const subTitle = state.titleLang === 'romaji' ? item.titleEnglish : item.titleRomaji;

        const infoDiv = document.createElement('div');
        infoDiv.className = 'hidden-anime-info';

        const posterImg = document.createElement('img');
        posterImg.className = 'hidden-anime-poster';
        posterImg.src = item.coverImage || 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="34" height="48"><rect width="100%" height="100%" fill="%23333"/></svg>';
        posterImg.alt = displayTitle;
        posterImg.loading = 'lazy';
        infoDiv.appendChild(posterImg);

        const titlesDiv = document.createElement('div');
        titlesDiv.className = 'hidden-anime-titles';

        const titleDiv = document.createElement('div');
        titleDiv.className = 'hidden-anime-title';
        titleDiv.textContent = displayTitle;
        titleDiv.title = displayTitle;
        titlesDiv.appendChild(titleDiv);

        const subDiv = document.createElement('div');
        subDiv.className = 'hidden-anime-sub';
        let subText = item.format || 'TV';
        if (item.totalEpisodes) {
            subText += ` • ${item.totalEpisodes} eps`;
        }
        if (subTitle && subTitle !== displayTitle) {
            subText += ` • ${subTitle}`;
        }
        subDiv.textContent = subText;
        titlesDiv.appendChild(subDiv);

        infoDiv.appendChild(titlesDiv);
        itemEl.appendChild(infoDiv);

        const restoreBtn = document.createElement('button');
        restoreBtn.className = 'btn-restore';
        restoreBtn.innerHTML = `↩️ Restore`;
        restoreBtn.title = 'Restore this series to schedule';
        restoreBtn.addEventListener('click', () => {
            restoreHiddenAnime(item.key);
        });
        itemEl.appendChild(restoreBtn);

        listEl.appendChild(itemEl);
    });
}

document.addEventListener('DOMContentLoaded', () => {
    initTheme();
    loadStoredUser();
    loadHiddenAnime();
    updateTitleLangButton();
    updateHiddenAnimeButton();
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
    const userInput = document.getElementById('usernameInput');
    const platSelect = document.getElementById('platformSelect');
    if (savedPlat) {
        state.platform = savedPlat;
        if (platSelect) platSelect.value = savedPlat;
    }
    if (savedPlat === 'All') {
        state.username = 'All Airing Anime';
        if (userInput) {
            userInput.value = 'All Airing Anime';
            userInput.disabled = true;
        }
    } else if (savedUser) {
        state.username = savedUser;
        if (userInput) userInput.value = savedUser;
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
                if (parsed.calendarData) {
                    state.calendarData = parsed.calendarData;
                } else {
                    state.calendarData = {
                        username: savedUser,
                        platform: savedPlat,
                        totalWatchingAnime: parsed.episodes.length
                    };
                }
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
            calendarData: state.calendarData,
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
            const userInput = document.getElementById('usernameInput');
            if (platformSelect.value === 'All') {
                if (userInput) {
                    if (userInput.value && userInput.value !== 'All Airing Anime') {
                        userInput.dataset.prevUser = userInput.value;
                    }
                    userInput.value = 'All Airing Anime';
                    userInput.disabled = true;
                }
            } else {
                if (userInput && userInput.disabled) {
                    userInput.disabled = false;
                    userInput.value = userInput.dataset.prevUser || 'wooles';
                }
            }
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
            const hModal = document.getElementById('hiddenAnimeModal');
            if (hModal && !hModal.classList.contains('hidden')) {
                renderHiddenAnimeList();
            }
        });
    }

    const hiddenBtn = document.getElementById('hiddenAnimeBtn');
    if (hiddenBtn) {
        hiddenBtn.addEventListener('click', () => {
            openHiddenAnimeModal();
        });
    }

    const hiddenModal = document.getElementById('hiddenAnimeModal');
    if (hiddenModal) {
        hiddenModal.addEventListener('click', (e) => {
            if (e.target.id === 'hiddenAnimeModal') {
                closeHiddenAnimeModal();
            }
        });
    }

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            closeModal();
            closeHiddenAnimeModal();
        }
    });

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

    let resizeTimer = null;
    window.addEventListener('resize', () => {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            if (state.allEpisodes && state.allEpisodes.length > 0) {
                renderSchedule();
            }
        }, 120);
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

    if (plat === 'All') {
        user = 'All Airing Anime';
    } else if (!user || user === 'All Airing Anime') {
        user = 'wooles';
        document.getElementById('usernameInput').value = 'wooles';
    }

    state.username = user;
    state.platform = plat;

    localStorage.setItem('anime_cal_user', user);
    localStorage.setItem('anime_cal_plat', plat);

    if (showSpinner) {
        state.allEpisodes = [];
        state.loadedMonths.clear();
        showLoading(`Loading anime schedule for ${state.username} (${state.platform})...`);
    } else {
        restoreLocalCache();
    }

    try {
        const y = state.startDate.getFullYear();
        const m = state.startDate.getMonth() + 1;
        
        const nextMonthDate = new Date(state.startDate);
        nextMonthDate.setMonth(nextMonthDate.getMonth() + 1);
        const nextY = nextMonthDate.getFullYear();
        const nextM = nextMonthDate.getMonth() + 1;

        // Fetch current month + next month concurrently in parallel
        await Promise.all([
            fetchMonthData(y, m, false, showSpinner),
            fetchMonthData(nextY, nextM, false, showSpinner)
        ]);

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

    // Update Stats Bar instantly
    const statsBar = document.getElementById('calendarStats');
    if (statsBar) {
        statsBar.classList.remove('hidden');
        document.getElementById('statsUsername').textContent = (state.platform === 'All') ? 'All Airing Anime' : (state.username || 'wooles');
        document.getElementById('statsPlatform').textContent = (state.platform === 'All') ? 'AniList Airing Schedule' : (state.platform || 'MyAnimeList');
        const count = state.calendarData?.totalWatchingAnime || state.allEpisodes.length || 0;
        document.getElementById('statsWatchingCount').textContent = count;
        
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

    // First, analyze all 7 visible days to determine episode counts & optimal scaling density
    const daysData = [];
    let maxVisibleEps = 0;

    for (let i = 0; i < 7; i++) {
        const colDate = new Date(state.startDate);
        colDate.setDate(colDate.getDate() + i);

        const colYear = colDate.getFullYear();
        const colMonth = colDate.getMonth(); // 0-11
        const colDay = colDate.getDate();
        const colDayOfWeek = colDate.getDay();
        const isToday = (colDate.getTime() === todayMidTime);

        // Filter all episodes matching this local date and exclude hidden anime
        const dayEps = state.allEpisodes.filter(ep => {
            if (isAnimeHidden(ep)) return false;
            const epDate = new Date(ep.airingAt);
            return epDate.getFullYear() === colYear &&
                   epDate.getMonth() === colMonth &&
                   epDate.getDate() === colDay;
        }).sort((a, b) => new Date(a.airingAt) - new Date(b.airingAt));

        if (dayEps.length > maxVisibleEps) {
            maxVisibleEps = dayEps.length;
        }

        daysData.push({
            colDate,
            colYear,
            colMonth,
            colDay,
            colDayOfWeek,
            isToday,
            headerTitle: `${dayShortNames[colDayOfWeek]} ${monthShortNames[colMonth]} ${colDay}`,
            dayEps
        });
    }

    // Dynamic uniform card height & density scaling based on available viewport height
    const scheduleWrapper = document.querySelector('.schedule-wrapper');
    const wrapperHeight = (scheduleWrapper && scheduleWrapper.clientHeight > 100)
        ? scheduleWrapper.clientHeight
        : Math.max(300, window.innerHeight - 85);

    // Calculate maximum workload across visible days
    // Count the live NOW time indicator (if present on today) as equivalent to ~0.35 of a slot
    let maxWorkload = 0;
    daysData.forEach(dayInfo => {
        let load = dayInfo.dayEps.length;
        if (dayInfo.isToday && load > 0) {
            load += 0.35;
        }
        if (load > maxWorkload) {
            maxWorkload = load;
        }
    });

    // Space taken by day column header (~34px), body padding (~8px), and wrapper paddings
    const headerAndPadding = 46;
    const availableBodyHeight = Math.max(200, wrapperHeight - headerAndPadding);

    let cardHeight;
    let cardGap = 4;
    let densityClass = 'density-normal';

    if (maxWorkload <= 0) {
        cardHeight = 90;
        densityClass = 'density-normal';
        cardGap = 5;
    } else {
        // First estimate slot height with a 4px gap
        const estSlot = (availableBodyHeight - (maxWorkload - 1) * 4) / maxWorkload;

        if (estSlot >= 76) {
            densityClass = 'density-normal';
            cardGap = 5;
            const target = (availableBodyHeight - (maxWorkload - 1) * cardGap) / maxWorkload;
            cardHeight = Math.min(94, Math.max(76, Math.floor(target)));
        } else if (estSlot >= 60) {
            densityClass = 'density-compact';
            cardGap = 4;
            const target = (availableBodyHeight - (maxWorkload - 1) * cardGap) / maxWorkload;
            cardHeight = Math.max(60, Math.floor(target));
        } else if (estSlot >= 46) {
            densityClass = 'density-dense';
            cardGap = 3;
            const target = (availableBodyHeight - (maxWorkload - 1) * cardGap) / maxWorkload;
            cardHeight = Math.max(46, Math.floor(target));
        } else {
            densityClass = 'density-ultra-dense';
            cardGap = 2;
            const target = (availableBodyHeight - (maxWorkload - 1) * cardGap) / maxWorkload;
            cardHeight = Math.max(40, Math.floor(target));
        }
    }

    grid.style.setProperty('--card-height', `${cardHeight}px`);
    grid.style.setProperty('--card-gap', `${cardGap}px`);
    grid.className = `schedule-columns-container ${densityClass}`;

    // Render 7 consecutive day columns
    daysData.forEach(dayInfo => {
        const colEl = document.createElement('div');
        colEl.className = 'day-column' + (dayInfo.isToday ? ' is-today' : '');

        // Column Header
        const colHeader = document.createElement('div');
        colHeader.className = 'day-column-header';

        const titleSpan = document.createElement('span');
        titleSpan.className = 'day-header-title';
        titleSpan.textContent = dayInfo.headerTitle;
        colHeader.appendChild(titleSpan);

        const countSpan = document.createElement('span');
        countSpan.className = 'day-ep-count';
        countSpan.textContent = dayInfo.dayEps.length > 0 ? `${dayInfo.dayEps.length} eps` : '';
        colHeader.appendChild(countSpan);

        colEl.appendChild(colHeader);

        // Column Body (Episode Cards Container)
        const colBody = document.createElement('div');
        colBody.className = 'day-column-body';

        // If today: calculate chronological insertion of the Blue "NOW" Indicator
        let indicatorInserted = false;

        dayInfo.dayEps.forEach(ep => {
            const epAirDate = new Date(ep.airingAt);

            // Insert live NOW indicator before the first upcoming episode of today
            if (dayInfo.isToday && !indicatorInserted && epAirDate > now) {
                colBody.appendChild(createTimeIndicatorElement());
                indicatorInserted = true;
            }

            colBody.appendChild(createAnimeCard(ep));
        });

        // If today and all episodes have aired or no episodes, append indicator at bottom
        if (dayInfo.isToday && !indicatorInserted) {
            colBody.appendChild(createTimeIndicatorElement());
        }

        colEl.appendChild(colBody);
        grid.appendChild(colEl);
    });

    // Auto-fit title font sizes so long titles aren't truncated
    autoFitCardTitles();
    requestAnimationFrame(() => {
        autoFitCardTitles();
    });

    // On mobile / tablet viewports, smoothly center today's column in the viewport
    setTimeout(() => {
        const todayCol = grid.querySelector('.day-column.is-today');
        if (todayCol && window.innerWidth <= 1024) {
            todayCol.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
        }
    }, 60);
}

function autoFitCardTitles() {
    const cards = document.querySelectorAll('.anime-card');
    if (!cards.length) return;

    cards.forEach(card => {
        const titleEl = card.querySelector('.card-title');
        const contentEl = card.querySelector('.card-content');
        if (!titleEl || !contentEl) return;

        // Reset previous inline styles so we measure against stylesheet baseline without transition delays
        titleEl.style.transition = 'none';
        titleEl.style.fontSize = '';
        titleEl.style.lineHeight = '';
        titleEl.style.webkitLineClamp = '';

        const contentHeight = contentEl.clientHeight;
        if (contentHeight <= 0) return;

        const timeRow = contentEl.querySelector('.card-time-row');
        const footer = contentEl.querySelector('.card-footer');

        const timeRowHeight = timeRow ? timeRow.offsetHeight : 14;
        const footerHeight = footer ? footer.offsetHeight : 14;
        // Available vertical space for the title inside card-content
        const availableHeight = Math.max(14, contentHeight - timeRowHeight - footerHeight - 1);

        const baseSize = parseFloat(window.getComputedStyle(titleEl).fontSize) || 12;
        const minSize = 7.5; // Minimum readable font size in px

        function checkFit(size) {
            const lhRatio = size <= 9.5 ? 1.08 : (size <= 11 ? 1.14 : 1.18);
            const linePx = size * lhRatio;
            // Dynamically calculate the maximum lines that can fit vertically in available space
            const maxLines = Math.max(1, Math.floor(availableHeight / linePx));

            titleEl.style.fontSize = `${size.toFixed(1)}px`;
            titleEl.style.lineHeight = String(lhRatio);
            titleEl.style.webkitLineClamp = String(maxLines);

            const overflows = (titleEl.scrollHeight > titleEl.clientHeight + 1) || 
                              (titleEl.scrollWidth > titleEl.clientWidth + 1) ||
                              (titleEl.offsetHeight > availableHeight);

            return { overflows, maxLines, lhRatio };
        }

        // 1. Check if the full title already fits at default stylesheet size
        const baseCheck = checkFit(baseSize);
        if (!baseCheck.overflows) {
            titleEl.style.fontSize = '';
            titleEl.style.lineHeight = '';
            titleEl.style.webkitLineClamp = String(baseCheck.maxLines);
            return;
        }

        // 2. Binary search to find the largest readable font size in [minSize, baseSize] that fits
        let low = minSize;
        let high = baseSize;
        let best = minSize;
        let bestLines = baseCheck.maxLines;
        let bestLh = baseCheck.lhRatio;

        for (let i = 0; i < 6; i++) {
            const mid = (low + high) / 2;
            const res = checkFit(mid);
            if (res.overflows) {
                high = mid; // Still overflows, try smaller font
            } else {
                best = mid; // Fits! Record and try slightly larger font
                bestLines = res.maxLines;
                bestLh = res.lhRatio;
                low = mid;
            }
        }

        titleEl.style.fontSize = `${best.toFixed(1)}px`;
        titleEl.style.lineHeight = String(bestLh);
        titleEl.style.webkitLineClamp = String(bestLines);
    });
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

    // Top row: Airing Time & Actions (Countdown + Hide Button)
    const timeRow = document.createElement('div');
    timeRow.className = 'card-time-row';

    const airTimeSpan = document.createElement('span');
    airTimeSpan.className = 'card-air-time';
    airTimeSpan.textContent = formatLocalTime(ep.airingAt);
    timeRow.appendChild(airTimeSpan);

    const timeActions = document.createElement('div');
    timeActions.className = 'card-time-actions';

    const countdownSpan = document.createElement('span');
    const countdownInfo = getCountdownInfo(ep.airingAt);
    countdownSpan.className = `card-countdown ${countdownInfo.statusClass}`;
    countdownSpan.textContent = countdownInfo.text;
    timeActions.appendChild(countdownSpan);

    const hideBtn = document.createElement('button');
    hideBtn.className = 'card-hide-btn';
    hideBtn.title = 'Hide this series from schedule';
    hideBtn.setAttribute('aria-label', 'Hide anime');
    hideBtn.innerHTML = `<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3.5"/><line x1="21" y1="3" x2="3" y2="21"/></svg>`;
    hideBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        hideAnimeSeries(ep);
    });
    timeActions.appendChild(hideBtn);

    timeRow.appendChild(timeActions);
    content.appendChild(timeRow);

    // Title
    const titleEl = document.createElement('div');
    titleEl.className = 'card-title';
    titleEl.textContent = displayTitle;
    titleEl.title = displayTitle;
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

// ==================== STREAMING SERVICES (OFFICIAL VECTOR LOGOS) ====================

const STREAMING_PLATFORMS = [
    {
        id: 'crunchyroll',
        name: 'Crunchyroll',
        urlMatch: 'crunchyroll.com',
        svg: `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M12 2C6.477 2 2 6.477 2 12s4.477 10 10 10 10-4.477 10-10S17.523 2 12 2zm3.82 14.18A5.999 5.999 0 0 1 12 18c-3.314 0-6-2.686-6-6 0-1.745.748-3.315 1.94-4.41a6.002 6.002 0 0 0 7.88 6.59zm.98-2.38a6.007 6.007 0 0 0-4.6-7.6 5.992 5.992 0 0 1 4.6 7.6z"/></svg>`
    },
    {
        id: 'disneyplus',
        name: 'Disney+',
        urlMatch: 'disneyplus.com',
        svg: `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M11.5 3c-4.69 0-8.5 3.81-8.5 8.5 0 2.62 1.18 4.96 3.03 6.53C5.55 16.5 5 14.5 5 12.5c0-3.59 2.91-6.5 6.5-6.5 1.79 0 3.42.73 4.6 1.91A8.448 8.448 0 0 0 11.5 3zm6.5 7h-2v2h2v2h2v-2h2v-2h-2V8h-2v2z"/></svg>`
    },
    {
        id: 'netflix',
        name: 'Netflix',
        urlMatch: 'netflix.com',
        svg: `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M4 2h4.5l5.5 14V2H18v20h-4.5L8 8v14H4V2z"/></svg>`
    },
    {
        id: 'max',
        name: 'Max',
        urlMatch: 'max.com',
        svg: `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M2.5 6.5h3.8l2.9 6.8 2.9-6.8H16v11h-2.8v-6.7l-2.6 6.7h-1.4L6.6 10.8v6.7H2.5v-11zm15.1 0h3.2l2.3 4.4 2.3-4.4h3.1l-3.8 6.5 4 6.5h-3.3l-2.3-4.6-2.3 4.6h-3.3l4-6.5-3.9-6.5z"/></svg>`
    },
    {
        id: 'primevideo',
        name: 'Prime Video',
        urlMatch: 'primevideo.com',
        svg: `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M1.5 15.5c4.5 3.5 10.5 3.5 15 1 .3-.2.5 0 .3.3-2.1 2.2-5.5 3.7-8.8 3.7-3.8 0-7.3-1.8-9.5-4.5-.2-.3 0-.7.3-.5h.1zm15.8-.2c-.3.4-.8.7-1.3.8-.2 0-.3-.1-.4-.2-.1-.1-.1-.3 0-.4.3-.4.6-.9.6-1.5 0-1.7-1.3-3-3-3s-3 1.3-3 3c0 .6.2 1.1.6 1.5.1.1.1.3 0 .4-.1.1-.2.2-.4.2-.5-.1-1-.4-1.3-.8-.5-.8-.7-1.7-.7-2.7 0-2.6 2.1-4.8 4.8-4.8s4.8 2.1 4.8 4.8c-.1 1-.3 1.9-.7 2.7zm2.4 1.1c-.2-.3-.1-.6.2-.8 1.1-.7 1.8-1.7 1.8-3 0-2.2-1.8-4-4-4s-4 1.8-4 4c0 1.3.6 2.3 1.6 3 .3.2.3.5.1.8-.2.3-.5.3-.8.1-1.3-.9-2.1-2.3-2.1-3.9 0-2.8 2.3-5.1 5.2-5.1s5.2 2.3 5.2 5.1c0 1.6-.8 3-2 3.9-.1.1-.3 0-.4-.1z"/></svg>`
    },
    {
        id: 'adn',
        name: 'ADN',
        urlMatch: 'animationdigitalnetwork.fr',
        svg: `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M12 2L2 22h4.5l1.8-4h7.4l1.8 4H22L12 2zm0 6.5l2.4 5.5H9.6L12 8.5z"/></svg>`
    },
    {
        id: 'hidive',
        name: 'HIDIVE',
        urlMatch: 'hidive.com',
        svg: `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M4 4h4v6h4V4h4v16h-4v-6H8v6H4V4z"/></svg>`
    },
    {
        id: 'hulu',
        name: 'Hulu',
        urlMatch: 'hulu.com',
        svg: `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M4 3h3.5v7.2c0 1.5.9 2.5 2.3 2.5 1.3 0 2.2-1 2.2-2.5V3h3.5v7.2c0 3.7-2.4 6.3-5.7 6.3-3.4 0-5.8-2.6-5.8-6.3V3z"/></svg>`
    },
    {
        id: 'youtube',
        name: 'YouTube',
        urlMatch: 'youtube.com',
        svg: `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M21.58 7.19c-.23-.86-.91-1.54-1.77-1.77C18.25 5 12 5 12 5s-6.25 0-7.81.42c-.86.23-1.54.91-1.77 1.77C2 8.75 2 12 2 12s0 3.25.42 4.81c.23.86.91 1.54 1.77 1.77C5.75 19 12 19 12 19s6.25 0 7.81-.42c.86-.23 1.54-.91 1.77-1.77C22 15.25 22 12 22 12s0-3.25-.42-4.81zM10 15V9l5.2 3L10 15z"/></svg>`
    }
];

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
    document.getElementById('modalAirTime').textContent = airDate.toLocaleString('en-US', { 
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

    // STREAMING SERVICES (Only show platforms where this anime is ACTUALLY streaming)
    const streamSection = document.getElementById('modalStreamingSection');
    const streamContainer = document.getElementById('modalStreamingLinks');
    if (streamContainer) {
        streamContainer.innerHTML = '';
        const addedPlatforms = new Set();
        let validLinksCount = 0;

        if (Array.isArray(ep.streamingLinks) && ep.streamingLinks.length > 0) {
            ep.streamingLinks.forEach(link => {
                const siteName = (link.site || '').trim();
                const directUrl = (link.url || '').trim();
                if (!directUrl) return;

                // Completely remove and ignore Bilibili or iQiyi
                if (siteName.toLowerCase().includes('bilibili') || siteName.toLowerCase().includes('iqiyi') || directUrl.toLowerCase().includes('bilibili') || directUrl.toLowerCase().includes('iq.com')) {
                    return;
                }

                // Match with known platforms
                const matchedDef = STREAMING_PLATFORMS.find(p => 
                    siteName.toLowerCase().includes(p.id) || 
                    siteName.toLowerCase().includes(p.name.toLowerCase()) || 
                    directUrl.toLowerCase().includes(p.urlMatch)
                );

                if (matchedDef && addedPlatforms.has(matchedDef.id)) {
                    return; // Avoid duplicate button for same platform
                }

                const btn = document.createElement('a');
                btn.href = directUrl;
                btn.target = '_blank';
                btn.rel = 'noopener noreferrer';

                if (matchedDef) {
                    btn.className = `streaming-btn ${matchedDef.id}`;
                    btn.innerHTML = `${matchedDef.svg} <span>${matchedDef.name}</span>`;
                    addedPlatforms.add(matchedDef.id);
                } else {
                    btn.className = 'streaming-btn';
                    btn.innerHTML = `<span>📺</span> <span>${siteName}</span>`;
                }

                streamContainer.appendChild(btn);
                validLinksCount++;
            });
        }

        // Nyaa.si (English-translated releases)
        const nyaaTitle = (ep.titleRomaji || ep.displayTitle || ep.titleEnglish || '').trim();
        if (nyaaTitle) {
            const nyaaBtn = document.createElement('a');
            nyaaBtn.href = `https://nyaa.si/?f=0&c=1_2&q=${encodeURIComponent(nyaaTitle)}`;
            nyaaBtn.target = '_blank';
            nyaaBtn.rel = 'noopener noreferrer';
            nyaaBtn.className = 'streaming-btn nyaa';
            nyaaBtn.title = `Search English-translated releases for "${nyaaTitle}" on Nyaa.si`;
            nyaaBtn.innerHTML = `<img src="nyaa.png" class="nyaa-icon" alt="Nyaa" /> <span>Nyaa.si</span>`;
            streamContainer.appendChild(nyaaBtn);
            validLinksCount++;
        }

        if (streamSection) {
            if (validLinksCount > 0) {
                streamSection.classList.remove('hidden');
            } else {
                streamSection.classList.add('hidden');
            }
        }
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
