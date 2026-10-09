/**
 * Per-tab session persistence: survives reloads but never stores credentials.
 * Tokens remain readable to JavaScript; use a HttpOnly refresh-cookie flow
 * when long-lived sessions are required.
 */
const STORAGE_KEY = 'finance.auth.v1';

type StoredSession = { token: string; expiresAt: string };

export function readSession(): string {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return '';
    const data: StoredSession = JSON.parse(raw);
    if (typeof data.token !== 'string' || typeof data.expiresAt !== 'string' ||
        !Number.isFinite(Date.parse(data.expiresAt)) || Date.parse(data.expiresAt) <= Date.now()) {
      clearSession();
      return '';
    }
    return data.token;
  } catch {
    clearSession();
    return '';
  }
}

export function writeSession(token: string, expiresAt: string): void {
  if (!token || !Number.isFinite(Date.parse(expiresAt)) || Date.parse(expiresAt) <= Date.now()) {
    clearSession();
    throw new Error('Sessão inválida. Faça login novamente.');
  }
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ token, expiresAt } satisfies StoredSession));
  } catch {
    // Some browsers disable storage. Login still works in memory until refresh.
  }
}

export function clearSession(): void {
  try {
    sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    // Storage can be unavailable in private browsing contexts.
  }
}
