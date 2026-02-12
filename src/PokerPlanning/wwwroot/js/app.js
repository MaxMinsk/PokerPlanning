// ===== Session Persistence =====
const SESSION_KEY = 'poker_session';

function saveSession(roomCode, playerId, playerName) {
    try {
        localStorage.setItem(SESSION_KEY, JSON.stringify({ roomCode, playerId, playerName, ts: Date.now() }));
    } catch (e) { /* ignore */ }
}

function loadSession() {
    try {
        const raw = localStorage.getItem(SESSION_KEY);
        if (!raw) return null;
        const s = JSON.parse(raw);
        // Expire after 2 hours
        if (Date.now() - s.ts > 2 * 60 * 60 * 1000) {
            localStorage.removeItem(SESSION_KEY);
            return null;
        }
        return s;
    } catch (e) { return null; }
}

function clearSession() {
    localStorage.removeItem(SESSION_KEY);
}

// ===== State =====
let connection = null;
let timerInterval = null;
let state = {
    roomCode: null,
    myName: null,
    isOwner: false,
    isSpectator: false,
    scale: [],
    players: [],
    currentCardIndex: 0,
    totalCards: 0,
    selectedVote: null,
    roomState: 'Voting',  // Voting | Revealed | Finished
    votes: {},
    results: null,
    secondsPerCard: null,
    timerDeadline: null,    // Date object: when current card timer expires
    coffeeBreakEnabled: false
};

// ===== SignalR Connection =====
function initConnection() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/pokerhub")
        .withAutomaticReconnect()
        .build();

    connection.on("RoomCreated", onRoomCreated);
    connection.on("RoomState", onRoomState);
    connection.on("PlayerJoined", onPlayerJoined);
    connection.on("PlayerLeft", onPlayerLeft);
    connection.on("VoteReceived", onVoteReceived);
    connection.on("CardsRevealed", onCardsRevealed);
    connection.on("VoteUpdated", onVoteUpdated);
    connection.on("EstimateAccepted", onEstimateAccepted);
    connection.on("NewRound", onNewRound);
    connection.on("GameFinished", onGameFinished);
    connection.on("Results", onResults);
    connection.on("PlayerThinking", onPlayerThinking);
    connection.on("RejoinFailed", onRejoinFailed);
    connection.on("Error", onError);

    connection.onreconnecting(() => showToast("Reconnecting..."));
    connection.onreconnected(async () => {
        showToast("Reconnected!");
        // Auto-rejoin room after SignalR reconnect
        const session = loadSession();
        if (session && session.roomCode && session.playerId) {
            connection.invoke("RejoinRoom", session.roomCode, session.playerId).catch(() => {});
        }
    });

    return connection.start();
}

// ===== Screen Management =====
function showScreen(name) {
    document.querySelectorAll('.screen').forEach(s => s.classList.remove('active'));
    document.getElementById(`screen-${name}`).classList.add('active');
}

// ===== Event Handlers: UI =====
document.getElementById('btnCreateRoom').addEventListener('click', () => showScreen('create'));

document.getElementById('btnJoinGo').addEventListener('click', () => {
    const code = document.getElementById('joinCode').value.trim().toUpperCase();
    if (!code) return showToast("Enter a room code", true);
    navigateToJoin(code);
});

document.getElementById('joinCode').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') document.getElementById('btnJoinGo').click();
});

document.getElementById('btnContinue').addEventListener('click', async () => {
    const ownerName = document.getElementById('ownerName').value.trim();
    const scaleType = parseInt(document.getElementById('scaleSelect').value);
    const cardsText = document.getElementById('cardsText').value.trim();
    const sessionVal = document.getElementById('sessionTime').value;
    const sessionMinutes = sessionVal ? parseInt(sessionVal) : null;
    const coffeeBreak = document.getElementById('coffeeBreak').checked;
    const shuffle = document.getElementById('shuffleQuestions').checked;

    if (!cardsText) return showToast("Enter at least one question", true);

    await ensureConnected();
    connection.invoke("CreateRoom", ownerName || null, scaleType, cardsText, sessionMinutes, coffeeBreak, shuffle);
});

