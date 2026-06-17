'use strict';

// ── State ──────────────────────────────────────────────────────────
let raidStartTime = null;
let timerInterval = null;

// ── HUD helpers ───────────────────────────────────────────────────
function el(id) { return document.getElementById(id); }

function showHud(map, gameType, raidType) {
    el('hud-map').textContent = map || '—';
    el('badge-gametype').textContent = gameType || '';
    el('badge-raidtype').textContent = raidType || '';
    el('hud').classList.remove('hidden');
}

function hideHud() {
    el('hud').classList.add('hidden');
    stopTimer();
    raidStartTime = null;
}

function startTimer() {
    raidStartTime = Date.now();
    if (timerInterval) clearInterval(timerInterval);
    timerInterval = setInterval(tickTimer, 1000);
    tickTimer();
}

function stopTimer() {
    if (timerInterval) { clearInterval(timerInterval); timerInterval = null; }
    el('hud-timer').textContent = '00:00';
}

function tickTimer() {
    if (!raidStartTime) return;
    const secs = Math.floor((Date.now() - raidStartTime) / 1000);
    const m = String(Math.floor(secs / 60)).padStart(2, '0');
    const s = String(secs % 60).padStart(2, '0');
    el('hud-timer').textContent = `${m}:${s}`;
}

// ── Popup queue ───────────────────────────────────────────────────
const POPUP_SHOW_MS = 5000;
const POPUP_FADE_MS = 400;
const POPUP_GAP_MS = 250;

const queue = [];
let busy = false;

function enqueue(title, body) {
    queue.push({ title, body });
    if (!busy) pump();
}

function pump() {
    if (!queue.length) { busy = false; return; }
    busy = true;
    const { title, body } = queue.shift();
    const zone = el('popup-zone');
    const div = document.createElement('div');
    div.className = 'popup';
    div.innerHTML =
        `<div class="popup-title">${esc(title)}</div>` +
        `<div class="popup-body">${esc(body)}</div>`;
    zone.appendChild(div);

    setTimeout(() => {
        div.classList.add('fade-out');
        setTimeout(() => { div.remove(); setTimeout(pump, POPUP_GAP_MS); }, POPUP_FADE_MS);
    }, POPUP_SHOW_MS);
}

function esc(s) {
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// ── Event handler ─────────────────────────────────────────────────
function handleEvent(eventType, data) {
    const gameType = (data.profileType === 'PVE') ? 'PvE' : 'PvP';

    switch (eventType) {
        case 'MapLoading':
        case 'RaidStarting':
            showHud(data.map, gameType, data.raidType);
            break;

        case 'RaidStarted':
            showHud(data.map, gameType, data.raidType);
            startTimer();
            enqueue('Raid Started', `${data.map || ''} — ${data.raidType || ''}`);
            break;

        case 'RaidExited':
            stopTimer();
            enqueue('Raid Ended', `Exited ${data.map || ''}`);
            break;

        case 'RaidEnded':
            stopTimer();
            break;

        case 'ExitedPostRaidMenus':
            hideHud();
            break;

        case 'TaskFinished':
            enqueue('Task Complete', data.taskId || '');
            break;

        case 'FleaSold':
            enqueue('Flea Sale', `${data.buyer || 'Someone'} bought ${data.soldItemCount || '?'}× ${data.soldItemId || '?'}`);
            break;
    }
}

// ── SignalR connection ─────────────────────────────────────────────
async function connect() {
    const conn = new signalR.HubConnectionBuilder()
        .withUrl('/overlayhub')
        .withAutomaticReconnect([0, 1000, 3000, 5000, 10000])
        .build();

    conn.on('GameEvent', handleEvent);
    conn.onclose(() => setTimeout(connect, 5000));

    try {
        await conn.start();
    } catch {
        setTimeout(connect, 5000);
    }
}

connect();
