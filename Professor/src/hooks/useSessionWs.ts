import { useEffect, useRef } from 'react';
import { wsUrl } from '../api/client';
import type { EventResponse, ParticipantStatusUpdate } from '../types';

interface Callbacks {
  onEvent: (event: EventResponse) => void;
  onStatusUpdate: (update: ParticipantStatusUpdate) => void;
}

export function useSessionWs(
  sessionId: string | null,
  token: string | null,
  callbacks: Callbacks
) {
  const cbRef = useRef(callbacks);
  cbRef.current = callbacks;

  useEffect(() => {
    if (!sessionId || !token) return;

    let ws: WebSocket | null = null;
    let stopped = false;
    let retryTimer: ReturnType<typeof setTimeout> | null = null;

    function connect() {
      ws = new WebSocket(wsUrl(`/ws/console/${sessionId}`, token!));

      ws.onmessage = (e) => {
        try {
          const msg = JSON.parse(e.data as string) as Record<string, unknown>;
          if ('monitorName' in msg) {
            cbRef.current.onEvent(msg as unknown as EventResponse);
          } else if ('connectionStatus' in msg) {
            cbRef.current.onStatusUpdate(msg as unknown as ParticipantStatusUpdate);
          }
        } catch { /* ignore malformed frames */ }
      };

      ws.onerror = () => console.warn('Session WebSocket error');

      ws.onclose = (e) => {
        if (stopped) return;
        console.warn('Session WebSocket closed (code %d) — reconnecting in 3 s', e.code);
        retryTimer = setTimeout(connect, 3000);
      };
    }

    connect();

    return () => {
      stopped = true;
      if (retryTimer !== null) clearTimeout(retryTimer);
      ws?.close();
    };
  }, [sessionId, token]);
}