document.getElementById('btnJoinRoom').addEventListener('click', async () => {
    const name = document.getElementById('playerName').value.trim();
    if (!name) return showToast("Enter your name", true);

    const code = document.getElementById('joinRoomCode').textContent;
    await ensureConnected();
    connection.invoke("JoinRoom", code, name);
});

document.getElementById('btnReveal').addEventListener('click', () => {
    connection.invoke("RevealCards", state.roomCode);
});

document.getElementById('btnRevote').addEventListener('click', () => {
    connection.invoke("Revote", state.roomCode);
});

document.getElementById('btnNext').addEventListener('click', () => {
    connection.invoke("NextQuestion", state.roomCode);
});

document.getElementById('btnAccept').addEventListener('click', async () => {
    const val = document.getElementById('acceptSelect').value;
    await connection.invoke("AcceptEstimate", state.roomCode, val);
    connection.invoke("NextQuestion", state.roomCode);
});

document.getElementById('btnCopyLink').addEventListener('click', () => {
    const url = `${window.location.origin}/join/${state.roomCode}`;
    navigator.clipboard.writeText(url).then(() => showToast("Link copied!"));
});

document.getElementById('btnDownloadCsv').addEventListener('click', downloadCsv);
document.getElementById('btnDownloadJson').addEventListener('click', downloadJson);

document.getElementById('btnExport').addEventListener('click', showExportMenu);

// ===== Event Handlers: Server =====
function onRoomCreated(data) {
    state.roomCode = data.roomCode;
    state.myName = data.myName;
    state.isOwner = data.isOwner;
    state.isSpectator = data.isSpectator;
    state.scale = data.scale;
    state.players = data.players;
    state.currentCardIndex = data.currentCardIndex;
    state.totalCards = data.totalCards;
    state.roomState = 'Voting';
    state.selectedVote = null;
    state.votes = {};
    state.secondsPerCard = data.secondsPerCard || null;
    state.coffeeBreakEnabled = data.coffeeBreakEnabled || false;

    // Save session for reconnect
    const me = data.players.find(p => p.isOwner);
    saveSession(data.roomCode, data.playerId, me ? me.name : 'Spectator');

    resetActivityTracking();
    startSleepCheck();
    renderRoom(data.currentCard);
    startCardTimer();
    showScreen('room');
    updateUrl(`/room/${data.roomCode}`);
}

function onRoomState(data) {
    state.roomCode = data.roomCode;
    state.myName = data.myName;
    state.isOwner = data.isOwner;
    state.isSpectator = data.isSpectator;
    state.scale = data.scale;
    state.players = data.players;
    state.currentCardIndex = data.currentCardIndex;
    state.totalCards = data.totalCards;
    state.roomState = data.state;
    state.selectedVote = data.myVote || null;
    state.votes = data.votes || {};
    state.secondsPerCard = data.secondsPerCard || null;
    state.coffeeBreakEnabled = data.coffeeBreakEnabled || false;

    // Save/update session for reconnect
    const me = data.players.find(p => p.name && !p.isSpectator) || data.players[0];
    saveSession(data.roomCode, data.playerId, me ? me.name : 'Player');

    // Restore timer from server timestamp
    if (data.secondsPerCard && data.cardTimerStartedAt) {
        const started = new Date(data.cardTimerStartedAt);
        state.timerDeadline = new Date(started.getTime() + data.secondsPerCard * 1000);
    }

    resetActivityTracking();
    if (data.state === 'Voting') startSleepCheck();
    renderRoom(data.currentCard);
    if (data.state === 'Voting') startCardTimer();
    showScreen('room');
    updateUrl(`/room/${data.roomCode}`);

    if (data.state === 'Revealed' && data.votes) {
        stopSleepCheck();
        renderRevealed(data.votes, data.consensus, data.average, data.coffeeVotes);
    }
}

function onPlayerJoined(data) {
    state.players = [...state.players.filter(p => p.name !== data.name), data];

    // If the joining player is the owner, remove ownership from others (including us)
    if (data.isOwner) {
        state.players = state.players.map(p => ({
            ...p,
            isOwner: p.name === data.name
        }));
        if (state.myName !== data.name && state.isOwner) {
            state.isOwner = false;
        }
    }

    renderPlayers();
    renderOwnerControls();
    document.getElementById('btnExport').style.display = state.isOwner ? '' : 'none';
    showToast(`${data.name} joined`);
}

