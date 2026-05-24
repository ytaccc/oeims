import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import {
  getSession, startSession, endSession,
  getParticipants, getEvents,
} from '../api/sessions';
import { useSessionWs } from '../hooks/useSessionWs';
import { StudentCard } from '../components/StudentCard';
import type { SessionResponse, ParticipantResponse, EventResponse } from '../types';

export function SessionPage() {
  const { id } = useParams<{ id: string }>();
  const { auth, logout } = useAuth();
  const navigate = useNavigate();
  const token = auth!.token;

  const [session, setSession] = useState<SessionResponse | null>(null);
  const [participants, setParticipants] = useState<ParticipantResponse[]>([]);
  const [eventsByParticipant, setEventsByParticipant] = useState<Record<string, EventResponse[]>>({});
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState(false);
  const [actionError, setActionError] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    if (!id) return;
    Promise.all([
      getSession(token, id),
      getParticipants(token, id),
      getEvents(token, id),
    ])
      .then(([sess, parts, events]) => {
        setSession(sess);
        setParticipants(parts);
        const grouped: Record<string, EventResponse[]> = {};
        for (const ev of events) {
          (grouped[ev.participantId] ??= []).push(ev);
        }
        setEventsByParticipant(grouped);
      })
      .catch(() => setError('Failed to load session.'))
      .finally(() => setLoading(false));
  }, [id, token]);

  // Poll for new joiners — WebSocket only delivers events, not joins
  useEffect(() => {
    if (!id || !session || session.status === 'ENDED') return;
    const interval = setInterval(async () => {
      try {
        const parts = await getParticipants(token, id);
        setParticipants(parts);
      } catch { /* ignore */ }
    }, 4000);
    return () => clearInterval(interval);
  }, [id, token, session?.status]);

  // Poll events as fallback — WebSocket should deliver these in real-time but
  // this catches any that are missed (e.g. while the WS connection is not yet open).
  useEffect(() => {
    if (!id || !session || session.status === 'ENDED') return;
    const interval = setInterval(async () => {
      try {
        const evs = await getEvents(token, id);
        const grouped: Record<string, EventResponse[]> = {};
        for (const ev of evs) {
          (grouped[ev.participantId] ??= []).push(ev);
        }
        setEventsByParticipant(grouped);
      } catch { /* ignore */ }
    }, 5000);
    return () => clearInterval(interval);
  }, [id, token, session?.status]);

  const wsSessionId = session?.status === 'ACTIVE' ? (id ?? null) : null;

  useSessionWs(wsSessionId, token, {
    onEvent: useCallback((ev: EventResponse) => {
      setEventsByParticipant(prev => ({
        ...prev,
        [ev.participantId]: [...(prev[ev.participantId] ?? []), ev],
      }));
    }, []),
    onStatusUpdate: useCallback((update) => {
      setParticipants(prev =>
        prev.map(p =>
          p.id === update.participantId
            ? { ...p, connectionStatus: update.connectionStatus as ParticipantResponse['connectionStatus'] }
            : p
        )
      );
    }, []),
  });

  const handleStart = async () => {
    if (!id) return;
    setActionLoading(true);
    setActionError('');
    try {
      setSession(await startSession(token, id));
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Failed to start session.');
    } finally {
      setActionLoading(false);
    }
  };

  const handleEnd = async () => {
    if (!id || !confirm('End this session? Students will be disconnected.')) return;
    setActionLoading(true);
    setActionError('');
    try {
      setSession(await endSession(token, id));
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Failed to end session.');
    } finally {
      setActionLoading(false);
    }
  };

  if (loading) {
    return (
      <div className="session-page loading-state">
        <p className="muted">Loading session…</p>
      </div>
    );
  }

  if (error || !session) {
    return (
      <div className="session-page loading-state">
        <p className="error-msg">{error || 'Session not found.'}</p>
        <button className="btn btn-ghost" onClick={() => navigate('/')}>← Back home</button>
      </div>
    );
  }

  const statusLabel =
    session.status === 'PENDING' ? 'Waiting to start' :
    session.status === 'ACTIVE'  ? 'Live' :
                                   'Ended';

  return (
    <div className="session-page">
      <header className="top-bar">
        <div className="top-bar-brand">
          <button className="btn btn-ghost btn-sm" onClick={() => navigate('/')}>← Home</button>
          <span className="brand">Session Monitor</span>
        </div>
        <div className="top-bar-right">
          <span className="user-email">{auth!.email}</span>
          <button className="btn btn-ghost btn-sm" onClick={logout}>Sign out</button>
        </div>
      </header>

      <div className="session-controls">
        <div className="controls-left">
          <span className={`status-badge status-${session.status.toLowerCase()}`}>
            {statusLabel}
          </span>
          <span className="participant-count">
            {participants.length} student{participants.length !== 1 ? 's' : ''}
          </span>
        </div>

        {actionError && <p className="error-msg">{actionError}</p>}
        <div className="controls-right">
          {(session.status === 'PENDING' || session.status === 'ACTIVE') && (
            <div className="code-display">
              <span className="code-label">Code</span>
              <span className="session-code">{session.code}</span>
            </div>
          )}
          {session.status === 'PENDING' && (
            <button className="btn btn-primary" onClick={handleStart} disabled={actionLoading}>
              {actionLoading ? 'Starting…' : 'Start session'}
            </button>
          )}
          {session.status === 'ACTIVE' && (
            <button className="btn btn-danger" onClick={handleEnd} disabled={actionLoading}>
              {actionLoading ? 'Ending…' : 'End session'}
            </button>
          )}
        </div>
      </div>

      {session.status === 'PENDING' && (
        <div className="pending-banner">
          <p>Share the code above with students. Start the session when everyone has joined.</p>
        </div>
      )}

      {session.status !== 'PENDING' && participants.length === 0 && (
        <div className="pending-banner">
          <p className="muted">No students joined this session.</p>
        </div>
      )}

      {participants.length > 0 && (
        <div className="students-grid">
          {participants.map(p => (
            <StudentCard
              key={p.id}
              participant={p}
              events={eventsByParticipant[p.id] ?? []}
            />
          ))}
        </div>
      )}
    </div>
  );
}
