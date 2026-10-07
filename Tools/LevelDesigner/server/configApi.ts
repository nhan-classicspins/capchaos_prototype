import type { Plugin, Connect } from 'vite';
import type { ServerResponse } from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

/**
 * The tool's backend, mounted on Vite's dev / preview server: list, read and write the Unity config files in place
 * (Assets/CapsChaos/Content/Configs/{LevelConfig,ConveyorConfig}), and run Tools/LevelTool (validate / migrate --check)
 * so the C# validators and the solver stay the single source of truth. Unity's hot-reload picks the written files up.
 */
const here = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(here, '../../..');
const CONFIGS = process.env.CAPSCHAOS_CONFIGS ? path.resolve(process.env.CAPSCHAOS_CONFIGS) : path.join(REPO, 'Assets/CapsChaos/Content/Configs');
const LEVEL_DIR = path.join(CONFIGS, 'LevelConfig');
const CONVEYOR_DIR = path.join(CONFIGS, 'ConveyorConfig');
const INDEX_FILE = path.join(LEVEL_DIR, 'levels.index.json');

const LEVEL_ID = /^level_[0-9]{4}$/;
const CONVEYOR_ID = /^[a-z][a-z0-9_]{0,47}$/;

function readDir(dir: string, match: (id: string) => boolean) {
  if (!fs.existsSync(dir)) return [];
  return fs
    .readdirSync(dir)
    .filter((f) => f.endsWith('.json'))
    .map((f) => f.slice(0, -5))
    .filter(match)
    .sort()
    .map((id) => ({ id, text: fs.readFileSync(path.join(dir, id + '.json'), 'utf8') }));
}

function send(res: ServerResponse, status: number, body: unknown) {
  res.statusCode = status;
  res.setHeader('Content-Type', 'application/json');
  res.end(JSON.stringify(body));
}

function readBody(req: Connect.IncomingMessage): Promise<any> {
  return new Promise((resolve, reject) => {
    let data = '';
    req.on('data', (c) => (data += c));
    req.on('end', () => {
      try { resolve(data ? JSON.parse(data) : {}); } catch (e) { reject(e); }
    });
    req.on('error', reject);
  });
}

function writeText(file: string, text: string) {
  if (typeof text !== 'string') throw new Error('body.text must be a string');
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text, { encoding: 'utf8' }); // UTF-8, no BOM — as LevelJson.Write's callers write it
}

function removeWithMeta(file: string) {
  for (const f of [file, file + '.meta']) if (fs.existsSync(f)) fs.unlinkSync(f);
}

/** Runs `dotnet run --project Tools/LevelTool -- <args>`; only the read-only commands are allowed. */
function runLevelTool(args: string[]): Promise<{ code: number; stdout: string; stderr: string }> {
  const allowed = (args[0] === 'validate' && args.length === 1) || (args[0] === 'migrate' && args[1] === '--check' && args.length === 2);
  if (!allowed) return Promise.resolve({ code: 2, stdout: '', stderr: 'LevelDesigner only runs `validate` and `migrate --check`.' });
  return new Promise((resolve) => {
    const child = spawn('dotnet', ['run', '--project', 'Tools/LevelTool', '--', ...args], { cwd: REPO, env: process.env });
    let stdout = '', stderr = '';
    const timer = setTimeout(() => child.kill('SIGTERM'), 5 * 60_000);
    child.stdout.on('data', (d) => (stdout += d));
    child.stderr.on('data', (d) => (stderr += d));
    child.on('error', (e) => { clearTimeout(timer); resolve({ code: 2, stdout, stderr: stderr + String(e) }); });
    child.on('close', (code) => { clearTimeout(timer); resolve({ code: code ?? 2, stdout, stderr }); });
  });
}

function middleware(): Connect.NextHandleFunction {
  return async (req, res, next) => {
    const url = new URL(req.url ?? '/', 'http://x');
    if (!url.pathname.startsWith('/api/')) return next();
    const parts = url.pathname.split('/').filter(Boolean); // ['api', kind, id?]
    try {
      if (req.method === 'GET' && parts[1] === 'config') {
        return send(res, 200, {
          root: path.relative(REPO, CONFIGS),
          levels: readDir(LEVEL_DIR, (id) => LEVEL_ID.test(id)),
          conveyors: readDir(CONVEYOR_DIR, (id) => CONVEYOR_ID.test(id)),
          index: fs.existsSync(INDEX_FILE) ? fs.readFileSync(INDEX_FILE, 'utf8') : null,
        });
      }
      if (parts[1] === 'levels' && parts[2]) {
        if (!LEVEL_ID.test(parts[2])) return send(res, 400, { error: `'${parts[2]}' is not a level id (level_NNNN)` });
        const file = path.join(LEVEL_DIR, parts[2] + '.json');
        if (req.method === 'PUT') { writeText(file, (await readBody(req)).text); return send(res, 200, { ok: true }); }
        if (req.method === 'DELETE') { removeWithMeta(file); return send(res, 200, { ok: true }); }
      }
      if (parts[1] === 'conveyors' && parts[2]) {
        if (!CONVEYOR_ID.test(parts[2])) return send(res, 400, { error: `'${parts[2]}' is not a conveyor id` });
        const file = path.join(CONVEYOR_DIR, parts[2] + '.json');
        if (req.method === 'PUT') { writeText(file, (await readBody(req)).text); return send(res, 200, { ok: true }); }
        if (req.method === 'DELETE') { removeWithMeta(file); return send(res, 200, { ok: true }); }
      }
      if (parts[1] === 'index' && req.method === 'PUT') {
        writeText(INDEX_FILE, (await readBody(req)).text);
        return send(res, 200, { ok: true });
      }
      if (parts[1] === 'leveltool' && req.method === 'POST') {
        const body = await readBody(req);
        return send(res, 200, await runLevelTool(Array.isArray(body.args) ? body.args.map(String) : []));
      }
      return send(res, 404, { error: `no route ${req.method} ${url.pathname}` });
    } catch (e: any) {
      return send(res, 500, { error: e?.message ?? String(e) });
    }
  };
}

export function configApi(): Plugin {
  return {
    name: 'capschaos-config-api',
    configureServer(server) { server.middlewares.use(middleware()); },
    configurePreviewServer(server) { server.middlewares.use(middleware()); },
  };
}
