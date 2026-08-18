// LiveChart Anime Tracker - Core Frontend Application

const state = {
    currentView: 'schedule',
    activeDay: 'ALL',
    currentSeason: getCurrentSeason(),
    currentYear: new Date().getFullYear(),
    activeLibStatus: 'WATCHING',
    searchQuery: '',
    formatFilter: 'ALL',
    genreFilter: 'ALL',
    myScheduleOnly: false,
    user: null, // { platform: '', username: '', data: null }
    scheduleData: null,
    seasonalData: null,
    libraryData: null,
    allGenres: new Set(),
    activeModalAnime: null
};

// ==================== INITIALIZATION ====================
document.addEventListener('DOMContentLoaded', () => {
    initTheme();
    loadStoredUser();
    initYearSelect();
    setupEventListeners();
    startCountdownLoop();
    loadScheduleData();
});

function initTheme() {
    const savedTheme = localStorage.getItem('theme') || 'dark';
    document.documentElement.setAttribute('data-theme', savedTheme);
    document.getElementById('themeIcon').textContent = savedTheme === 'dark' ? '🌙' : '☀️';
}

function initYearSelect() {
    const select = document.getElementById('seasonYearSelect');
    const currentYear = new Date().getFullYear();
    for (let y = currentYear + 1; y >= currentYear - 5; y--) {
        const opt = document.createElement('option');
        opt.value = y;
        opt.textContent = y;
        if (y === state.currentYear) opt.selected = true;
        select.appendChild(opt);
    }
}

function loadStoredUser() {
    const stored = localStorage.getItem('anime_user');
    const storedMySchedule = localStorage.getItem('my_schedule_only');
    if (stored) {
        try {
            state.user = JSON.parse(stored);
            state.myScheduleOnly = storedMySchedule === 'true';
            document.getElementById('myScheduleToggle').checked = state.myScheduleOnly;
            updateUserUI();
            if (state.user && state.user.platform && state.user.username) {
                // Refresh user library in background
                fetchUserLibrary(state.user.platform, state.user.username, false);
            }
        } catch (e) {
            console.error('Error loading stored user', e);
        }
    }
}