function onPlayerLeft(data) {
    state.players = state.players.filter(p => p.name !== data.playerName);
    if (data.newOwnerName) {
        state.players = state.players.map(p => ({
            ...p,
            isOwner: p.name === data.newOwnerName
        }));
        // Check if *this* client became the new owner
        if (data.newOwnerName === state.myName) {
            state.isOwner = true;
            showToast('You are now the room owner!');
        }
    }
    renderPlayers();
    renderOwnerControls();
    // Show/hide owner-only header buttons
    document.getElementById('btnExport').style.display = state.isOwner ? '' : 'none';
    showToast(`${data.playerName} left`);
}

function onVoteReceived(data) {
    playerLastActivity[data.playerName] = Date.now();
    state.players = state.players.map(p =>
        p.name === data.playerName ? { ...p, hasVoted: true } : p
    );
    renderPlayers();
}

function onCardsRevealed(data) {
    state.roomState = 'Revealed';
    state.votes = data.votes;
    stopCardTimer();
    stopSleepCheck();
    renderRevealed(data.votes, data.consensus, data.average, data.coffeeVotes);
}

function onVoteUpdated(data) {
    state.votes[data.playerName] = data.value;
    renderRevealedVotes();
    document.getElementById('consensusValue').textContent = data.consensus || '-';
    document.getElementById('averageValue').textContent = data.average != null ? data.average : '-';
    preselectAcceptValue(data.consensus, data.average);
    renderCoffeeBanner(data.coffeeVotes || 0);
}

function onEstimateAccepted(data) {
    showToast(`Estimate accepted: ${data.value}`);
}

function onNewRound(data) {
    state.currentCardIndex = data.cardIndex;
    state.totalCards = data.totalCards;
    state.roomState = 'Voting';
    state.selectedVote = null;
    state.votes = {};
    state.secondsPerCard = data.secondsPerCard || state.secondsPerCard;
    state.players = state.players.map(p => ({ ...p, hasVoted: false }));

    resetActivityTracking();
    startSleepCheck();
    renderRoom(data.card);
    startCardTimer();
    document.getElementById('coffeeBanner').style.display = 'none';
}

function onGameFinished(data) {
    state.roomState = 'Finished';
    state.results = data.results;
    stopCardTimer();
    stopSleepCheck();
    clearSession();
    renderResults(data.results);
    showScreen('results');
}

function onResults(data) {
    state.results = data.results;
    if (state._exportPending) {
        state._exportPending = false;
        if (state._exportFormat === 'json') {
            downloadJson();
        } else {
            downloadCsv();
        }
    } else {
        renderResults(data.results);
        showScreen('results');
    }
}

function onRejoinFailed(msg) {
    clearSession();
    // Fall back to join screen for this room
    const path = window.location.pathname;
    const roomMatch = path.match(/^\/room\/([A-Za-z0-9]+)$/);
    if (roomMatch) {
        navigateToJoin(roomMatch[1].toUpperCase());
    } else {
        showScreen('home');
    }
}

function onError(msg) {
    showToast(msg, true);
}

// ===== Player Thinking (wobble) & Sleeping (Zzz) =====
const SLEEP_TIMEOUT = 30_000; // 30 seconds
const playerLastActivity = {}; // { playerName: timestamp }
let sleepCheckInterval = null;

function onPlayerThinking(data) {
    playerLastActivity[data.playerName] = Date.now();

    const seat = document.querySelector(`.player-seat[data-player="${CSS.escape(data.playerName)}"]`);
    if (!seat) return;
    const card = seat.querySelector('.player-card');
    if (!card) return;

    // Wake up — remove sleeping if present
    card.classList.remove('sleeping');

    // Remove and re-add class to restart animation
    card.classList.remove('thinking');
    void card.offsetWidth;
    card.classList.add('thinking');

    card.addEventListener('animationend', () => {
        card.classList.remove('thinking');
    }, { once: true });
}

