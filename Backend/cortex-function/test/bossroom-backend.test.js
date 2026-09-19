// Runs the function handler against an in-memory mock of the Cortex REST API.
// node --test Backend/cortex-function/test
const test = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');
const crypto = require('node:crypto');

const PROJECT = 'proj-1';
let server;
let state;

// mirrors the token format of the function, so the test can build tokens the function must reject
function signPlayerToken(payload) {
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  const signature = crypto.createHmac('sha256', process.env.PLAYER_TOKEN_SECRET).update(body).digest('base64url');
  return `${body}.${signature}`;
}

function resetState() {
  state = { participants: new Map(), gatherings: new Map(), tokens: [], sessions: new Map() };
}

function formatCode(code) {
  return `${code.slice(0, 4)}-${code.slice(4)}`;
}

function gatheringResponse(g, includeMembers) {
  const active = g.members.filter((m) => m.status !== 'left');
  const res = { ...g, joinCode: formatCode(g.joinCode), memberCount: active.length };
  if (includeMembers) res.members = g.members;
  else delete res.members;
  return res;
}

function startMock() {
  return new Promise((resolve) => {
    server = http.createServer(async (req, res) => {
      let raw = '';
      for await (const chunk of req) raw += chunk;
      const body = raw ? JSON.parse(raw) : {};
      const url = new URL(req.url, 'http://localhost');
      const send = (status, data) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(data === undefined ? '' : JSON.stringify(data)); };

      if (req.headers['x-api-key'] !== 'test-key') return send(401, { message: 'unauthorized' });
      const prefix = `/api/projects/${PROJECT}`;
      const path = url.pathname;

      if (req.method === 'POST' && path === `${prefix}/participants`) {
        let p = [...state.participants.values()].find((x) => x.externalUserId === body.externalUserId);
        if (!p) { p = { id: crypto.randomUUID(), externalUserId: body.externalUserId, displayName: body.displayName }; state.participants.set(p.id, p); }
        return send(201, p);
      }
      if (req.method === 'POST' && path === `${prefix}/gatherings`) {
        const code = crypto.randomBytes(4).toString('hex').toUpperCase();
        const g = {
          id: crypto.randomUUID(), name: body.name, type: body.type, status: 'active', maxMembers: body.maxMembers ?? 10,
          accessPolicy: body.accessPolicy ?? 'private', listed: body.listed ?? false, roomId: body.roomId ?? code, joinCode: code,
          autoStartSession: !!body.autoStartSession, sessionId: null, ownerId: body.ownerId ?? null, properties: body.properties ?? {},
          createdAt: new Date(Date.now() + state.gatherings.size).toISOString(), members: [],
        };
        if (body.ownerId) g.members.push({ id: crypto.randomUUID(), participantId: body.ownerId, displayName: state.participants.get(body.ownerId)?.displayName, role: 'owner', status: 'joined' });
        state.gatherings.set(g.id, g);
        return send(201, gatheringResponse(g, false));
      }
      if (req.method === 'GET' && path === `${prefix}/gatherings`) {
        let list = [...state.gatherings.values()];
        if (url.searchParams.get('type')) list = list.filter((g) => g.type === url.searchParams.get('type'));
        if (url.searchParams.get('status')) list = list.filter((g) => g.status === url.searchParams.get('status'));
        if (url.searchParams.get('listed')) list = list.filter((g) => String(g.listed) === url.searchParams.get('listed'));
        return send(200, { gatherings: list.map((g) => gatheringResponse(g, false)), total: list.length });
      }
      let m;
      if ((m = path.match(new RegExp(`^${prefix}/gatherings/lookup/(\\w+)$`)))) {
        const g = [...state.gatherings.values()].find((x) => x.joinCode === m[1].replace(/-/g, '').toUpperCase());
        return g ? send(200, gatheringResponse(g, false)) : send(404, { message: 'not found' });
      }
      if (req.method === 'POST' && path === `${prefix}/gatherings/join`) {
        const g = [...state.gatherings.values()].find((x) => x.joinCode === body.joinCode);
        if (!g) return send(404, { message: 'not found' });
        const existing = g.members.find((x) => x.participantId === body.participantId);
        if (existing) existing.status = 'joined';
        else g.members.push({ id: crypto.randomUUID(), participantId: body.participantId, role: 'member', status: 'joined' });
        return send(200, { success: true });
      }
      if ((m = path.match(new RegExp(`^${prefix}/gatherings/([0-9a-f-]{36})(/.*)?$`)))) {
        const g = state.gatherings.get(m[1]);
        if (!g) return send(404, { message: 'not found' });
        const sub = m[2] || '';
        if (req.method === 'GET' && sub === '') return send(200, gatheringResponse(g, url.searchParams.get('includeMembers') === 'true'));
        if (req.method === 'DELETE' && sub === '') { g.status = 'cancelled'; return send(200, gatheringResponse(g, false)); }
        if (req.method === 'POST' && sub === '/end') { g.status = 'ended'; return send(200, gatheringResponse(g, false)); }
        if (req.method === 'POST' && sub === '/start') {
          if (!body.roomId) return send(400, { message: ['roomId must be a string'] });
          g.status = 'started';
          if (g.autoStartSession) { g.sessionId = crypto.randomUUID(); state.sessions.set(g.sessionId, []); }
          return send(200, gatheringResponse(g, false));
        }
        if (req.method === 'POST' && sub === '/members') {
          const active = g.members.filter((x) => x.status !== 'left').length;
          if (active >= g.maxMembers) return send(400, { message: 'Gathering is full' });
          g.members.push({ id: crypto.randomUUID(), participantId: body.participantId, role: 'member', status: 'joined' });
          return send(201, {});
        }
        const del = sub.match(/^\/members\/(.+)$/);
        if (req.method === 'DELETE' && del) { g.members.filter((x) => x.participantId === del[1]).forEach((x) => { x.status = 'left'; }); return send(204); }
      }
      if (req.method === 'POST' && path === `${prefix}/token`) {
        state.tokens.push(body);
        return send(200, { token: `token-${body.roomId}-${body.userId}` });
      }
      if ((m = path.match(new RegExp(`^${prefix}/sessions/([0-9a-f-]{36})/messages$`)))) {
        return send(200, state.sessions.get(m[1]) || []);
      }
      if (req.method === 'POST' && path === '/api/plugins/annotations/messages/batch') {
        const out = {};
        for (const id of body.messageIds) if (id === 'msg-bad') out[id] = [{ type: 'profanity', content: { flagged: true, categories: { harassment: true, hate: false } } }];
        return send(200, out);
      }
      send(404, { message: `mock route missing ${req.method} ${path}` });
    });
    server.listen(0, '127.0.0.1', () => resolve(server.address().port));
  });
}