// ==================== EVENT LISTENERS ====================
function setupEventListeners() {
    // Navigation
    document.querySelectorAll('.nav-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.nav-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            switchView(btn.dataset.view);
        });
    });

    // Theme Toggle
    document.getElementById('themeToggle').addEventListener('click', () => {
        const current = document.documentElement.getAttribute('data-theme');
        const next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('theme', next);
        document.getElementById('themeIcon').textContent = next === 'dark' ? '🌙' : '☀️';
    });

    // Day Pills
    document.querySelectorAll('.pill-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.pill-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            state.activeDay = btn.dataset.day;
            renderSchedule();
        });
    });

    // Season Buttons
    document.querySelectorAll('.season-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.season-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            state.currentSeason = btn.dataset.season;
            loadSeasonalData();
        });
    });

    // Year Select
    document.getElementById('seasonYearSelect').addEventListener('change', (e) => {
        state.currentYear = parseInt(e.target.value);
        loadSeasonalData();
    });

    // Library Tabs
    document.querySelectorAll('.lib-tab').forEach(tab => {
        tab.addEventListener('click', () => {
            document.querySelectorAll('.lib-tab').forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            state.activeLibStatus = tab.dataset.libStatus;
            renderLibrary();
        });
    });

    // Search Input
    const searchInput = document.getElementById('searchInput');
    const clearSearch = document.getElementById('clearSearchBtn');
    searchInput.addEventListener('input', (e) => {
        state.searchQuery = e.target.value.trim().toLowerCase();
        clearSearch.classList.toggle('hidden', state.searchQuery.length === 0);
        renderCurrentView();
    });
    clearSearch.addEventListener('click', () => {
        searchInput.value = '';
        state.searchQuery = '';
        clearSearch.classList.add('hidden');
        renderCurrentView();
    });

    // Filters
    document.getElementById('formatFilter').addEventListener('change', (e) => {
        state.formatFilter = e.target.value;
        renderCurrentView();
    });
    document.getElementById('genreFilter').addEventListener('change', (e) => {
        state.genreFilter = e.target.value;
        renderCurrentView();
    });

    // My Schedule Toggle
    document.getElementById('myScheduleToggle').addEventListener('change', (e) => {
        state.myScheduleOnly = e.target.checked;
        localStorage.setItem('my_schedule_only', state.myScheduleOnly);
        loadScheduleData();
    });

    // User Sync Modal Open
    document.getElementById('userSyncBtn').addEventListener('click', () => {
        document.getElementById('syncError').classList.add('hidden');
        if (state.user) {
            document.getElementById('syncUsernameInput').value = state.user.username;
            const r = document.querySelector(`input[name="platformChoice"][value="${state.user.platform}"]`);
            if (r) r.checked = true;
        }
        openModal('syncModal');
    });

    // Start Sync Button in Modal
    document.getElementById('startSyncBtn').addEventListener('click', handleSyncSubmit);
    document.getElementById('syncUsernameInput').addEventListener('keydown', (e) => {
        if (e.key === 'Enter') handleSyncSubmit();
    });

    // Disconnect User
    document.getElementById('disconnectUserBtn').addEventListener('click', () => {
        state.user = null;
        state.libraryData = null;
        state.myScheduleOnly = false;
        localStorage.removeItem('anime_user');
        localStorage.removeItem('my_schedule_only');
        document.getElementById('myScheduleToggle').checked = false;
        updateUserUI();
        loadScheduleData();
        if (state.currentView === 'library') renderLibrary();
    });

    // Export ICS Modal Open
    document.getElementById('exportIcsBtn').addEventListener('click', () => {
        if (!state.user) {
            openModal('syncModal');
            return;
        }
        openModal('exportModal');
    });

    // Download ICS Button
    document.getElementById('downloadIcsActionBtn').addEventListener('click', () => {
        if (!state.user) return;
        const scope = document.querySelector('input[name="exportScope"]:checked').value;
        const onlyWatching = scope === 'watching';
        const remind = document.getElementById('reminderSelect').value;
        const url = `/api/export/ics?platform=${encodeURIComponent(state.user.platform)}&username=${encodeURIComponent(state.user.username)}&onlyWatching=${onlyWatching}&remindMinutes=${remind}`;
        window.location.href = url;
        closeModal('exportModal');
    });

    // Close Modals on click outside
    document.querySelectorAll('.modal-backdrop').forEach(modal => {
        modal.addEventListener('click', (e) => {
            if (e.target === modal) {
                modal.classList.add('hidden');
            }
        });
    });
}

// ==================== VIEW SWITCHING ====================
function switchView(viewName) {
    state.currentView = viewName;

    // View containers
    document.getElementById('scheduleView').classList.toggle('hidden', viewName !== 'schedule');
    document.getElementById('seasonalView').classList.toggle('hidden', viewName !== 'seasonal');
    document.getElementById('libraryView').classList.toggle('hidden', viewName !== 'library');

    // Toolbars
    document.getElementById('scheduleToolbar').classList.toggle('hidden', viewName !== 'schedule');
    document.getElementById('seasonalToolbar').classList.toggle('hidden', viewName !== 'seasonal');
    document.getElementById('libraryToolbar').classList.toggle('hidden', viewName !== 'library');

    if (viewName === 'seasonal' && !state.seasonalData) {
        loadSeasonalData();
    } else if (viewName === 'library') {
        renderLibrary();
    } else {
        renderCurrentView();
    }
}

function renderCurrentView() {
    if (state.currentView === 'schedule') renderSchedule();
    else if (state.currentView === 'seasonal') renderSeasonal();
    else if (state.currentView === 'library') renderLibrary();
}

// ==================== DATA FETCHING ====================
async function loadScheduleData() {
    showLoading('Pobieranie harmonogramu tygodniowego...');
    try {
        let url = '/api/schedule';
        if (state.myScheduleOnly && state.user) {
            url += `?platform=${encodeURIComponent(state.user.platform)}&username=${encodeURIComponent(state.user.username)}`;
        }
        const res = await fetch(url);
        if (!res.ok) throw new Error('Błąd pobierania ramówki');
        const data = await res.json();
        state.scheduleData = data;
        extractGenres(data.schedule.flatMap(d => d.animeList));
        renderSchedule();
    } catch (err) {
        console.error(err);
        alert('Nie udało się pobrać ramówki: ' + err.message);
    } finally {
        hideLoading();
    }
}

