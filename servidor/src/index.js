// Coletor de runas do BR: lê partidas ranqueadas Solo/Duo de Desafiante e Grão-Mestre na API da Riot
// e conta jogos e vitórias por página de runas (campeão + rota + patch). O AutoPick consulta GET /runas.
const PLATAFORMA = 'https://br1.api.riotgames.com';
const REGIAO = 'https://americas.api.riotgames.com'; // o BR fica na região "americas" da match-v5
const FILA_SOLO = 420;
export const ROTAS = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'];

const ESQUEMA = [
  'CREATE TABLE IF NOT EXISTS jogadores (puuid TEXT PRIMARY KEY, visto INTEGER NOT NULL DEFAULT 0)',
  'CREATE TABLE IF NOT EXISTS fila (id TEXT PRIMARY KEY)',
  'CREATE TABLE IF NOT EXISTS feitas (id TEXT PRIMARY KEY)',
  'CREATE TABLE IF NOT EXISTS stats (patch TEXT, campeao INTEGER, rota TEXT, pagina TEXT, jogos INTEGER NOT NULL, vitorias INTEGER NOT NULL, PRIMARY KEY (patch, campeao, rota, pagina))',
  'CREATE TABLE IF NOT EXISTS meta (chave TEXT PRIMARY KEY, valor TEXT)',
];
const criarEsquema = db => db.batch(ESQUEMA.map(q => db.prepare(q)));

// Participante da match-v5 → "primária|secundária|9 ids" (4 principais, 2 secundárias, 3 fragmentos), ou null.
export function paginaDe(p) {
  const estilos = p.perks?.styles || [], f = p.perks?.statPerks;
  const pri = estilos.find(s => s.description === 'primaryStyle'), sec = estilos.find(s => s.description === 'subStyle');
  if (!pri || !sec || !f || pri.selections?.length !== 4 || sec.selections?.length !== 2) return null;
  const ids = [...pri.selections, ...sec.selections].map(s => s.perk).concat([f.offense, f.flex, f.defense]);
  if (ids.some(x => !Number.isInteger(x) || x <= 0)) return null;
  return `${pri.style}|${sec.style}|${ids.join(',')}`;
}

// Partida → uma linha por jogador. Ignora outras filas e remakes.
export function linhasDaPartida(m) {
  const info = m?.info;
  if (!info || info.queueId !== FILA_SOLO || info.gameDuration < 300) return [];
  const patch = String(info.gameVersion || '').split('.').slice(0, 2).join('.');
  return (info.participants || [])
    .map(p => ({ patch, campeao: p.championId, rota: p.teamPosition, pagina: paginaDe(p), vitoria: p.win ? 1 : 0 }))
    .filter(l => l.pagina && ROTAS.includes(l.rota) && /^\d+\.\d+$/.test(l.patch));
}

const LIMITE = 'limite';
async function riot(env, url) {
  const r = await fetch(url, { headers: { 'X-Riot-Token': env.RIOT_KEY } });
  if (r.status === 429) throw new Error(LIMITE); // estourou o limite da chave: para e tenta no próximo minuto
  if (!r.ok) { const e = new Error('Riot respondeu ' + r.status); e.status = r.status; throw e; }
  return r.json();
}
const pedacos = (a, n) => Array.from({ length: Math.ceil(a.length / n) }, (_, i) => a.slice(i * n, i * n + n));
const guardar = (db, chave, valor) => db.prepare('INSERT OR REPLACE INTO meta VALUES (?, ?)').bind(chave, String(valor)).run();