let backend;

test.before(async () => {
  const port = await startMock();
  process.env.BOSSROOM_API_URL = `http://127.0.0.1:${port}/`;
  process.env.BOSSROOM_API_KEY = 'test-key';
  process.env.PLAYER_TOKEN_SECRET = 'a'.repeat(40);
  process.env.TRANSCRIPTION = 'true';
  backend = require('../bossroom-backend.js');
});

test.after(() => server.close());
test.beforeEach(() => resetState());

async function call(method, path, body, token, asText = true) {
  const event = {
    method, path, projectId: PROJECT, headers: token ? { 'x-player-token': token } : {},
    body: body === undefined ? '' : asText ? JSON.stringify(body) : body, rawBody: body === undefined ? '' : JSON.stringify(body),
  };
  const result = await backend.handler(event, {});
  return { status: result.statusCode, data: result.body ? JSON.parse(result.body) : undefined };
}

async function login(name, device = crypto.randomUUID()) {
  const r = await call('POST', '/login', { deviceId: device, profile: 'p1', displayName: name });
  assert.equal(r.status, 200, JSON.stringify(r.data));
  return r.data;
}

test('login is stable per device and profile', async () => {
  const a = await login('Alice', 'device-0001');
  const b = await login('Alice', 'device-0001');
  assert.equal(a.playerId, b.playerId);
  assert.ok(a.playerToken.includes('.'));
});

test('rejects missing, forged and expired tokens', async () => {
  const alice = await login('Alice');
  assert.equal((await call('GET', '/lobbies')).status, 401);
  const [body] = alice.playerToken.split('.');
  assert.equal((await call('GET', '/lobbies', undefined, `${body}.forged`)).status, 401);
  const expired = signPlayerToken({ pid: alice.playerId, exp: 1 });
  assert.equal((await call('GET', '/lobbies', undefined, expired)).status, 401);
});

test('host creates lobby, client joins by list, code and quick join', async () => {
  const host = await login('Host');
  const created = await call('POST', '/lobbies', { name: 'Boss Fight', isPrivate: false, maxMembers: 3 }, host.playerToken);
  assert.equal(created.status, 201, JSON.stringify(created.data));
  assert.equal(created.data.ownerId, host.playerId);
  assert.match(created.data.roomId, /^bossroom-/);
  assert.equal(created.data.members.length, 1);

  const c1 = await login('C1');
  const list = await call('GET', '/lobbies', undefined, c1.playerToken);
  assert.equal(list.data.lobbies.length, 1);
  assert.equal((await call('POST', `/lobbies/${created.data.id}/join`, undefined, c1.playerToken)).status, 200);

  const c2 = await login('C2');
  const byCode = await call('POST', `/lobbies/code/${created.data.joinCode}/join`, undefined, c2.playerToken);
  assert.equal(byCode.status, 200, JSON.stringify(byCode.data));
  assert.equal(byCode.data.memberCount, 3);

  const c3 = await login('C3');
  assert.equal((await call('POST', `/lobbies/code/${created.data.joinCode}/join`, undefined, c3.playerToken)).data.error, 'lobby_full');
  assert.equal((await call('POST', '/lobbies/quickjoin', undefined, c3.playerToken)).status, 404);
  assert.equal((await call('GET', '/lobbies', undefined, c3.playerToken)).data.lobbies.length, 0, 'full lobbies are not listed');
});