async function loadSeasonalData() {
    showLoading(`Pobieranie anime sezonowych (${state.currentSeason} ${state.currentYear})...`);
    try {
        let url = `/api/seasonal?season=${state.currentSeason}&year=${state.currentYear}`;
        if (state.user) {
            url += `&platform=${encodeURIComponent(state.user.platform)}&username=${encodeURIComponent(state.user.username)}`;
        }
        const res = await fetch(url);
        if (!res.ok) throw new Error('Błąd pobierania anime sezonowych');
        const data = await res.json();
        state.seasonalData = data;
        extractGenres(data.animeList);
        renderSeasonal();
    } catch (err) {
        console.error(err);
        alert('Nie udało się pobrać anime sezonowych: ' + err.message);
    } finally {
        hideLoading();
    }
}

async function handleSyncSubmit() {
    const platform = document.querySelector('input[name="platformChoice"]:checked').value;
    const username = document.getElementById('syncUsernameInput').value.trim();
    const errBox = document.getElementById('syncError');

    if (!username) {
        errBox.textContent = 'Proszę podać nazwę użytkownika.';
        errBox.classList.remove('hidden');
        return;
    }

    errBox.classList.add('hidden');
    showLoading(`Pobieranie listy użytkownika ${username} z ${platform}...`);

    try {
        await fetchUserLibrary(platform, username, true);
        closeModal('syncModal');
        loadScheduleData();
    } catch (err) {
        errBox.textContent = err.message || 'Wystąpił błąd podczas synchronizacji.';
        errBox.classList.remove('hidden');
    } finally {
        hideLoading();
    }
}

async function fetchUserLibrary(platform, username, showAlert = false) {
    const res = await fetch(`/api/user/${encodeURIComponent(platform)}/${encodeURIComponent(username)}`);
    if (!res.ok) {
        const json = await res.json();
        throw new Error(json.error || 'Nie znaleziono profilu lub błąd połączenia.');
    }
    const data = await res.json();
    state.user = { platform, username, data };
    state.libraryData = data;
    localStorage.setItem('anime_user', JSON.stringify(state.user));
    updateUserUI();
}

function updateUserUI() {
    const bar = document.getElementById('userStatusBar');
    const btnText = document.getElementById('userBtnText');

    if (state.user && state.user.data) {
        bar.classList.remove('hidden');
        btnText.textContent = state.user.username;
        document.getElementById('userName').textContent = state.user.username;
        document.getElementById('userPlatformBadge').textContent = state.user.platform;
        document.getElementById('userAvatar').src = state.user.data.avatarUrl || 'https://myanimelist.net/images/userimages/default.jpg';
        document.getElementById('userWatchingCount').textContent = state.user.data.watching.length;
        document.getElementById('userPlanningCount').textContent = state.user.data.planning.length;
        document.getElementById('userCompletedCount').textContent = state.user.data.completed.length;

        document.getElementById('libWatchCount').textContent = state.user.data.watching.length;
        document.getElementById('libPlanCount').textContent = state.user.data.planning.length;
        document.getElementById('libCompCount').textContent = state.user.data.completed.length;
        document.getElementById('libPauseCount').textContent = state.user.data.paused.length;
        document.getElementById('libDropCount').textContent = state.user.data.dropped.length;
    } else {
        bar.classList.add('hidden');
        btnText.textContent = 'Połącz konto';
    }
}