function resetActivityTracking() {
    const now = Date.now();
    for (const key of Object.keys(playerLastActivity)) {
        delete playerLastActivity[key];
    }
    // Mark all current non-spectator players as active now
    (state.players || []).forEach(p => {
        if (!p.isSpectator) playerLastActivity[p.name] = now;
    });
}

function startSleepCheck() {
    stopSleepCheck();
    sleepCheckInterval = setInterval(updateSleepIndicators, 5000);
}

function stopSleepCheck() {
    if (sleepCheckInterval) { clearInterval(sleepCheckInterval); sleepCheckInterval = null; }
    // Remove all sleeping classes
    document.querySelectorAll('.player-card.sleeping').forEach(c => c.classList.remove('sleeping'));
}

function updateSleepIndicators() {
    if (state.roomState !== 'Voting') return;

    const now = Date.now();
    // Check if at least one non-spectator player has voted
    const someoneVoted = state.players.some(p =>
        !p.isSpectator && (p.hasVoted || state.votes[p.name] !== undefined)
    );
    if (!someoneVoted) return;

    state.players.forEach(p => {
        if (p.isSpectator) return;
        const hasVoted = p.hasVoted || state.votes[p.name] !== undefined;
        const seat = document.querySelector(`.player-seat[data-player="${CSS.escape(p.name)}"]`);
        if (!seat) return;
        const card = seat.querySelector('.player-card');
        if (!card) return;

        const lastActive = playerLastActivity[p.name] || 0;
        const idle = now - lastActive >= SLEEP_TIMEOUT;

        if (!hasVoted && idle) {
            card.classList.add('sleeping');
        } else {
            card.classList.remove('sleeping');
        }
    });
}

let thinkingThrottleTimer = null;
function sendThinking() {
    if (thinkingThrottleTimer || !state.roomCode || state.roomState !== 'Voting') return;
    thinkingThrottleTimer = setTimeout(() => { thinkingThrottleTimer = null; }, 2000);
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke("PlayerThinking", state.roomCode).catch(() => {});
    }
}

// Attach hover/mousemove listeners to voting area
document.getElementById('votingCards').addEventListener('mouseover', sendThinking);
document.getElementById('votingCards').addEventListener('touchstart', sendThinking, { passive: true });

// ===== Rendering =====
const CARD_COLORS = 8;

function renderRoom(card) {
    document.getElementById('roomCodeBadge').textContent = state.roomCode;
    document.getElementById('questionCounter').textContent =
        `Question ${state.currentCardIndex + 1} / ${state.totalCards}`;

    if (card) {
        document.getElementById('cardSubject').textContent = card.subject;
        document.getElementById('cardDescription').innerHTML = card.description || '';
    }

    // Apply color based on question index
    const tableCenter = document.querySelector('.table-center');
    for (let i = 0; i < CARD_COLORS; i++) {
        tableCenter.classList.remove(`card-color-${i}`);
    }
    tableCenter.classList.add(`card-color-${state.currentCardIndex % CARD_COLORS}`);

    document.getElementById('statsDisplay').style.display = 'none';
    document.getElementById('coffeeBanner').style.display = 'none';

    // Show export button for owner
    document.getElementById('btnExport').style.display = state.isOwner ? '' : 'none';

    renderPlayers();
    renderVotingCards();
    renderOwnerControls();
}

function renderPlayerSeat(p) {
    const vote = state.votes[p.name];
    const isRevealed = state.roomState === 'Revealed';
    const hasVoted = p.hasVoted || (vote !== undefined);

    let cardContent = '';
    let cardClass = 'player-card';

    if (p.isSpectator) {
        cardContent = '&#128065;';
        cardClass += ' spectator';
    } else if (isRevealed && vote !== undefined) {
        cardContent = escapeHtml(vote);
        cardClass += ' revealed';
    } else if (hasVoted) {
        cardContent = '&#10003;';
        cardClass += ' voted';
    }

    const nameClass = p.isOwner ? 'player-name owner' : 'player-name';
    const badge = p.isSpectator ? ' <span class="spectator-badge">spectator</span>' : '';

    return `
        <div class="player-seat" data-player="${escapeHtml(p.name)}">
            <div class="${cardClass}">${cardContent}</div>
            <div class="${nameClass}">${escapeHtml(p.name)}${badge}</div>
        </div>
    `;
}

