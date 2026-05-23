import { apiFetch } from './client';
import type { ExamResponse } from '../types';

export const getExams = (token: string): Promise<ExamResponse[]> =>
  apiFetch<ExamResponse[]>('/exams', {}, token);

export const createExam = (
  token: string,
  payload: { title: string; description?: string; durationMins: number }
): Promise<ExamResponse> =>
  apiFetch<ExamResponse>('/exams', { method: 'POST', body: JSON.stringify(payload) }, token);