// ==================== RENDERING ====================
function renderSchedule() {
    if (!state.scheduleData) return;

    const container = document.getElementById('scheduleDaysContainer');
    container.innerHTML = '';

    let visibleCount = 0;
    const schedule = state.scheduleData.schedule;

    schedule.forEach(day => {
        if (state.activeDay !== 'ALL' && day.Day !== state.activeDay) return;

        const filteredList = filterAnimeList(day.AnimeList);
        if (filteredList.length === 0 && state.activeDay !== 'ALL') {
            container.innerHTML = `
                <div class="empty-state">
                    <div class="empty-icon">📅</div>
                    <h3>Brak emisji w wybranym dniu</h3>
                    <p>Nie znaleziono anime odpowiadających wybranym filtrom dla dnia: ${day.DayPl}.</p>
                </div>
            `;
            return;
        }

        if (filteredList.length === 0) return;

        visibleCount += filteredList.length;

        const daySec = document.createElement('div');
        daySec.className = 'day-section';

        const dayHeader = document.createElement('div');
        dayHeader.className = 'day-header';
        dayHeader.innerHTML = `
            <h3>📅 ${day.DayPl} <span class="day-count">(${day.Day})</span></h3>
            <span class="badge">${filteredList.length} serii</span>
        `;
        daySec.appendChild(dayHeader);

        const grid = document.createElement('div');
        grid.className = 'anime-cards-grid';

        filteredList.forEach(anime => {
            grid.appendChild(createAnimeCard(anime));
        });

        daySec.appendChild(grid);
        container.appendChild(daySec);
    });

    document.getElementById('scheduleCountBadge').textContent = `${visibleCount} serii`;
}

function renderSeasonal() {
    if (!state.seasonalData) return;

    const container = document.getElementById('seasonalGrid');
    container.innerHTML = '';

    document.getElementById('seasonalTitle').textContent = `Anime Sezonu: ${getSeasonPl(state.currentSeason)} ${state.currentYear}`;

    const filtered = filterAnimeList(state.seasonalData.animeList);
    document.getElementById('seasonalCountBadge').textContent = `${filtered.length} serii`;

    if (filtered.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">🌸</div>
                <h3>Brak wyników</h3>
                <p>Nie znaleziono anime pasujących do bieżących filtrów w tym sezonie.</p>
            </div>
        `;
        return;
    }

    filtered.forEach(anime => {
        container.appendChild(createAnimeCard(anime));
    });
}

function renderLibrary() {
    const container = document.getElementById('libraryGrid');
    const emptyState = document.getElementById('libraryEmptyState');
    container.innerHTML = '';

    if (!state.user || !state.user.data) {
        emptyState.classList.remove('hidden');
        document.getElementById('libraryCountBadge').textContent = '0 serii';
        return;
    }

    emptyState.classList.add('hidden');

    let list = [];
    switch (state.activeLibStatus) {
        case 'WATCHING': list = state.user.data.watching; break;
        case 'PLANNING': list = state.user.data.planning; break;
        case 'COMPLETED': list = state.user.data.completed; break;
        case 'PAUSED': list = state.user.data.paused; break;
        case 'DROPPED': list = state.user.data.dropped; break;
        default: list = state.user.data.watching; break;
    }

    const filtered = filterAnimeList(list);
    document.getElementById('libraryCountBadge').textContent = `${filtered.length} serii`;

    if (filtered.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">📚</div>
                <h3>Pusta zakładka</h3>
                <p>Nie masz żadnych anime o statusie "${state.activeLibStatus}" lub filtry je ukryły.</p>
            </div>
        `;
        return;
    }

    filtered.forEach(anime => {
        container.appendChild(createAnimeCard(anime));
    });
}