function renderPlayers() {
    const players = state.players;
    const count = players.length;

    // Distribute players around the table: top, left, right, bottom
    // Rule: sides (left/right) hold max 3 each, everything else goes top/bottom
    const MAX_SIDE = 3;
    let top = [], right = [], bottom = [], left = [];

    if (count <= 3) {
        top = players;
    } else if (count <= 6) {
        const mid = Math.ceil(count / 2);
        top = players.slice(0, mid);
        bottom = players.slice(mid);
    } else {
        // Assign up to MAX_SIDE per side, rest to top/bottom
        const sideTotal = Math.min(count - 2, MAX_SIDE * 2); // at least 1 top + 1 bottom
        const leftCount = Math.ceil(sideTotal / 2);
        const rightCount = sideTotal - leftCount;
        const remaining = count - leftCount - rightCount;
        const topCount = Math.ceil(remaining / 2);
        const bottomCount = remaining - topCount;

        let idx = 0;
        top = players.slice(idx, idx + topCount); idx += topCount;
        right = players.slice(idx, idx + rightCount); idx += rightCount;
        bottom = players.slice(idx, idx + bottomCount); idx += bottomCount;
        left = players.slice(idx, idx + leftCount);
    }

    document.getElementById('playersTop').innerHTML = top.map(renderPlayerSeat).join('');
    document.getElementById('playersRight').innerHTML = right.map(renderPlayerSeat).join('');
    document.getElementById('playersBottom').innerHTML = bottom.map(renderPlayerSeat).join('');
    document.getElementById('playersLeft').innerHTML = left.map(renderPlayerSeat).join('');
}

function renderVotingCards() {
    const container = document.getElementById('votingCards');
    const area = document.getElementById('votingArea');

    if (state.isSpectator) {
        area.style.display = 'none';
        return;
    }

    area.style.display = '';
    let html = state.scale.map(val => {
        const selected = state.selectedVote === val ? 'selected' : '';
        return `<button class="vote-btn ${selected}" onclick="castVote('${escapeHtml(val)}')">${escapeHtml(val)}</button>`;
    }).join('');

    if (state.coffeeBreakEnabled) {
        const coffeeSelected = state.selectedVote === '☕' ? 'selected' : '';
        html += `<button class="vote-btn vote-btn-coffee ${coffeeSelected}" onclick="castVote('☕')">☕</button>`;
    }

    container.innerHTML = html;
}

function renderOwnerControls() {
    const controls = document.getElementById('ownerControls');
    if (!state.isOwner) {
        controls.style.display = 'none';
        return;
    }
    controls.style.display = '';

    const isVoting = state.roomState === 'Voting';
    const isRevealed = state.roomState === 'Revealed';

    document.getElementById('btnReveal').style.display = isVoting ? '' : 'none';
    document.getElementById('btnRevote').style.display = isRevealed ? '' : 'none';
    document.getElementById('btnNext').style.display = isRevealed ? '' : 'none';
    document.getElementById('acceptRow').style.display = isRevealed ? '' : 'none';

    if (isRevealed) {
        const select = document.getElementById('acceptSelect');
        select.innerHTML = state.scale
            .filter(v => v !== '?' && v !== '☕')
            .map(v => `<option value="${escapeHtml(v)}">${escapeHtml(v)}</option>`)
            .join('');
    }
}

function renderRevealed(votes, consensus, average, coffeeVotes) {
    state.votes = votes;
    renderPlayers();
    renderOwnerControls();

    document.getElementById('statsDisplay').style.display = '';
    document.getElementById('consensusValue').textContent = consensus || '-';
    document.getElementById('averageValue').textContent = average != null ? average : '-';

    preselectAcceptValue(consensus, average);
    renderCoffeeBanner(coffeeVotes || 0);
}

function renderCoffeeBanner(coffeeCount) {
    const banner = document.getElementById('coffeeBanner');
    if (!banner) return;
    if (coffeeCount > 0) {
        banner.style.display = '';
        const text = coffeeCount === 1
            ? '1 player needs a break!'
            : `${coffeeCount} players need a break!`;
        document.getElementById('coffeeBannerText').textContent = text;
    } else {
        banner.style.display = 'none';
    }
}

