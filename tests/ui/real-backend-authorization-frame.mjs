// Observe only the fixture's real SignalR control frame. Never log its payload.
export function isAuthorizationChangeFrame(frame, expected) {
  return authorizationChangesInFrame(frame).some((change) => matches(change, expected));
}

function authorizationChangesInFrame(frame) {
  const payload = typeof frame.payload === 'string'
    ? frame.payload
    : Buffer.isBuffer(frame.payload) ? frame.payload.toString('utf8') : '';
  return payload.split('\u001e').flatMap((record) => {
    try {
      const message = JSON.parse(record);
      const event = message?.arguments?.[0];
      if (message?.type === 1 && message.target === 'DurableEvent' &&
        message.arguments.length === 1 &&
        event?.eventType === 'Security.AuthorizationStateChanged.v1' &&
        event.payloadSchemaVersion === 1) {
        const fields = ['affectedUserId', 'scopeType', 'scopeId', 'change'];
        if (fields.every((field) => typeof event.payload?.[field] === 'string' &&
          event.payload[field].length > 0 && event.payload[field].length <= 128)) {
          return [Object.fromEntries(fields.map((field) => [field, event.payload[field]]))];
        }
      }
    } catch {
      // Malformed records cannot establish delivery.
    }
    return [];
  });
}

function matches(change, expected) {
  return ['affectedUserId', 'scopeType', 'scopeId', 'change'].every((field) => change[field] === expected[field]);
}

export function isWorkspaceRevocationFrame(frame, userId, workspaceId) {
  return isAuthorizationChangeFrame(frame, {
    affectedUserId: userId, scopeType: 'workspace', scopeId: workspaceId, change: 'revoked',
  });
}

// Register before navigation so reconnects are observed too. Retain only a
// boolean for the current owned mutation and at most 16 safe metadata records.
// Creation assigns its ID in the HTTP response, which can follow its grant frame.
export function observeAuthorizationChanges(page) {
  let observation;
  let recent = [];
  page.on('websocket', (socket) => {
    if (new URL(socket.url()).pathname !== '/hubs/app') { return; }
    socket.on('framereceived', (frame) => {
      const changes = authorizationChangesInFrame(frame);
      recent = [...recent, ...changes].slice(-16);
      if (observation && changes.some((change) => matches(change, observation.expected))) {
        observation.received = true;
      }
    });
  });
  return {
    hasObservedChange(expected) {
      return recent.some((change) => matches(change, expected));
    },
    expectChange(expected) {
      observation = { expected: { ...expected }, received: false };
      return observation;
    },
  };
}
