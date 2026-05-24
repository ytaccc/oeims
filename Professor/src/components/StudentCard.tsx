import { memo } from 'react';
import type { ParticipantResponse, EventResponse } from '../types';

interface Props {
  participant: ParticipantResponse;
  events: EventResponse[];
}

function borderClass(count: number) {
  if (count === 0) return 'card-ok';
  if (count <= 2) return 'card-warn';
  return 'card-danger';
}

function badgeClass(count: number) {
  if (count <= 2) return 'badge-warn';
  return 'badge-danger';
}

function severityClass(s: string) {
  if (s === 'CRITICAL') return 'sev-critical';
  if (s === 'WARNING') return 'sev-warning';
  return 'sev-info';
}

function fmtTime(iso: string) {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

export const StudentCard = memo(function StudentCard({ participant, events }: Props) {
  const bc = borderClass(events.length);

  const dotClass =
    participant.connectionStatus === 'CONNECTED'
      ? 'dot-connected'
      : participant.connectionStatus === 'TIMED_OUT'
      ? 'dot-timeout'
      : 'dot-disconnected';

  const statusLabel = participant.connectionStatus === 'TIMED_OUT'
    ? 'timed out'
    : participant.connectionStatus.toLowerCase();

  return (
    <div className={`student-card ${bc}`}>
      <div className="card-main">
        <span className={`conn-dot ${dotClass}`} />
        <span className="student-email" title={participant.email}>
          {participant.email}
        </span>
        {events.length > 0 && (
          <span className={`violation-badge ${badgeClass(events.length)}`}>
            {events.length}
          </span>
        )}
      </div>

      <p className="card-status">
        {statusLabel}
        {participant.lastHeartbeat && ` · hb ${fmtTime(participant.lastHeartbeat)}`}
      </p>

      {events.length > 0 && (
        <>
          <div className="card-divider" />
          <ul className="card-events">
            {events.map(ev => (
              <li key={ev.id} className="card-event">
                <div className="ce-header">
                  <span className={`ce-sev ${severityClass(ev.severity)}`} />
                  <span className="ce-monitor">{ev.monitorName}</span>
                  <span className="ce-time">{fmtTime(ev.occurredAt)}</span>
                </div>
                <p className="ce-msg">{ev.message}</p>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
});
