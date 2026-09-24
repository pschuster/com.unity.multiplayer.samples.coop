// Boss Room backend as an ODIN Cortex serverless function (runtime nodejs20, authMode "public").
//
// It replaces Unity Authentication and Multiplayer Services (sessions) in the Boss Room sample:
//   - players log in with a device id and get a short-lived player token
//   - lobbies are Cortex gatherings of type "lobby"
//   - ODIN room tokens are only issued to gathering members
//   - transcripts of the gathering's voice session are relayed to the game
//
// Required env vars (names must not start with CORTEX_, which Cortex reserves for itself - it
// removes CORTEX_API_URL and CORTEX_PROJECT_SECRET from the environment before this code runs):
//   BOSSROOM_API_URL     e.g. https://cortex.odin.4players.io
//   BOSSROOM_API_KEY     project scoped key with scopes: sessions, messages, plugins
//   PLAYER_TOKEN_SECRET  random string (32+ chars) used to sign player tokens
// Optional env vars:
//   GAME_ID              default "bossroom", separates games sharing a project
//   TRANSCRIPTION        "true" to start a transcription session when the host starts the game
//   PLAYER_TOKEN_TTL     seconds, default 86400
//
// The invoke proxy drops query strings, so all parameters are path segments or body fields.
// Clients send JSON bodies with Content-Type text/plain; both forms are accepted.

// the runtime resolves built-in modules before project dependencies, so no npm package is needed.
// The binding is not called `crypto` because that name is already taken by the global WebCrypto object.
const nodeCrypto = require('crypto');

const GAME_ID = process.env.GAME_ID || 'bossroom';
const MAX_MEMBERS_LIMIT = 8;

class HttpError extends Error {
  statusCode = 500;
  code = 'internal_error';

  constructor(statusCode, code, message) {
    super(message);
    this.statusCode = statusCode;
    this.code = code;
  }
}

// ---------- helpers ----------

function config(event) {
  const apiUrl = (process.env.BOSSROOM_API_URL || '').replace(/\/+$/, '');
  const apiKey = process.env.BOSSROOM_API_KEY;
  const secret = process.env.PLAYER_TOKEN_SECRET;

  // name the missing ones: a variable that is set but invisible to the function is otherwise hard to spot
  const missing = [
    !apiUrl && 'BOSSROOM_API_URL',
    !apiKey && 'BOSSROOM_API_KEY',
    !secret && 'PLAYER_TOKEN_SECRET',
  ].filter(Boolean);

  if (missing.length > 0) {
    throw new HttpError(500, 'not_configured', `Missing environment variable(s): ${missing.join(', ')}`);
  }

  return { apiUrl, apiKey, secret, projectId: event.projectId };
}

function parseBody(event) {
  if (event.body && typeof event.body === 'object') return event.body;
  const raw = typeof event.body === 'string' && event.body.length > 0 ? event.body : event.rawBody;
  if (!raw) return {};
  try {
    return JSON.parse(raw);
  } catch {
    throw new HttpError(400, 'invalid_json', 'Request body must be JSON');
  }
}

function json(statusCode, body) {
  return { statusCode, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) };
}

function header(event, name) {
  const headers = event.headers || {};
  return headers[name] || headers[name.toLowerCase()];
}

function base64url(input) {
  return Buffer.from(input).toString('base64url');
}

function signPlayerToken(cfg, payload) {
  const body = base64url(JSON.stringify(payload));
  const signature = nodeCrypto.createHmac('sha256', cfg.secret).update(body).digest('base64url');
  return `${body}.${signature}`;
}

function verifyPlayerToken(cfg, event) {
  const token = header(event, 'X-Player-Token');
  if (!token || !token.includes('.')) throw new HttpError(401, 'unauthorized', 'Missing player token');

  const [body, signature] = token.split('.');
  const expected = nodeCrypto.createHmac('sha256', cfg.secret).update(body).digest('base64url');
  const a = Buffer.from(signature);
  const b = Buffer.from(expected);
  if (a.length !== b.length || !nodeCrypto.timingSafeEqual(a, b)) throw new HttpError(401, 'unauthorized', 'Invalid player token');

  const payload = JSON.parse(Buffer.from(body, 'base64url').toString('utf8'));
  if (payload.pid === undefined || payload.exp < Math.floor(Date.now() / 1000)) {
    throw new HttpError(401, 'token_expired', 'Player token expired');
  }
  return payload;
}