function createAnimeCard(anime) {
    const card = document.createElement('div');
    card.className = 'anime-card';
    card.addEventListener('click', () => openDetailsModal(anime));

    // Poster & Overlay
    const top = document.createElement('div');
    top.className = 'card-top';

    const poster = document.createElement('img');
    poster.className = 'card-poster';
    poster.src = anime.coverImage || 'https://via.placeholder.com/280x180?text=Brak+Plakatu';
    poster.alt = anime.displayTitle;
    poster.loading = 'lazy';
    top.appendChild(poster);

    const overlay = document.createElement('div');
    overlay.className = 'card-overlay';
    top.appendChild(overlay);

    // Format badge
    const formatBadge = document.createElement('span');
    formatBadge.className = 'card-format-badge';
    formatBadge.textContent = anime.format || 'TV';
    top.appendChild(formatBadge);

    // Score badge
    if (anime.averageScore) {
        const scoreBadge = document.createElement('span');
        scoreBadge.className = 'card-score-badge';
        scoreBadge.textContent = `⭐ ${anime.averageScore.toFixed(0)}%`;
        top.appendChild(scoreBadge);
    }

    // User watching badge
    if (anime.userStatus) {
        const userTag = document.createElement('span');
        userTag.className = 'card-user-tag';
        const prog = anime.userProgress != null ? `${anime.userProgress}/${anime.episodes || '?'}` : '';
        userTag.textContent = `${getUserStatusPl(anime.userStatus)} ${prog}`;
        top.appendChild(userTag);
    }

    // Countdown badge
    if (anime.nextAiringEpisode) {
        const ticker = document.createElement('span');
        ticker.className = 'card-countdown-ticker js-countdown-ticker';
        ticker.dataset.airingAt = anime.nextAiringEpisode.airingAt;
        ticker.dataset.ep = anime.nextAiringEpisode.episode;
        ticker.textContent = formatCountdown(anime.nextAiringEpisode.airingAt, anime.nextAiringEpisode.episode);
        top.appendChild(ticker);
    }

    card.appendChild(top);

    // Card Body
    const body = document.createElement('div');
    body.className = 'card-body';

    const title = document.createElement('h4');
    title.className = 'card-title';
    title.textContent = anime.displayTitle;
    title.title = anime.displayTitle;
    body.appendChild(title);

    const metaRow = document.createElement('div');
    metaRow.className = 'card-meta-row';
    const studio = anime.studios && anime.studios.length > 0 ? anime.studios[0] : 'Studio N/A';
    const epCount = anime.episodes ? `${anime.episodes} odc.` : 'Odc. ?';
    metaRow.innerHTML = `<span>🏢 ${escapeHtml(studio)}</span><span>📺 ${epCount}</span>`;
    body.appendChild(metaRow);

    if (anime.genres && anime.genres.length > 0) {
        const genresDiv = document.createElement('div');
        genresDiv.className = 'card-genres';
        anime.genres.slice(0, 3).forEach(g => {
            const tag = document.createElement('span');
            tag.className = 'genre-tag';
            tag.textContent = g;
            genresDiv.appendChild(tag);
        });
        body.appendChild(genresDiv);
    }

    if (anime.synopsis) {
        const syn = document.createElement('p');
        syn.className = 'card-synopsis';
        syn.textContent = stripHtml(anime.synopsis);
        body.appendChild(syn);
    }

    card.appendChild(body);
    return card;
}

// ==================== MODALS LOGIC ====================
function openModal(id) {
    document.getElementById(id).classList.remove('hidden');
}

function closeModal(id) {
    document.getElementById(id).classList.add('hidden');
    if (id === 'detailsModal') state.activeModalAnime = null;
}

