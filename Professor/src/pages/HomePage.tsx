import { useState, useEffect, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { getExams, createExam } from '../api/exams';
import { createSession, getSession } from '../api/sessions';
import type { ExamResponse, SessionResponse } from '../types';

export function HomePage() {
  const { auth, logout } = useAuth();
  const navigate = useNavigate();
  const token = auth!.token;

  const [exams, setExams] = useState<ExamResponse[]>([]);
  const [examsLoading, setExamsLoading] = useState(true);

  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [duration, setDuration] = useState('');
  const [createLoading, setCreateLoading] = useState(false);
  const [createError, setCreateError] = useState('');

  const [pendingSessions, setPendingSessions] = useState<Record<string, SessionResponse>>(() => {
    try {
      const stored = localStorage.getItem('oeims_pending_sessions');
      return stored ? (JSON.parse(stored) as Record<string, SessionResponse>) : {};
    } catch {
      return {};
    }
  });
  const [sessionLoading, setSessionLoading] = useState<string | null>(null);
  const [sessionError, setSessionError] = useState('');

  useEffect(() => {
    localStorage.setItem('oeims_pending_sessions', JSON.stringify(pendingSessions));
  }, [pendingSessions]);

  useEffect(() => {
    getExams(token)
      .then(setExams)
      .catch(console.error)
      .finally(() => setExamsLoading(false));
  }, [token]);

  useEffect(() => {
    const entries = Object.entries(pendingSessions);
    if (entries.length === 0) return;
    Promise.allSettled(
      entries.map(([examId, session]) =>
        getSession(token, session.id).then(fresh =>
          setPendingSessions(prev => ({ ...prev, [examId]: fresh }))
        )
      )
    );
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  const handleCreateExam = async (e: FormEvent) => {
    e.preventDefault();
    setCreateError('');
    setCreateLoading(true);
    try {
      const exam = await createExam(token, {
        title,
        description: description.trim() || undefined,
        durationMins: parseInt(duration, 10),
      });
      setExams(prev => [exam, ...prev]);
      setTitle('');
      setDescription('');
      setDuration('');
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Failed to create exam.');
    } finally {
      setCreateLoading(false);
    }
  };

  const handleCreateSession = async (examId: string) => {
    setSessionLoading(examId);
    setSessionError('');
    try {
      const session = await createSession(token, examId);
      setPendingSessions(prev => ({ ...prev, [examId]: session }));
    } catch (err) {
      setSessionError(err instanceof Error ? err.message : 'Failed to create session.');
    } finally {
      setSessionLoading(null);
    }
  };

  return (
    <div className="home-page">
      <header className="top-bar">
        <div className="top-bar-brand">
          <span className="brand">OEIMS</span>
          <span className="brand-sub">Professor Console</span>
        </div>
        <div className="top-bar-right">
          <span className="user-email">{auth!.email}</span>
          <button className="btn btn-ghost btn-sm" onClick={logout}>Sign out</button>
        </div>
      </header>

      <main className="home-content">
        <section className="panel panel-left">
          <h2>New Exam</h2>
          <form onSubmit={handleCreateExam} className="exam-form">
            <div className="field">
              <label>Title</label>
              <input
                type="text"
                value={title}
                onChange={e => setTitle(e.target.value)}
                placeholder="LEIC-AED T1 C.3.07"
                pattern="^[A-Z]{4}-[A-Z]{3} (T1|T2|T3|T4|T5) C\.\d\.\d{2}$"
                required
              />
              <span className="field-hint">Format: COURSE-SUB T# C.floor.room — e.g. LEIC-AED T1 C.3.07</span>
            </div>
            <div className="field">
              <label>Description <span className="optional">(optional)</span></label>
              <textarea
                value={description}
                onChange={e => setDescription(e.target.value)}
                placeholder="Brief description…"
                rows={3}
              />
            </div>
            <div className="field">
              <label>Duration (minutes)</label>
              <input
                type="number"
                value={duration}
                onChange={e => setDuration(e.target.value)}
                placeholder="120"
                min="1"
                required
              />
            </div>
            {createError && <p className="error-msg">{createError}</p>}
            <button type="submit" className="btn btn-primary" disabled={createLoading}>
              {createLoading ? 'Creating…' : 'Create exam'}
            </button>
          </form>
        </section>

        <section className="panel panel-right">
          <h2>Your Exams</h2>

          {examsLoading && <p className="muted">Loading…</p>}

          {!examsLoading && exams.length === 0 && (
            <p className="muted">No exams yet — create one on the left.</p>
          )}

          {sessionError && <p className="error-msg">{sessionError}</p>}

          <ul className="exam-list">
            {exams.map(exam => {
              const session = pendingSessions[exam.id];
              return (
                <li key={exam.id} className="exam-item">
                  <div className="exam-info">
                    <span className="exam-title">{exam.title}</span>
                    <span className="exam-meta">{exam.durationMins} min</span>
                    {session && (
                      <span className={`status-badge status-${session.status.toLowerCase()}`}>
                        {session.status === 'PENDING' ? 'Not started'
                          : session.status === 'ACTIVE' ? 'Live'
                          : 'Ended'}
                      </span>
                    )}
                  </div>
                  {exam.description && (
                    <p className="exam-desc">{exam.description}</p>
                  )}

                  {session ? (
                    <div className="session-ready">
                      <div className="session-code-block">
                        <span className="session-code-label">Student code</span>
                        <span className="session-code">{session.code}</span>
                      </div>
                      <button
                        className="btn btn-primary btn-sm"
                        onClick={() => navigate(`/session/${session.id}`)}
                      >
                        Open session →
                      </button>
                    </div>
                  ) : (
                    <button
                      className="btn btn-secondary btn-sm"
                      disabled={sessionLoading === exam.id}
                      onClick={() => handleCreateSession(exam.id)}
                    >
                      {sessionLoading === exam.id ? 'Creating…' : 'Create session'}
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        </section>
      </main>
    </div>
  );
}