/**
 * Calls the project scoped Cortex REST API. Pass `undefined` as body for requests without one.
 * @param {*} cfg configuration from {@link config}
 * @param {string} method HTTP method
 * @param {string} path path below /api/projects/:projectId
 * @param {*} body JSON body, or undefined
 */
async function cortex(cfg, method, path, body) {
  const response = await fetch(`${cfg.apiUrl}/api/projects/${cfg.projectId}${path}`, {
    method,
    headers: { 'X-API-Key': cfg.apiKey, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  const text = await response.text();
  const data = text ? JSON.parse(text) : undefined;
  if (!response.ok) {
    const message = (data && (Array.isArray(data.message) ? data.message.join(', ') : data.message)) || response.statusText;
    const code = response.status === 404 ? 'not_found' : response.status === 403 ? 'forbidden' : 'cortex_error';
    throw new HttpError(response.status >= 500 ? 502 : response.status, code, message);
  }
  return data;
}

function activeMember(gathering, playerId) {
  return (gathering.members || []).find((m) => m.participantId === playerId && m.status !== 'left');
}

function toLobby(gathering) {
  const properties = gathering.properties || {};
  const members = (gathering.members || []).filter((m) => m.status !== 'left');
  return {
    id: gathering.id,
    name: gathering.name,
    joinCode: gathering.joinCode,
    isPrivate: gathering.listed === false,
    status: gathering.status,
    maxMembers: gathering.maxMembers,
    memberCount: gathering.memberCount,
    ownerId: gathering.ownerId,
    roomId: gathering.roomId,
    sessionId: gathering.sessionId,
    createdAt: gathering.createdAt,
    hostName: properties.hostName || '',
    members: members.map((m) => ({ playerId: m.participantId, displayName: m.displayName || '', isOwner: m.role === 'owner' })),
  };
}

async function getGathering(cfg, gatheringId) {
  const gathering = await cortex(cfg, 'GET', `/gatherings/${encodeURIComponent(gatheringId)}?includeMembers=true`, undefined);
  if ((gathering.properties || {}).game !== GAME_ID) throw new HttpError(404, 'not_found', 'Lobby not found');
  return gathering;
}

function assertOpen(gathering) {
  if (gathering.status !== 'active' && gathering.status !== 'started') {
    throw new HttpError(409, 'lobby_closed', 'Lobby is no longer open');
  }
}

function assertCapacity(gathering, playerId) {
  if (activeMember(gathering, playerId)) return;
  if (gathering.memberCount >= gathering.maxMembers) throw new HttpError(409, 'lobby_full', 'Lobby is full');
}

async function listOpenLobbies(cfg) {
  const result = await cortex(cfg, 'GET', '/gatherings?type=lobby&status=active&listed=true&limit=100', undefined);
  return (result.gatherings || [])
    .filter((g) => (g.properties || {}).game === GAME_ID && g.memberCount < g.maxMembers)
    .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt));
}

async function joinGathering(cfg, gathering, player) {
  assertOpen(gathering);
  if (!activeMember(gathering, player.pid)) {
    assertCapacity(gathering, player.pid);
    await cortex(cfg, 'POST', `/gatherings/${gathering.id}/members`, { participantId: player.pid });
  }
  return toLobby(await getGathering(cfg, gathering.id));
}

async function closeGathering(cfg, gathering, actorParticipantId) {
  // the actor lets Cortex enforce the owner rule too; deployments without that check ignore the field
  const actor = actorParticipantId ? { actorParticipantId } : undefined;
  if (gathering.status === 'started') await cortex(cfg, 'POST', `/gatherings/${gathering.id}/end`, actor);
  else if (gathering.status === 'active' || gathering.status === 'pending') {
    const query = actorParticipantId ? `?actorParticipantId=${encodeURIComponent(actorParticipantId)}` : '';
    await cortex(cfg, 'DELETE', `/gatherings/${gathering.id}${query}`, undefined);
  }
}

// ---------- routes ----------

function health(cfg) {
  return json(200, {
    service: 'bossroom-backend',
    game: GAME_ID,
    transcription: process.env.TRANSCRIPTION === 'true',
    projectId: cfg.projectId,
    routes: ['POST /login', 'GET /lobbies', 'POST /lobbies', 'POST /lobbies/quickjoin', 'POST /lobbies/code/{joinCode}/join'],
  });
}

async function login(cfg, event) {
  const { deviceId, profile, displayName } = parseBody(event);
  if (typeof deviceId !== 'string' || deviceId.length < 8 || deviceId.length > 128) {
    throw new HttpError(400, 'invalid_device', 'deviceId must be 8-128 characters');
  }
  const profileName = typeof profile === 'string' && profile.length > 0 ? profile.slice(0, 64) : 'default';
  const name = typeof displayName === 'string' && displayName.trim().length > 0 ? displayName.trim().slice(0, 32) : 'Player';

  const participant = await cortex(cfg, 'POST', '/participants', {
    externalUserId: `${GAME_ID}:${deviceId}:${profileName}`,
    displayName: name,
  });

  // POST only creates or looks up by external user id, so a returning player would keep the name they first
  // signed in with. Push the current one, otherwise the participant list shows a stale name forever.
  if (participant.displayName !== name) {
    await cortex(cfg, 'PATCH', `/participants/${participant.id}`, { displayName: name });
  }

  const ttl = parseInt(process.env.PLAYER_TOKEN_TTL || '86400', 10);
  const expiresAt = Math.floor(Date.now() / 1000) + ttl;
  return json(200, {
    playerId: participant.id,
    displayName: name,
    playerToken: signPlayerToken(cfg, { pid: participant.id, name, exp: expiresAt }),
    expiresAt,
  });
}

async function listLobbies(cfg) {
  const lobbies = await listOpenLobbies(cfg);
  return json(200, { lobbies: lobbies.map(toLobby) });
}

async function createLobby(cfg, event, player) {
  const { name, isPrivate, maxMembers } = parseBody(event);
  if (typeof name !== 'string' || name.trim().length === 0 || name.length > 64) {
    throw new HttpError(400, 'invalid_name', 'Lobby name must be 1-64 characters');
  }
  const capacity = Math.min(Math.max(parseInt(maxMembers, 10) || MAX_MEMBERS_LIMIT, 2), MAX_MEMBERS_LIMIT);

  const created = await cortex(cfg, 'POST', '/gatherings', {
    name: name.trim(),
    type: 'lobby',
    maxMembers: capacity,
    // "private" still accepts the join code, it only hides the lobby from the list
    accessPolicy: isPrivate ? 'private' : 'public',
    listed: !isPrivate,
    ownerId: player.pid,
    roomId: `${GAME_ID}-${nodeCrypto.randomUUID()}`,
    autoStartSession: process.env.TRANSCRIPTION === 'true',
    properties: { game: GAME_ID, hostName: player.name || '' },
  });
  return json(201, toLobby(await getGathering(cfg, created.id)));
}

async function joinByCode(cfg, event, player, code) {
  const joinCode = decodeURIComponent(code || '').replace(/-/g, '').toUpperCase();
  if (!/^[A-Z0-9]{8}$/.test(joinCode)) throw new HttpError(400, 'invalid_code', 'Join code must have 8 characters');

  // lookup first so the lobby can be checked for game and capacity before becoming a member
  const lookup = await cortex(cfg, 'GET', `/gatherings/lookup/${joinCode}`, undefined);
  const gathering = await getGathering(cfg, lookup.id);
  assertOpen(gathering);
  assertCapacity(gathering, player.pid);
  await cortex(cfg, 'POST', '/gatherings/join', { joinCode, participantId: player.pid });
  return json(200, toLobby(await getGathering(cfg, gathering.id)));
}

async function joinById(cfg, player, gatheringId) {
  const gathering = await getGathering(cfg, gatheringId);
  if (gathering.listed === false && !activeMember(gathering, player.pid)) {
    throw new HttpError(403, 'forbidden', 'Private lobbies can only be joined with a code');
  }
  return json(200, await joinGathering(cfg, gathering, player));
}

async function quickJoin(cfg, player) {
  for (const candidate of await listOpenLobbies(cfg)) {
    try {
      return json(200, await joinGathering(cfg, await getGathering(cfg, candidate.id), player));
    } catch (error) {
      // someone else took the last slot, try the next lobby
      if (!(error instanceof HttpError) || (error.code !== 'lobby_full' && error.statusCode !== 400)) throw error;
    }
  }
  throw new HttpError(404, 'no_lobby', 'No open lobby found');
}

async function getLobby(cfg, player, gatheringId) {
  const gathering = await getGathering(cfg, gatheringId);
  if (!activeMember(gathering, player.pid)) throw new HttpError(403, 'not_a_member', 'Not a member of this lobby');
  return json(200, toLobby(gathering));
}

async function leaveLobby(cfg, player, gatheringId) {
  const gathering = await getGathering(cfg, gatheringId);
  if (activeMember(gathering, player.pid)) {
    await cortex(cfg, 'DELETE', `/gatherings/${gathering.id}/members/${player.pid}?actorParticipantId=${encodeURIComponent(player.pid)}`, undefined);
  }
  // Boss Room is host-authoritative: the lobby ends with its host
  if (gathering.ownerId === player.pid) await closeGathering(cfg, gathering, player.pid);
  return json(200, { left: true });
}

async function kickPlayer(cfg, event, player, gatheringId) {
  const { playerId } = parseBody(event);
  const gathering = await getGathering(cfg, gatheringId);
  if (gathering.ownerId !== player.pid) throw new HttpError(403, 'not_owner', 'Only the host can remove players');
  if (typeof playerId !== 'string' || playerId === player.pid) throw new HttpError(400, 'invalid_player', 'Invalid player');
  if (activeMember(gathering, playerId)) {
    await cortex(cfg, 'DELETE', `/gatherings/${gathering.id}/members/${playerId}?actorParticipantId=${encodeURIComponent(player.pid)}`, undefined);
  }
  return json(200, { removed: true });
}

async function roomToken(cfg, player, gatheringId) {
  const gathering = await getGathering(cfg, gatheringId);
  assertOpen(gathering);
  if (!activeMember(gathering, player.pid)) throw new HttpError(403, 'not_a_member', 'Join the lobby before requesting a voice token');

  const { token } = await cortex(cfg, 'POST', '/token', { roomId: gathering.roomId, userId: player.pid });
  return json(200, { roomId: gathering.roomId, token });
}

async function startGame(cfg, player, gatheringId) {
  const gathering = await getGathering(cfg, gatheringId);
  if (gathering.ownerId !== player.pid) throw new HttpError(403, 'not_owner', 'Only the host can start the game');
  if (gathering.status === 'started') return json(200, toLobby(gathering));
  assertOpen(gathering);

  await cortex(cfg, 'POST', `/gatherings/${gathering.id}/start`, { roomId: gathering.roomId, actorParticipantId: player.pid });
  return json(200, toLobby(await getGathering(cfg, gathering.id)));
}

async function endLobby(cfg, player, gatheringId) {
  const gathering = await getGathering(cfg, gatheringId);
  if (gathering.ownerId !== player.pid) throw new HttpError(403, 'not_owner', 'Only the host can end the lobby');
  await closeGathering(cfg, gathering, player.pid);
  return json(200, { ended: true });
}

async function transcript(cfg, player, gatheringId, after) {
  const gathering = await getGathering(cfg, gatheringId);
  if (!activeMember(gathering, player.pid)) throw new HttpError(403, 'not_a_member', 'Not a member of this lobby');
  if (!gathering.sessionId) return json(200, { messages: [] });

  const since = Number.isFinite(Date.parse(after)) ? Date.parse(after) : 0;
  const messages = (await cortex(cfg, 'GET', `/sessions/${gathering.sessionId}/messages`, undefined))
    .filter((m) => Date.parse(m.timestamp) > since)
    .sort((a, b) => Date.parse(a.timestamp) - Date.parse(b.timestamp))
    .slice(-50);

  let annotations = {};
  if (messages.length > 0) {
    // annotation routes are not project scoped
    const response = await fetch(`${cfg.apiUrl}/api/plugins/annotations/messages/batch`, {
      method: 'POST',
      headers: { 'X-API-Key': cfg.apiKey, 'Content-Type': 'application/json' },
      body: JSON.stringify({ messageIds: messages.map((m) => m.id) }),
    });
    if (response.ok) annotations = (await response.json()) || {};
  }

  return json(200, {
    messages: messages.map((m) => {
      const flags = (annotations[m.id] || []).filter((a) => a.type === 'profanity' && a.content && a.content.flagged);
      return {
        id: m.id,
        senderName: m.senderName || '',
        content: m.content || '',
        timestamp: m.timestamp,
        flagged: flags.length > 0,
        categories: flags.flatMap((a) => Object.keys(a.content.categories || {}).filter((c) => a.content.categories[c])),
      };
    }),
  });
}

// ---------- router ----------

// Every handler takes the same request context, so the router can call them uniformly:
//   ctx.cfg    configuration and credentials
//   ctx.event  the raw invoke event
//   ctx.player the verified player token payload (null on public routes)
//   ctx.match  the result of the route's regular expression
const routes = [
  // a plain GET on the base URL is the quickest way to check that the function is deployed and configured
  { method: 'GET', pattern: /^\/$/, auth: false, run: (ctx) => health(ctx.cfg) },
  { method: 'POST', pattern: /^\/login$/, auth: false, run: (ctx) => login(ctx.cfg, ctx.event) },
  { method: 'GET', pattern: /^\/lobbies$/, run: (ctx) => listLobbies(ctx.cfg) },
  { method: 'POST', pattern: /^\/lobbies$/, run: (ctx) => createLobby(ctx.cfg, ctx.event, ctx.player) },
  { method: 'POST', pattern: /^\/lobbies\/quickjoin$/, run: (ctx) => quickJoin(ctx.cfg, ctx.player) },
  { method: 'POST', pattern: /^\/lobbies\/code\/([^/]+)\/join$/, run: (ctx) => joinByCode(ctx.cfg, ctx.event, ctx.player, ctx.match[1]) },
  { method: 'GET', pattern: /^\/lobbies\/([0-9a-f-]{36})$/, run: (ctx) => getLobby(ctx.cfg, ctx.player, ctx.match[1]) },
  { method: 'POST', pattern: /^\/lobbies\/([0-9a-f-]{36})\/join$/, run: (ctx) => joinById(ctx.cfg, ctx.player, ctx.match[1]) },
  { method: 'POST', pattern: /^\/lobbies\/([0-9a-f-]{36})\/leave$/, run: (ctx) => leaveLobby(ctx.cfg, ctx.player, ctx.match[1]) },
  { method: 'POST', pattern: /^\/lobbies\/([0-9a-f-]{36})\/kick$/, run: (ctx) => kickPlayer(ctx.cfg, ctx.event, ctx.player, ctx.match[1]) },
  { method: 'POST', pattern: /^\/lobbies\/([0-9a-f-]{36})\/token$/, run: (ctx) => roomToken(ctx.cfg, ctx.player, ctx.match[1]) },
  { method: 'POST', pattern: /^\/lobbies\/([0-9a-f-]{36})\/start$/, run: (ctx) => startGame(ctx.cfg, ctx.player, ctx.match[1]) },
  { method: 'POST', pattern: /^\/lobbies\/([0-9a-f-]{36})\/end$/, run: (ctx) => endLobby(ctx.cfg, ctx.player, ctx.match[1]) },
  { method: 'GET', pattern: /^\/lobbies\/([0-9a-f-]{36})\/transcript(?:\/([^/]+))?$/, run: (ctx) => transcript(ctx.cfg, ctx.player, ctx.match[1], decodeURIComponent(ctx.match[2] || '')) },
];

exports.handler = async (event) => {
  try {
    const path = (event.path || '/').replace(/\/+$/, '') || '/';
    const method = (event.method || 'GET').toUpperCase();
    const route = routes.find((r) => r.method === method && r.pattern.test(path));
    if (!route) return json(404, { error: 'route_not_found', message: `${method} ${path}` });

    const cfg = config(event);
    const player = route.auth === false ? null : verifyPlayerToken(cfg, event);
    return await route.run({ cfg, event, player, match: path.match(route.pattern) });
  } catch (error) {
    if (error instanceof HttpError) return json(error.statusCode, { error: error.code, message: error.message });
    console.error('[bossroom-backend]', error);
    return json(500, { error: 'internal_error', message: 'Unexpected error' });
  }
};

// End a lobby whose host left without calling /leave (e.g. the game crashed and the member was removed elsewhere).
exports.onGatheringMemberLeft = async (event) => {
  const { gatheringId, participantId } = (event.payload && event.payload.data) || {};
  if (!gatheringId || !participantId) return;
  const cfg = { apiUrl: (process.env.BOSSROOM_API_URL || '').replace(/\/+$/, ''), apiKey: process.env.BOSSROOM_API_KEY, projectId: event.projectId };
  if (!cfg.apiUrl || !cfg.apiKey) return;

  try {
    const gathering = await getGathering(cfg, gatheringId);
    // no acting player here - this runs as a trusted backend call from the event
    if (gathering.ownerId === participantId) await closeGathering(cfg, gathering, undefined);
  } catch (error) {
    if (!(error instanceof HttpError) || error.statusCode !== 404) console.error('[bossroom-backend] member left handler', error);
  }
};
