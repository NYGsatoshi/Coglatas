// Observe only the fixture's real SignalR control frame. Never log its payload.
export function isWorkspaceRevocationFrame(frame, userId, workspaceId) {
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
        event.payload?.affectedUserId === userId &&
        event.payload.scopeType === 'workspace' &&
        event.payload.scopeId === workspaceId &&
        event.payload.change === 'revoked';
    } catch {
      return false;
    }
  });
}
