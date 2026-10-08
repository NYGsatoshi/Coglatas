// Observe only the fixture's real SignalR control frame. Never log its payload.
export function isAuthorizationChangeFrame(frame, expected) {
  const payload = typeof frame.payload === 'string'
    ? frame.payload
    : Buffer.isBuffer(frame.payload) ? frame.payload.toString('utf8') : '';
  return payload.split('\u001e').some((record) => {
    try {
      const message = JSON.parse(record);
      const event = message?.arguments?.[0];
      return message?.type === 1 && message.target === 'DurableEvent' &&
        message.arguments.length === 1 &&
        event?.eventType === 'Security.AuthorizationStateChanged.v1' &&
        event.payloadSchemaVersion === 1 &&
        event.payload?.affectedUserId === expected.affectedUserId &&
        event.payload.scopeType === expected.scopeType &&
        event.payload.scopeId === expected.scopeId &&
        event.payload.change === expected.change;
    } catch {
      return false;
    }
  });
}

export function isWorkspaceRevocationFrame(frame, userId, workspaceId) {
  return isAuthorizationChangeFrame(frame, {
    affectedUserId: userId, scopeType: 'workspace', scopeId: workspaceId, change: 'revoked',
  });
}

// Register before navigation so reconnects are observed too. Retain only a
// boolean for the current owned mutation, rather than raw frames or a history.
export function observeAuthorizationChanges(page) {
  let observation;
  page.on('websocket', (socket) => {
    if (new URL(socket.url()).pathname !== '/hubs/app') { return; }
    socket.on('framereceived', (frame) => {
      if (observation && isAuthorizationChangeFrame(frame, observation.expected)) {
        observation.received = true;
      }
    });
  });
  return {
    expectChange(expected) {
      observation = { expected: { ...expected }, received: false };
      return observation;
    },
  };
}