// Uma rodada (cron a cada minuto). Poucos pedidos por rodada para caber no limite da chave pessoal (100 a cada 2 min).
export async function coletar(env) {
  const db = env.DB, max = Number(env.PARTIDAS_POR_RODADA || 5);
  await criarEsquema(db);
  try {
    // 1. Jogadores: Desafiante + Grão-Mestre do BR, renovado uma vez por dia.
    const semente = Number(await db.prepare("SELECT valor FROM meta WHERE chave = 'semente'").first('valor') || 0);
    if (Date.now() - semente > 864e5) {
      let total = 0;
      for (const liga of ['challengerleagues', 'grandmasterleagues']) {
        const l = await riot(env, `${PLATAFORMA}/lol/league/v4/${liga}/by-queue/RANKED_SOLO_5x5`);
        // shortcut: usa só o puuid que a league-v4 já devolve; se a Riot tirar o campo, precisa buscar pela summoner-v4
        const puuids = (l.entries || []).map(e => e.puuid).filter(Boolean);
        total += puuids.length;
        for (const lote of pedacos(puuids, 50)) await db.batch(lote.map(p => db.prepare('INSERT OR IGNORE INTO jogadores (puuid) VALUES (?)').bind(p)));
      }
      await guardar(db, 'semente', Date.now());
      await guardar(db, 'jogadores_semente', total);
    }
    // 2. Fila acabando: pega as partidas recentes do jogador visto há mais tempo.
    if (await db.prepare('SELECT COUNT(*) AS n FROM fila').first('n') < max * 2) {
      const j = await db.prepare('SELECT puuid FROM jogadores ORDER BY visto LIMIT 1').first('puuid');
      if (j) {
        const ids = await riot(env, `${REGIAO}/lol/match/v5/matches/by-puuid/${encodeURIComponent(j)}/ids?queue=${FILA_SOLO}&type=ranked&count=20`);
        await db.batch([
          db.prepare('UPDATE jogadores SET visto = ? WHERE puuid = ?').bind(Date.now(), j),
          ...ids.map(id => db.prepare('INSERT OR IGNORE INTO fila (id) SELECT ?1 WHERE NOT EXISTS (SELECT 1 FROM feitas WHERE id = ?1)').bind(id)),
        ]);
      }
    }
    // 3. Lê algumas partidas da fila e soma nas estatísticas.
    const { results } = await db.prepare('SELECT id FROM fila LIMIT ?').bind(max).all();
    for (const { id } of results) {
      let linhas = [];
      try { linhas = linhasDaPartida(await riot(env, `${REGIAO}/lol/match/v5/matches/${encodeURIComponent(id)}`)); }
      catch (e) { if (e.message === LIMITE || e.status === 401 || e.status === 403) throw e; } // 404 e afins: descarta a partida
      await db.batch([
        ...linhas.map(l => db.prepare(
          'INSERT INTO stats (patch, campeao, rota, pagina, jogos, vitorias) VALUES (?, ?, ?, ?, 1, ?) ' +
          'ON CONFLICT (patch, campeao, rota, pagina) DO UPDATE SET jogos = jogos + 1, vitorias = vitorias + excluded.vitorias')
          .bind(l.patch, l.campeao, l.rota, l.pagina, l.vitoria)),
        db.prepare('INSERT OR IGNORE INTO feitas (id) VALUES (?)').bind(id),
        db.prepare('DELETE FROM fila WHERE id = ?').bind(id),
      ]);
    }
    await guardar(db, 'erro', '');
  } catch (e) {
    if (e.message !== LIMITE) { await guardar(db, 'erro', e.message + (e.status === 401 || e.status === 403 ? ' (chave da Riot inválida ou vencida)' : '')); throw e; }
  }
}

const json = (dados, status = 200, cache = 0) => new Response(JSON.stringify(dados), {
  status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': cache ? `public, max-age=${cache}` : 'no-store' } });

// Os dois patches mais novos que têm dados (no começo de um patch quase não há partidas).
async function patchesRecentes(db) {
  const { results } = await db.prepare('SELECT DISTINCT patch FROM stats').all();
  const num = p => p.split('.').map(Number);
  return results.map(r => r.patch).sort((a, b) => { const [x, y] = [num(a), num(b)]; return y[0] - x[0] || y[1] - x[1]; }).slice(0, 2);
}

export async function consultar(db, campeao, rota) {
  const patches = await patchesRecentes(db);
  if (!patches.length) return { patches, total: 0, paginas: [] };
  const filtro = `campeao = ? AND patch IN (${patches.map(() => '?').join(',')})` + (rota ? ' AND rota = ?' : '');
  const args = [campeao, ...patches, ...(rota ? [rota] : [])];
  const total = await db.prepare(`SELECT COALESCE(SUM(jogos), 0) AS n FROM stats WHERE ${filtro}`).bind(...args).first('n');
  const { results } = await db.prepare(
    `SELECT pagina, SUM(jogos) AS jogos, SUM(vitorias) AS vitorias FROM stats WHERE ${filtro} GROUP BY pagina HAVING SUM(jogos) >= 3 ORDER BY jogos DESC LIMIT 3`)
    .bind(...args).all();
  return {
    patches, total,
    paginas: results.map(r => { const [primaria, secundaria, ids] = r.pagina.split('|'); return { primaria: +primaria, secundaria: +secundaria, perks: ids.split(',').map(Number), jogos: r.jogos, vitorias: r.vitorias }; }),
  };
}

export default {
  async fetch(req, env) {
    const u = new URL(req.url), db = env.DB;
    await criarEsquema(db);
    if (u.pathname === '/runas') {
      const campeao = Number(u.searchParams.get('campeao')), rota = (u.searchParams.get('rota') || '').toUpperCase();
      if (!Number.isInteger(campeao) || campeao <= 0 || (rota && !ROTAS.includes(rota))) return json({ erro: 'use ?campeao=<id numérico>&rota=TOP|JUNGLE|MIDDLE|BOTTOM|UTILITY' }, 400);
      return json(await consultar(db, campeao, rota), 200, 600);
    }
    if (u.pathname === '/') {
      const um = q => db.prepare(q).first('n');
      const meta = Object.fromEntries((await db.prepare('SELECT chave, valor FROM meta').all()).results.map(r => [r.chave, r.valor]));
      return json({
        partidas_lidas: await um('SELECT COUNT(*) AS n FROM feitas'), na_fila: await um('SELECT COUNT(*) AS n FROM fila'),
        jogadores: await um('SELECT COUNT(*) AS n FROM jogadores'), patches: await patchesRecentes(db),
        ultima_semente: meta.semente ? new Date(Number(meta.semente)).toISOString() : null, erro: meta.erro || null,
      });
    }
    return json({ erro: 'não encontrado' }, 404);
  },
  async scheduled(_evento, env, ctx) { ctx.waitUntil(coletar(env)); },
};
