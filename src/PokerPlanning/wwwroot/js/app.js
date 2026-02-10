// ===== State =====
let connection = null;
let timerInterval = null;
let state = {
    roomCode: null,
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
    timerDeadline: null     // Date object: when current card timer expires
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
    connection.on("Error", onError);

    connection.onreconnecting(() => showToast("Reconnecting..."));
    connection.onreconnected(() => showToast("Reconnected!"));

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

    if (!cardsText) return showToast("Enter at least one question", true);

    await ensureConnected();
    connection.invoke("CreateRoom", ownerName || null, scaleType, cardsText, sessionMinutes);
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

document.getElementById('btnAccept').addEventListener('click', () => {
    const val = document.getElementById('acceptSelect').value;
    connection.invoke("AcceptEstimate", state.roomCode, val);
});

document.getElementById('btnCopyLink').addEventListener('click', () => {
    const url = `${window.location.origin}/join/${state.roomCode}`;
    navigator.clipboard.writeText(url).then(() => showToast("Link copied!"));
});

document.getElementById('btnDownloadCsv').addEventListener('click', downloadCsv);
document.getElementById('btnDownloadJson').addEventListener('click', downloadJson);

// ===== Event Handlers: Server =====
function onRoomCreated(data) {
    state.roomCode = data.roomCode;
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

    renderRoom(data.currentCard);
    startCardTimer();
    showScreen('room');
    updateUrl(`/room/${data.roomCode}`);
}

function onRoomState(data) {
    state.roomCode = data.roomCode;
    state.isOwner = data.isOwner;
    state.isSpectator = data.isSpectator;
    state.scale = data.scale;
    state.players = data.players;
    state.currentCardIndex = data.currentCardIndex;
    state.totalCards = data.totalCards;
    state.roomState = data.state;
    state.selectedVote = null;
    state.votes = data.votes || {};
    state.secondsPerCard = data.secondsPerCard || null;

    // Restore timer from server timestamp
    if (data.secondsPerCard && data.cardTimerStartedAt) {
        const started = new Date(data.cardTimerStartedAt);
        state.timerDeadline = new Date(started.getTime() + data.secondsPerCard * 1000);
    }

    renderRoom(data.currentCard);
    if (data.state === 'Voting') startCardTimer();
    showScreen('room');
    updateUrl(`/room/${data.roomCode}`);

    if (data.state === 'Revealed' && data.votes) {
        renderRevealed(data.votes, data.consensus, data.average);
    }
}

function onPlayerJoined(data) {
    state.players = [...state.players.filter(p => p.name !== data.name), data];
    renderPlayers();
    showToast(`${data.name} joined`);
}

function onPlayerLeft(data) {
    state.players = state.players.filter(p => p.name !== data.playerName);
    if (data.newOwnerName) {
        state.players = state.players.map(p => ({
            ...p,
            isOwner: p.name === data.newOwnerName
        }));
        // If we became the new owner
        const me = state.players.find(p => p.isOwner);
        if (me) state.isOwner = true;
    }
    renderPlayers();
    showToast(`${data.playerName} left`);
}

function onVoteReceived(data) {
    state.players = state.players.map(p =>
        p.name === data.playerName ? { ...p, hasVoted: true } : p
    );
    renderPlayers();
}

function onCardsRevealed(data) {
    state.roomState = 'Revealed';
    state.votes = data.votes;
    stopCardTimer();
    renderRevealed(data.votes, data.consensus, data.average);
}

function onVoteUpdated(data) {
    state.votes[data.playerName] = data.value;
    renderRevealedVotes();
    document.getElementById('consensusValue').textContent = data.consensus || '-';
    document.getElementById('averageValue').textContent = data.average != null ? data.average : '-';
    preselectAcceptValue(data.consensus, data.average);
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

    renderRoom(data.card);
    startCardTimer();
}

function onGameFinished(data) {
    state.roomState = 'Finished';
    state.results = data.results;
    stopCardTimer();
    renderResults(data.results);
    showScreen('results');
}

function onResults(data) {
    state.results = data.results;
    renderResults(data.results);
    showScreen('results');
}

function onError(msg) {
    showToast(msg, true);
}

// ===== Rendering =====
const CARD_COLORS = 8;

function renderRoom(card) {
    document.getElementById('roomCodeBadge').textContent = state.roomCode;
    document.getElementById('questionCounter').textContent =
        `Question ${state.currentCardIndex + 1} / ${state.totalCards}`;

    if (card) {
        document.getElementById('cardSubject').textContent = card.subject;
        document.getElementById('cardDescription').textContent = card.description || '';
    }

    // Apply color based on question index
    const tableCenter = document.querySelector('.table-center');
    for (let i = 0; i < CARD_COLORS; i++) {
        tableCenter.classList.remove(`card-color-${i}`);
    }
    tableCenter.classList.add(`card-color-${state.currentCardIndex % CARD_COLORS}`);

    document.getElementById('statsDisplay').style.display = 'none';

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
        <div class="player-seat">
            <div class="${cardClass}">${cardContent}</div>
            <div class="${nameClass}">${escapeHtml(p.name)}${badge}</div>
        </div>
    `;
}

function renderPlayers() {
    const players = state.players;
    const count = players.length;

    // Distribute players around the table: top, right, bottom, left
    let top = [], right = [], bottom = [], left = [];

    if (count <= 3) {
        // 1-3: all on top
        top = players;
    } else if (count <= 6) {
        // 4-6: top and bottom
        const mid = Math.ceil(count / 2);
        top = players.slice(0, mid);
        bottom = players.slice(mid);
    } else if (count <= 10) {
        // 7-10: top, left, right, bottom
        const perSide = Math.floor((count - 2) / 2);
        const topCount = Math.ceil((count - perSide * 2) / 2);
        const bottomCount = count - perSide * 2 - topCount;
        top = players.slice(0, topCount);
        right = players.slice(topCount, topCount + perSide);
        bottom = players.slice(topCount + perSide, topCount + perSide + bottomCount);
        left = players.slice(topCount + perSide + bottomCount);
    } else {
        // 11-18: distribute evenly
        const quarter = Math.ceil(count / 4);
        top = players.slice(0, quarter);
        right = players.slice(quarter, quarter * 2);
        bottom = players.slice(quarter * 2, quarter * 3);
        left = players.slice(quarter * 3);
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
    container.innerHTML = state.scale.map(val => {
        const selected = state.selectedVote === val ? 'selected' : '';
        return `<button class="vote-btn ${selected}" onclick="castVote('${escapeHtml(val)}')">${escapeHtml(val)}</button>`;
    }).join('');
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
            .filter(v => v !== '?')
            .map(v => `<option value="${escapeHtml(v)}">${escapeHtml(v)}</option>`)
            .join('');
    }
}

function renderRevealed(votes, consensus, average) {
    state.votes = votes;
    renderPlayers();
    renderOwnerControls();

    document.getElementById('statsDisplay').style.display = '';
    document.getElementById('consensusValue').textContent = consensus || '-';
    document.getElementById('averageValue').textContent = average != null ? average : '-';

    preselectAcceptValue(consensus, average);
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

// ===== Downloads =====
function downloadCsv() {
    if (!state.results) return;
    const rows = [['#', 'Subject', 'Description', 'Estimate', 'Votes']];

    state.results.forEach(r => {
        const votesStr = Object.entries(r.votes || {}).map(([n, v]) => `${n}:${v}`).join(' | ');
        rows.push([r.index, r.subject, r.description || '', r.estimate || '', votesStr]);
    });

    const csv = rows.map(r => r.map(c => `"${String(c).replace(/"/g, '""')}"`).join(',')).join('\n');
    downloadFile(`planning-poker-${state.roomCode}.csv`, csv, 'text/csv');
}

function downloadJson() {
    if (!state.results) return;
    const json = JSON.stringify({ roomCode: state.roomCode, results: state.results }, null, 2);
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
        navigateToJoin(joinMatch[1].toUpperCase());
        return;
    }

    // /room/CODE — try to rejoin (shows join screen if no connection)
    const roomMatch = path.match(/^\/room\/([A-Za-z0-9]+)$/);
    if (roomMatch) {
        navigateToJoin(roomMatch[1].toUpperCase());
        return;
    }

    // Default: home
    showScreen('home');
}

// ===== Init =====
handleRoute();
window.addEventListener('popstate', handleRoute);
