export interface ConfigPayload {
  root: string;
  levels: { id: string; text: string }[];
  conveyors: { id: string; text: string }[];
  index: string | null;
}

async function call<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  const json = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(json.error ?? `${method} ${url}: ${res.status}`);
  return json as T;
}

export const api = {
  load: () => call<ConfigPayload>('GET', '/api/config'),
  saveLevel: (id: string, text: string) => call('PUT', `/api/levels/${id}`, { text }),
  deleteLevel: (id: string) => call('DELETE', `/api/levels/${id}`),
  saveConveyor: (id: string, text: string) => call('PUT', `/api/conveyors/${id}`, { text }),
  deleteConveyor: (id: string) => call('DELETE', `/api/conveyors/${id}`),
  saveIndex: (text: string) => call('PUT', '/api/index', { text }),
  levelTool: (args: string[]) => call<{ code: number; stdout: string; stderr: string }>('POST', '/api/leveltool', { args }),
};