function openDetailsModal(anime) {
    state.activeModalAnime = anime;

    document.getElementById('modalAnimeTitle').textContent = anime.displayTitle;
    document.getElementById('modalTitleEnglish').textContent = anime.titleEnglish || anime.displayTitle;
    document.getElementById('modalTitleRomaji').textContent = anime.titleRomaji ? `Romaji: ${anime.titleRomaji}` : '';
    document.getElementById('modalTitleNative').textContent = anime.titleNative ? `JP: ${anime.titleNative}` : '';

    const bannerImg = document.getElementById('modalAnimeBanner');
    if (anime.bannerImage) {
        bannerImg.src = anime.bannerImage;
        bannerImg.classList.remove('hidden');
    } else {
        bannerImg.classList.add('hidden');
    }

    document.getElementById('modalAnimePoster').src = anime.coverImage || '';
    document.getElementById('modalScoreBadge').textContent = anime.averageScore ? `⭐ Średnia ocena: ${anime.averageScore.toFixed(0)}%` : '⭐ Ocena: N/A';

    const progBadge = document.getElementById('modalUserProgressBadge');
    if (anime.userStatus) {
        progBadge.classList.remove('hidden');
        progBadge.textContent = `Status: ${getUserStatusPl(anime.userStatus)} (${anime.userProgress || 0}/${anime.episodes || '?'})`;
    } else {
        progBadge.classList.add('hidden');
    }

    // Countdown Box
    const countBox = document.getElementById('modalCountdownBox');
    if (anime.nextAiringEpisode) {
        countBox.classList.remove('hidden');
        document.getElementById('modalEpisodeNum').textContent = `Odcinek ${anime.nextAiringEpisode.episode}`;
        updateModalCountdown();
        const airDate = new Date(anime.nextAiringEpisode.airingAt);
        document.getElementById('modalAiringDate').textContent = airDate.toLocaleString('pl-PL', { weekday: 'long', day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit' });
    } else {
        countBox.classList.add('hidden');
    }

    // Meta Grid
    document.getElementById('modalFormat').textContent = anime.format || 'TV';
    document.getElementById('modalStatus').textContent = anime.status || 'RELEASING';
    document.getElementById('modalEpisodes').textContent = anime.episodes ? `${anime.episodes} odcinków` : 'W trakcie / Nieznane';
    document.getElementById('modalDuration').textContent = anime.episodeDuration ? `${anime.episodeDuration} min` : '24 min';
    document.getElementById('modalStudio').textContent = anime.studios && anime.studios.length > 0 ? anime.studios.join(', ') : 'Nieznane';
    document.getElementById('modalSeason').textContent = anime.season && anime.seasonYear ? `${getSeasonPl(anime.season)} ${anime.seasonYear}` : '--';
    document.getElementById('modalSource').textContent = anime.source || 'Original';

    // Genres
    const genresWrap = document.getElementById('modalGenres');
    genresWrap.innerHTML = '';
    (anime.genres || []).forEach(g => {
        const tag = document.createElement('span');
        tag.className = 'genre-tag';
        tag.textContent = g;
        genresWrap.appendChild(tag);
    });

    // Synopsis
    document.getElementById('modalSynopsis').textContent = stripHtml(anime.synopsis) || 'Brak opisu dla tej serii.';

    // External Links
    const linksWrap = document.getElementById('modalLinks');
    linksWrap.innerHTML = '';

    if (anime.liveChartUrl || anime.malId || anime.aniListId) {
        const lcQuery = encodeURIComponent(anime.displayTitle);
        addLinkButton(linksWrap, `https://www.livechart.me/search?q=${lcQuery}`, '🌐 LiveChart.me', 'btn-outline');
    }
    if (anime.malUrl || anime.malId) {
        const malUrl = anime.malUrl || `https://myanimelist.net/anime/${anime.malId}`;
        addLinkButton(linksWrap, malUrl, '🔵 MyAnimeList', 'btn-outline');
    }
    if (anime.aniListUrl || anime.aniListId) {
        const alUrl = anime.aniListUrl || `https://anilist.co/anime/${anime.aniListId}`;
        addLinkButton(linksWrap, alUrl, '🔷 AniList', 'btn-outline');
    }
    if (anime.kitsuUrl || anime.kitsuId) {
        const kUrl = anime.kitsuUrl || `https://kitsu.app/anime/${anime.kitsuId}`;
        addLinkButton(linksWrap, kUrl, '🦊 Kitsu', 'btn-outline');
    }

    openModal('detailsModal');
}

function addLinkButton(container, href, text, extraClass = '') {
    const a = document.createElement('a');
    a.href = href;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.className = `btn btn-sm ${extraClass}`;
    a.textContent = text;
    container.appendChild(a);
}

// ==================== REAL-TIME COUNTDOWN LOOP ====================
function startCountdownLoop() {
    setInterval(() => {
        // Update all card countdown tickers
        document.querySelectorAll('.js-countdown-ticker').forEach(ticker => {
            const airingAt = ticker.dataset.airingAt;
            const ep = ticker.dataset.ep;
            if (airingAt) {
                ticker.textContent = formatCountdown(airingAt, ep);
            }
        });

        // Update modal if open
        if (state.activeModalAnime && state.activeModalAnime.nextAiringEpisode) {
            updateModalCountdown();
        }
    }, 1000);
}

function updateModalCountdown() {
    if (!state.activeModalAnime || !state.activeModalAnime.nextAiringEpisode) return;
    const targetTime = new Date(state.activeModalAnime.nextAiringEpisode.airingAt).getTime();
    const now = Date.now();
    const diff = targetTime - now;

    const timerEl = document.getElementById('modalCountdownTime');
    if (diff <= 0) {
        timerEl.textContent = 'Premiera teraz!';
        timerEl.style.color = '#f47067';
        return;
    }

    const days = Math.floor(diff / (1000 * 60 * 60 * 24));
    const hours = Math.floor((diff % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
    const minutes = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60));
    const seconds = Math.floor((diff % (1000 * 60)) / 1000);

    timerEl.textContent = `za ${pad(days)}d ${pad(hours)}h ${pad(minutes)}m ${pad(seconds)}s`;
    timerEl.style.color = 'var(--accent-green)';
}

function formatCountdown(airingAtIso, ep) {
    const target = new Date(airingAtIso).getTime();
    const now = Date.now();
    const diff = target - now;

    if (diff <= 0) return `Odc. ${ep} teraz!`;

    const days = Math.floor(diff / (1000 * 60 * 60 * 24));
    const hours = Math.floor((diff % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
    const minutes = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60));

    if (days > 0) {
        return `Odc. ${ep} za ${days}d ${hours}h`;
    }
    return `Odc. ${ep} za ${pad(hours)}:${pad(minutes)}`;
}

// ==================== FILTER HELPERS ====================
function filterAnimeList(list) {
    if (!list) return [];
    return list.filter(anime => {
        // Search query
        if (state.searchQuery) {
            const q = state.searchQuery;
            const tEn = (anime.titleEnglish || '').toLowerCase();
            const tRo = (anime.titleRomaji || '').toLowerCase();
            const tNa = (anime.titleNative || '').toLowerCase();
            const syn = (anime.synopsis || '').toLowerCase();
            const studios = (anime.studios || []).map(s => s.toLowerCase()).join(' ');
            const genres = (anime.genres || []).map(g => g.toLowerCase()).join(' ');

            const matches = tEn.includes(q) || tRo.includes(q) || tNa.includes(q) || syn.includes(q) || studios.includes(q) || genres.includes(q);
            if (!matches) return false;
        }

        // Format
        if (state.formatFilter !== 'ALL') {
            if ((anime.format || 'TV').toUpperCase() !== state.formatFilter) return false;
        }

        // Genre
        if (state.genreFilter !== 'ALL') {
            if (!anime.genres || !anime.genres.includes(state.genreFilter)) return false;
        }

        return true;
    });
}

function extractGenres(list) {
    if (!list) return;
    list.forEach(a => {
        (a.genres || []).forEach(g => state.allGenres.add(g));
    });

    const select = document.getElementById('genreFilter');
    const currentVal = select.value;
    select.innerHTML = '<option value="ALL">Wszystkie gatunki</option>';
    Array.from(state.allGenres).sort().forEach(g => {
        const opt = document.createElement('option');
        opt.value = g;
        opt.textContent = g;
        if (g === currentVal) opt.selected = true;
        select.appendChild(opt);
    });
}

// ==================== UTILS ====================
function getCurrentSeason() {
    const month = new Date().getMonth() + 1;
    if (month >= 1 && month <= 3) return 'WINTER';
    if (month >= 4 && month <= 6) return 'SPRING';
    if (month >= 7 && month <= 9) return 'SUMMER';
    return 'FALL';
}

function getSeasonPl(s) {
    switch ((s || '').toUpperCase()) {
        case 'WINTER': return 'Zima';
        case 'SPRING': return 'Wiosna';
        case 'SUMMER': return 'Lato';
        case 'FALL': return 'Jesień';
        default: return s;
    }
}

function getUserStatusPl(st) {
    switch ((st || '').toUpperCase()) {
        case 'CURRENT':
        case 'WATCHING': return 'Oglądane';
        case 'PLANNING': return 'Planowane';
        case 'COMPLETED': return 'Ukończone';
        case 'PAUSED': return 'Wstrzymane';
        case 'DROPPED': return 'Porzucone';
        default: return st;
    }
}

function pad(n) {
    return n < 10 ? '0' + n : n;
}

function stripHtml(html) {
    if (!html) return '';
    const tmp = document.createElement('div');
    tmp.innerHTML = html;
    return tmp.textContent || tmp.innerText || '';
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function showLoading(msg = 'Ładowanie danych...') {
    document.getElementById('loadingMessage').textContent = msg;
    document.getElementById('loadingOverlay').classList.remove('hidden');
}

function hideLoading() {
    document.getElementById('loadingOverlay').classList.add('hidden');
}