test('private lobbies are hidden and need the code', async () => {
  const host = await login('Host');
  const lobby = (await call('POST', '/lobbies', { name: 'Secret', isPrivate: true }, host.playerToken)).data;
  const client = await login('Client');
  assert.equal((await call('GET', '/lobbies', undefined, client.playerToken)).data.lobbies.length, 0);
  assert.equal((await call('POST', `/lobbies/${lobby.id}/join`, undefined, client.playerToken)).status, 403);
  assert.equal((await call('POST', `/lobbies/code/${lobby.joinCode.replace('-', '').toLowerCase()}/join`, undefined, client.playerToken)).status, 200);
});

test('tokens only for members, start and leave rules', async () => {
  const host = await login('Host');
  const lobby = (await call('POST', '/lobbies', { name: 'Run', isPrivate: false }, host.playerToken)).data;
  const client = await login('Client');

  assert.equal((await call('POST', `/lobbies/${lobby.id}/token`, undefined, client.playerToken)).status, 403);
  await call('POST', `/lobbies/${lobby.id}/join`, undefined, client.playerToken);
  const token = await call('POST', `/lobbies/${lobby.id}/token`, undefined, client.playerToken);
  assert.equal(token.status, 200);
  assert.equal(token.data.roomId, lobby.roomId);
  assert.deepEqual(state.tokens.at(-1), { roomId: lobby.roomId, userId: client.playerId });

  assert.equal((await call('POST', `/lobbies/${lobby.id}/start`, undefined, client.playerToken)).status, 403);
  const started = await call('POST', `/lobbies/${lobby.id}/start`, undefined, host.playerToken);
  assert.equal(started.data.status, 'started');
  assert.ok(started.data.sessionId);

  assert.equal((await call('POST', `/lobbies/${lobby.id}/kick`, { playerId: host.playerId }, client.playerToken)).status, 403);
  assert.equal((await call('POST', `/lobbies/${lobby.id}/kick`, { playerId: client.playerId }, host.playerToken)).status, 200);
  assert.equal((await call('GET', `/lobbies/${lobby.id}`, undefined, client.playerToken)).status, 403, 'kicked player lost access');

  await call('POST', `/lobbies/${lobby.id}/leave`, undefined, host.playerToken);
  assert.equal(state.gatherings.get(lobby.id).status, 'ended', 'lobby ends with its host');
});

test('transcript returns new messages with moderation flags', async () => {
  const host = await login('Host');
  const lobby = (await call('POST', '/lobbies', { name: 'Talk', isPrivate: false }, host.playerToken)).data;
  const started = (await call('POST', `/lobbies/${lobby.id}/start`, undefined, host.playerToken)).data;
  state.sessions.set(started.sessionId, [
    { id: 'msg-ok', senderName: 'Host', content: 'hello', timestamp: '2026-09-17T10:00:00.000Z' },
    { id: 'msg-bad', senderName: 'Host', content: 'bad words', timestamp: '2026-09-17T10:00:05.000Z' },
  ]);

  const all = await call('GET', `/lobbies/${lobby.id}/transcript`, undefined, host.playerToken);
  assert.equal(all.data.messages.length, 2);
  assert.deepEqual(all.data.messages[1].categories, ['harassment']);
  assert.equal(all.data.messages[1].flagged, true);

  const after = await call('GET', `/lobbies/${lobby.id}/transcript/${encodeURIComponent('2026-09-17T10:00:00.000Z')}`, undefined, host.playerToken);
  assert.deepEqual(after.data.messages.map((m) => m.id), ['msg-bad']);
});

test('answers a health check on the base URL without a token', async () => {
  const r = await call('GET', '/');
  assert.equal(r.status, 200);
  assert.equal(r.data.service, 'bossroom-backend');
  assert.equal(r.data.projectId, PROJECT);
  assert.ok(Array.isArray(r.data.routes));
});

test('names the environment variable that is missing', async () => {
  const saved = process.env.BOSSROOM_API_URL;
  delete process.env.BOSSROOM_API_URL;
  try {
    const r = await call('POST', '/login', { deviceId: 'device-0003' });
    assert.equal(r.status, 500);
    assert.equal(r.data.error, 'not_configured');
    assert.match(r.data.message, /BOSSROOM_API_URL/);
  } finally {
    process.env.BOSSROOM_API_URL = saved;
  }
});

test('accepts parsed JSON bodies and reports unknown routes', async () => {
  const r = await call('POST', '/login', { deviceId: 'device-0002', displayName: 'Obj' }, undefined, false);
  assert.equal(r.status, 200);
  assert.equal((await call('GET', '/nope')).status, 404);
});