function preselectAcceptValue(consensus, average) {
    const select = document.getElementById('acceptSelect');
    if (!select || select.options.length === 0) return;

    // 1. If consensus exists and is in the scale — use it
    if (consensus) {
        const opt = Array.from(select.options).find(o => o.value === String(consensus));
        if (opt) { select.value = opt.value; return; }
    }

    // 2. Otherwise find closest scale value to the average
    if (average != null) {
        const numericOptions = Array.from(select.options)
            .map(o => ({ value: o.value, num: parseFloat(o.value) }))
            .filter(o => !isNaN(o.num));
        if (numericOptions.length > 0) {
            const closest = numericOptions.reduce((best, cur) =>
                Math.abs(cur.num - average) < Math.abs(best.num - average) ? cur : best
            );
            select.value = closest.value;
            return;
        }
    }
}

function renderRevealedVotes() {
    renderPlayers();
}

function renderResults(results) {
    document.getElementById('resultsRoomCode').textContent = state.roomCode;
    const tbody = document.getElementById('resultsBody');
    const tfoot = document.getElementById('resultsFoot');

    let totalEstimate = 0;
    let hasNumericTotal = false;

    tbody.innerHTML = results.map(r => {
        const votesStr = Object.entries(r.votes || {}).map(([name, val]) => `${name}: ${val}`).join(', ');
        const est = r.estimate || '-';

        if (r.estimate && !isNaN(parseFloat(r.estimate))) {
            totalEstimate += parseFloat(r.estimate);
            hasNumericTotal = true;
        }

        return `
            <tr>
                <td>${r.index}</td>
                <td><strong>${escapeHtml(r.subject)}</strong></td>
                <td>${escapeHtml(r.description || '')}</td>
                <td><strong>${escapeHtml(est)}</strong></td>
                <td>${escapeHtml(votesStr)}</td>
            </tr>
        `;
    }).join('');

    tfoot.innerHTML = hasNumericTotal ? `
        <tr>
            <td colspan="3" style="text-align:right"><strong>Total:</strong></td>
            <td><strong>${totalEstimate}</strong></td>
            <td></td>
        </tr>
    ` : '';
}

// ===== Actions =====
async function castVote(value) {
    state.selectedVote = value;
    renderVotingCards();
    await connection.invoke("Vote", state.roomCode, value);
}

// ===== Export (mid-session) =====
function showExportMenu() {
    // Remove existing menu if any
    const existing = document.getElementById('exportMenu');
    if (existing) { existing.remove(); return; }

    const btn = document.getElementById('btnExport');
    const menu = document.createElement('div');
    menu.id = 'exportMenu';
    menu.className = 'export-menu';
    menu.innerHTML = `
        <button class="export-menu-item" data-format="csv">&#128196; CSV</button>
        <button class="export-menu-item" data-format="json">&#128196; JSON</button>
    `;
    btn.parentElement.style.position = 'relative';
    btn.parentElement.appendChild(menu);

    menu.querySelectorAll('.export-menu-item').forEach(item => {
        item.addEventListener('click', async () => {
            state._exportPending = true;
            state._exportFormat = item.dataset.format;
            menu.remove();
            await connection.invoke("GetResults", state.roomCode);
        });
    });

    // Close on outside click
    setTimeout(() => {
        document.addEventListener('click', function closeMenu(e) {
            if (!menu.contains(e.target) && e.target !== btn) {
                menu.remove();
                document.removeEventListener('click', closeMenu);
            }
        });
    }, 0);
}

// ===== Downloads =====
function downloadCsv() {
    if (!state.results) return;
    const rows = [['#', 'Subject', 'Estimate', 'Votes']];

    state.results.forEach(r => {
        const votesStr = Object.entries(r.votes || {}).map(([n, v]) => `${n}:${v}`).join(' | ');
        rows.push([r.index, r.subject, r.estimate || '', votesStr]);
    });

    const csv = rows.map(r => r.map(c => `"${String(c).replace(/"/g, '""')}"`).join(',')).join('\n');
    downloadFile(`planning-poker-${state.roomCode}.csv`, csv, 'text/csv');
}

