import { WebSocket } from 'k6/websockets';
import { Counter } from 'k6/metrics';
import { GATE_KEY, WS_URL } from './config.js';
import { field, post } from './api.js';

// The live connection a signed-in browser holds open (/hubs/live, SignalR's JSON protocol over WebSockets). The
// server only sends two content-free signals; the client then reloads what changed, which onSignal does here.
// The access token travels in the URL, as the browser sends it: the socket's metrics are named, never by URL.

const SEPARATOR = '\u001e';

export const liveSignals = new Counter('live_signals');
export const liveFailures = new Counter('live_failures');

export function openLive(session, onSignal) {
  const negotiated = post(session, '/hubs/live/negotiate?negotiateVersion=1', null, { expect: [200] });
  const connectionToken = field(negotiated, 'connectionToken');
  if (!connectionToken) {
    return null;
  }

  const persona = session.user.persona;
  const url = `${WS_URL}/hubs/live?id=${encodeURIComponent(connectionToken)}&access_token=${encodeURIComponent(session.token)}`;
  const socket = new WebSocket(url, null, {
    headers: GATE_KEY ? { 'X-LoadTest-Key': GATE_KEY } : {},
    tags: { name: 'WS /hubs/live', persona },
  });

  let keepAlive = null;
  socket.onopen = () => {
    socket.send(JSON.stringify({ protocol: 'json', version: 1 }) + SEPARATOR);
    // The server closes a connection it hasn't heard from in 30 seconds.
    keepAlive = setInterval(() => socket.send('{"type":6}' + SEPARATOR), 15000);
  };
  socket.onmessage = (event) => {
    for (const frame of String(event.data).split(SEPARATOR)) {
      if (!frame) {
        continue;
      }
      const message = JSON.parse(frame);
      if (message.type === 1 && (message.target === 'notifications' || message.target === 'messages')) {
        liveSignals.add(1, { signal: message.target, persona });
        onSignal(message.target);
      } else if (message.type === 7) {
        liveFailures.add(1, { reason: 'closed_by_server', persona });
        socket.close();
      }
    }
  };
  socket.onerror = () => liveFailures.add(1, { reason: 'error', persona });
  socket.onclose = () => clearInterval(keepAlive);

  return {
    close() {
      clearInterval(keepAlive);
      socket.close();
    },
  };
}