function downloadJson() {
    if (!state.results) return;
    const compact = state.results.map(r => ({
        index: r.index,
        subject: r.subject,
        estimate: r.estimate,
        votes: r.votes
    }));
    const json = JSON.stringify({ roomCode: state.roomCode, results: compact }, null, 2);
    downloadFile(`planning-poker-${state.roomCode}.json`, json, 'application/json');
}

function downloadFile(filename, content, type) {
    const blob = new Blob([content], { type });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
}

// ===== Utilities =====
function showToast(msg, isError = false) {
    const toast = document.getElementById('toast');
    toast.textContent = msg;
    toast.className = `toast show${isError ? ' error' : ''}`;
    setTimeout(() => toast.className = 'toast', 3000);
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function updateUrl(path) {
    window.history.pushState({}, '', path);
}

async function ensureConnected() {
    if (!connection || connection.state === 'Disconnected') {
        await initConnection();
    }
}

// ===== Card Timer =====
function startCardTimer() {
    stopCardTimer();
    if (!state.secondsPerCard) return;

    // Set deadline from now (new round) or from existing deadline (rejoin)
    if (!state.timerDeadline || state.timerDeadline < new Date()) {
        state.timerDeadline = new Date(Date.now() + state.secondsPerCard * 1000);
    }

    const timerEl = document.getElementById('cardTimer');
    const valueEl = document.getElementById('cardTimerValue');
    timerEl.style.display = '';

    timerInterval = setInterval(() => {
        const remaining = Math.max(0, Math.floor((state.timerDeadline - Date.now()) / 1000));
        const mins = Math.floor(remaining / 60);
        const secs = remaining % 60;
        valueEl.textContent = `${mins}:${secs.toString().padStart(2, '0')}`;

        // Color states
        const ratio = remaining / state.secondsPerCard;
        timerEl.classList.remove('warning', 'danger');
        if (remaining === 0) {
            timerEl.classList.add('danger');
            valueEl.textContent = "Time's up!";
            // Stop interval but keep the element visible
            clearInterval(timerInterval);
            timerInterval = null;
        } else if (ratio <= 0.15) {
            timerEl.classList.add('danger');
        } else if (ratio <= 0.35) {
            timerEl.classList.add('warning');
        }
    }, 250);
}

function stopCardTimer() {
    if (timerInterval) {
        clearInterval(timerInterval);
        timerInterval = null;
    }
    state.timerDeadline = null;
    // Hide the timer element (called on reveal, new round, game finished)
    const timerEl = document.getElementById('cardTimer');
    if (timerEl) {
        timerEl.style.display = 'none';
        timerEl.classList.remove('warning', 'danger');
    }
}

// ===== URL Routing =====
function navigateToJoin(code) {
    document.getElementById('joinRoomCode').textContent = code;
    showScreen('join');
}

async function handleRoute() {
    const path = window.location.pathname;

    // /join/CODE — show join screen
    const joinMatch = path.match(/^\/join\/([A-Za-z0-9]+)$/);
    if (joinMatch) {
        const code = joinMatch[1].toUpperCase();
        // Try auto-rejoin if we have a session for this room
        if (await tryAutoRejoin(code)) return;
        navigateToJoin(code);
        return;
    }

    // /room/CODE — try to rejoin from session
    const roomMatch = path.match(/^\/room\/([A-Za-z0-9]+)$/);
    if (roomMatch) {
        const code = roomMatch[1].toUpperCase();
        if (await tryAutoRejoin(code)) return;
        navigateToJoin(code);
        return;
    }

    // Default: home
    showScreen('home');
}

async function tryAutoRejoin(roomCode) {
    const session = loadSession();
    if (!session || session.roomCode !== roomCode || !session.playerId) return false;

    try {
        await ensureConnected();
        await connection.invoke("RejoinRoom", session.roomCode, session.playerId);
        return true;
    } catch (e) {
        clearSession();
        return false;
    }
}

// ===== Init =====
handleRoute();
window.addEventListener('popstate', handleRoute);
