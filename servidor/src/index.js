// Coletor de runas do BR: lê partidas ranqueadas Solo/Duo de Esmeralda para cima na API da Riot e conta jogos e
// vitórias por página de runas (campeão + rota + patch). Também guarda, para cada jogador visitado, com quais
// campeões e runas ele joga (para achar os monos) e acompanha a lista de pros de pros.js. O AutoPick consulta GET /runas.
import PROS from './pros.js';

const PLATAFORMA = 'https://br1.api.riotgames.com';
const REGIAO = 'https://americas.api.riotgames.com'; // o BR fica na região "americas" da match-v5 e da account-v1
const FILA_SOLO = 420;
// Fontes de jogadores (Esmeralda+): as 3 ligas do topo e as divisões paginadas de Diamante e Esmeralda.
const LIGAS_TOPO = ['challengerleagues', 'grandmasterleagues', 'masterleagues'];
const DIVISOES = ['DIAMOND', 'EMERALD'].flatMap(t => ['I', 'II', 'III', 'IV'].map(d => [t, d]));
export const ROTAS = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'];
export const MONO_MIN_JOGOS = 8; // mono = 8+ jogos com o campeão e pelo menos metade das partidas dele
const ORDEM_ELO = "CASE p.tier WHEN 'CHALLENGER' THEN 8 WHEN 'GRANDMASTER' THEN 7 WHEN 'MASTER' THEN 6 WHEN 'DIAMOND' THEN 5 WHEN 'EMERALD' THEN 4 ELSE 0 END";

// O D1 grátis aceita ~100 mil linhas gravadas por dia: tabelas WITHOUT ROWID (sem índice extra),
// fila e cursor de jogadores num registro só em meta. Cada partida grava ~13 linhas.
const ESQUEMA = [
  'CREATE TABLE IF NOT EXISTS meta (chave TEXT PRIMARY KEY, valor TEXT) WITHOUT ROWID',
  'CREATE TABLE IF NOT EXISTS feitas (id TEXT PRIMARY KEY) WITHOUT ROWID',
  'CREATE TABLE IF NOT EXISTS runas (patch TEXT, campeao INTEGER, rota TEXT, pagina TEXT, jogos INTEGER NOT NULL, vitorias INTEGER NOT NULL, PRIMARY KEY (patch, campeao, rota, pagina)) WITHOUT ROWID',
  'CREATE TABLE IF NOT EXISTS perfis (puuid TEXT PRIMARY KEY, nome TEXT, tier TEXT, pdl INTEGER, pro TEXT, jogos INTEGER NOT NULL DEFAULT 0, visto INTEGER NOT NULL DEFAULT 0) WITHOUT ROWID',
  'CREATE TABLE IF NOT EXISTS jogador_campeao (campeao INTEGER, puuid TEXT, pagina TEXT, jogos INTEGER NOT NULL, vitorias INTEGER NOT NULL, PRIMARY KEY (campeao, puuid, pagina)) WITHOUT ROWID',
  // tabelas da versão anterior (gravavam demais)
  'DROP TABLE IF EXISTS stats', 'DROP TABLE IF EXISTS jogadores', 'DROP TABLE IF EXISTS fila',
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
const decodificar = pagina => { const [primaria, secundaria, ids] = pagina.split('|'); return { primaria: +primaria, secundaria: +secundaria, perks: ids.split(',').map(Number) }; };

// Partida → uma linha por jogador. Ignora outras filas e remakes.
export function linhasDaPartida(m) {
  const info = m?.info;
  if (!info || info.queueId !== FILA_SOLO || info.gameDuration < 300) return [];
  const patch = String(info.gameVersion || '').split('.').slice(0, 2).join('.');
  return (info.participants || [])
    .map(p => ({
      patch, campeao: p.championId, rota: p.teamPosition, pagina: paginaDe(p), vitoria: p.win ? 1 : 0, puuid: p.puuid,
      nome: p.riotIdGameName ? `${p.riotIdGameName}#${p.riotIdTagline || ''}` : null,
    }))
    .filter(l => l.pagina && ROTAS.includes(l.rota) && /^\d+\.\d+$/.test(l.patch));
}

const LIMITE = 'limite';
const fatal = e => e.message === LIMITE || e.status === 401 || e.status === 403;
async function riot(env, url) {
  const r = await fetch(url, { headers: { 'X-Riot-Token': env.RIOT_KEY } });
  if (r.status === 429) throw new Error(LIMITE); // estourou o limite da chave: para e tenta no próximo minuto
  if (!r.ok) { const e = new Error('Riot respondeu ' + r.status); e.status = r.status; throw e; }
  return r.json();
}

// Pros de pros.js: acha a conta de um por rodada (account-v1) e marca em perfis.
async function resolverPro(env, db, meta, pros) {
  const feitos = meta.pros_resolvidos || [];
  const pro = pros.find(p => !feitos.includes(p.riotId));
  if (!pro) return;
  const [nome, tag = ''] = pro.riotId.split('#');
  try {
    const a = await riot(env, `${REGIAO}/riot/account/v1/accounts/by-riot-id/${encodeURIComponent(nome)}/${encodeURIComponent(tag)}`);
    meta.escritas++;
    await db.prepare('INSERT INTO perfis (puuid, nome, pro) VALUES (?, ?, ?) ON CONFLICT (puuid) DO UPDATE SET pro = excluded.pro, nome = excluded.nome')
      .bind(a.puuid, pro.riotId, pro.time || 'Pro').run();
  } catch (e) { if (fatal(e)) throw e; } // conta não existe: pula
  meta.pros_resolvidos = [...feitos, pro.riotId];
}

// Próximo jogador a visitar: a cada 4 rodadas um pro (se algum não foi visto em 12 h); senão o próximo da fonte atual
// (ligas do topo, depois Diamante e Esmeralda página a página, em ciclo). A fonte é buscada de novo a cada visita
// (uma visita a cada ~5 rodadas) para não guardar listas de milhares de jogadores.
async function proximoJogador(env, db, meta) {
  const c = meta.cursor = meta.cursor || { i: 0, pagina: 1, pos: 0, rodada: 0 };
  if (++c.rodada % 4 === 0) {
    const pro = await db.prepare('SELECT puuid FROM perfis WHERE pro IS NOT NULL AND visto < ? ORDER BY visto LIMIT 1').bind(Date.now() - 432e5).first('puuid');
    if (pro) return { puuid: pro };
  }
  let entradas = [], tier;
  try {
    if (c.i < LIGAS_TOPO.length) {
      const l = await riot(env, `${PLATAFORMA}/lol/league/v4/${LIGAS_TOPO[c.i]}/by-queue/RANKED_SOLO_5x5`);
      entradas = l.entries || []; tier = l.tier;
    } else {
      const [t, d] = DIVISOES[c.i - LIGAS_TOPO.length];
      entradas = await riot(env, `${PLATAFORMA}/lol/league/v4/entries/RANKED_SOLO_5x5/${t}/${d}?page=${c.pagina}`);
    }
  } catch (e) { if (fatal(e)) throw e; } // fonte com erro (404, 500…): pula para a próxima em vez de travar aqui
  // shortcut: usa só o puuid que a league-v4 já devolve; se a Riot tirar o campo, precisa buscar pela summoner-v4
  const lista = entradas.filter(e => e.puuid).sort((a, b) => (b.leaguePoints || 0) - (a.leaguePoints || 0));
  const e = lista[c.pos++];
  if (c.pos >= lista.length) { // acabou a fonte: próxima página, próxima divisão, ou recomeça do Desafiante
    c.pos = 0;
    if (c.i >= LIGAS_TOPO.length && lista.length) c.pagina++;
    else { c.i = (c.i + 1) % (LIGAS_TOPO.length + DIVISOES.length); c.pagina = 1; }
  }
  return e ? { puuid: e.puuid, tier: e.tier || tier || null, pdl: e.leaguePoints ?? null } : null;
}

// Uma rodada (cron a cada minuto): até 2 pedidos para achar jogadores + PARTIDAS_POR_RODADA partidas.
export async function coletar(env, pros = PROS) {
  const db = env.DB, max = Number(env.PARTIDAS_POR_RODADA || 4);
  await criarEsquema(db);
  const linhaMeta = await db.prepare("SELECT valor FROM meta WHERE chave = 'estado'").first('valor');
  const meta = linhaMeta ? JSON.parse(linhaMeta) : {};
  // cada gravação soma em meta.escritas (o REPLACE do próprio meta conta como 2: apaga e insere)
  const gravarMeta = () => { meta.escritas += 2; return db.prepare("INSERT OR REPLACE INTO meta (chave, valor) VALUES ('estado', ?)").bind(JSON.stringify(meta)); };
  meta.fila = meta.fila || [];
  // Trava do limite diário do D1 (zera à meia-noite UTC, como o da Cloudflare): ao chegar nele, pausa sem gravar nada.
  const hoje = new Date().toISOString().slice(0, 10);
  if (meta.dia !== hoje) { meta.dia = hoje; meta.escritas = 0; }
  if (meta.escritas >= Number(env.LIMITE_ESCRITAS_DIA || 90000)) return;
  try {
    await resolverPro(env, db, meta, pros);
    if (meta.fila.length < max) {
      const j = await proximoJogador(env, db, meta);
      if (j) {
        const ids = await riot(env, `${REGIAO}/lol/match/v5/matches/by-puuid/${encodeURIComponent(j.puuid)}/ids?queue=${FILA_SOLO}&type=ranked&count=20`);
        const { results: vistas } = ids.length
          ? await db.prepare(`SELECT id FROM feitas WHERE id IN (${ids.map(() => '?').join(',')})`).bind(...ids).all() : { results: [] };
        const ja = new Set(vistas.map(r => r.id).concat(meta.fila.map(f => f.id)));
        meta.fila.push(...ids.filter(id => !ja.has(id)).map(id => ({ id, p: j.puuid })));
        meta.escritas++;
        await db.batch([
          db.prepare('INSERT INTO perfis (puuid, tier, pdl, visto) VALUES (?, ?, ?, ?) ON CONFLICT (puuid) DO UPDATE SET tier = COALESCE(excluded.tier, tier), pdl = COALESCE(excluded.pdl, pdl), visto = excluded.visto')
            .bind(j.puuid, j.tier ?? null, j.pdl ?? null, Date.now()),
          gravarMeta(),
        ]);
      }
    }
    for (const item of meta.fila.slice(0, max)) {
      let linhas = [];
      try { linhas = linhasDaPartida(await riot(env, `${REGIAO}/lol/match/v5/matches/${encodeURIComponent(item.id)}`)); }
      catch (e) { if (fatal(e)) throw e; } // 404 e afins: descarta a partida
      meta.fila = meta.fila.filter(f => f !== item);
      const ops = [];
      if (!(await db.prepare('SELECT 1 AS x FROM feitas WHERE id = ?').bind(item.id).first('x'))) {
        ops.push(db.prepare('INSERT INTO feitas (id) VALUES (?)').bind(item.id), ...linhas.map(l => db.prepare(
          'INSERT INTO runas (patch, campeao, rota, pagina, jogos, vitorias) VALUES (?, ?, ?, ?, 1, ?) ' +
          'ON CONFLICT (patch, campeao, rota, pagina) DO UPDATE SET jogos = jogos + 1, vitorias = vitorias + excluded.vitorias')
          .bind(l.patch, l.campeao, l.rota, l.pagina, l.vitoria)));
        const dele = linhas.find(l => l.puuid === item.p);
        if (dele) ops.push(
          db.prepare('INSERT INTO jogador_campeao (campeao, puuid, pagina, jogos, vitorias) VALUES (?, ?, ?, 1, ?) ' +
            'ON CONFLICT (campeao, puuid, pagina) DO UPDATE SET jogos = jogos + 1, vitorias = vitorias + excluded.vitorias')
            .bind(dele.campeao, item.p, dele.pagina, dele.vitoria),
          db.prepare('UPDATE perfis SET jogos = jogos + 1, nome = COALESCE(nome, ?) WHERE puuid = ?').bind(dele.nome, item.p));
      }
      meta.escritas += ops.length;
      ops.push(gravarMeta());
      await db.batch(ops);
    }
    meta.erro = null;
  } catch (e) {
    if (e.message !== LIMITE) meta.erro = e.message + (e.status === 401 || e.status === 403 ? ' (chave da Riot inválida ou vencida)' : '');
    await gravarMeta().run();
    if (e.message !== LIMITE) throw e;
    return;
  }
  await gravarMeta().run();
}

const json = (dados, status = 200, cache = 0) => new Response(JSON.stringify(dados), {
  status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': cache ? `public, max-age=${cache}` : 'no-store' } });

// Os dois patches mais novos que têm dados (no começo de um patch quase não há partidas).
async function patchesRecentes(db) {
  const { results } = await db.prepare('SELECT DISTINCT patch FROM runas').all();
  const num = p => p.split('.').map(Number);
  return results.map(r => r.patch).sort((a, b) => { const [x, y] = [num(a), num(b)]; return y[0] - x[0] || y[1] - x[1]; }).slice(0, 2);
}

// Jogadores (monos ou pros) do campeão, cada um com a página que ele mais usa.
async function jogadores(db, campeao, sql, ...args) {
  const { results } = await db.prepare(sql).bind(campeao, ...args).all();
  return Promise.all(results.map(async r => {
    const fav = await db.prepare('SELECT pagina FROM jogador_campeao WHERE campeao = ? AND puuid = ? ORDER BY jogos DESC LIMIT 1').bind(campeao, r.puuid).first('pagina');
    return { nome: r.nome, tier: r.tier, pdl: r.pdl, pro: r.pro, jogos: r.jogos, vitorias: r.vitorias, total: r.total, pagina: fav ? decodificar(fav) : null };
  }));
}
const BASE_JOGADOR = 'SELECT c.puuid, SUM(c.jogos) AS jogos, SUM(c.vitorias) AS vitorias, p.nome, p.tier, p.pdl, p.pro, p.jogos AS total ' +
  'FROM jogador_campeao c JOIN perfis p ON p.puuid = c.puuid WHERE c.campeao = ? GROUP BY c.puuid ';

export async function consultar(db, campeao, rota) {
  const monos = await jogadores(db, campeao, BASE_JOGADOR + `HAVING SUM(c.jogos) >= ? AND SUM(c.jogos) * 2 >= p.jogos ORDER BY ${ORDEM_ELO} DESC, p.pdl DESC LIMIT 5`, MONO_MIN_JOGOS);
  const pros = await jogadores(db, campeao, BASE_JOGADOR + 'HAVING p.pro IS NOT NULL ORDER BY SUM(c.jogos) DESC LIMIT 5');
  const patches = await patchesRecentes(db);
  if (!patches.length) return { patches, total: 0, paginas: [], monos, pros };
  const filtro = `campeao = ? AND patch IN (${patches.map(() => '?').join(',')})` + (rota ? ' AND rota = ?' : '');
  const args = [campeao, ...patches, ...(rota ? [rota] : [])];
  const total = await db.prepare(`SELECT COALESCE(SUM(jogos), 0) AS n FROM runas WHERE ${filtro}`).bind(...args).first('n');
  const { results } = await db.prepare(
    `SELECT pagina, SUM(jogos) AS jogos, SUM(vitorias) AS vitorias FROM runas WHERE ${filtro} GROUP BY pagina HAVING SUM(jogos) >= 3 ORDER BY jogos DESC LIMIT 5`)
    .bind(...args).all();
  return { patches, total, paginas: results.map(r => ({ ...decodificar(r.pagina), jogos: r.jogos, vitorias: r.vitorias })), monos, pros };
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
      const v = await db.prepare("SELECT valor FROM meta WHERE chave = 'estado'").first('valor');
      const meta = v ? JSON.parse(v) : {}, c = meta.cursor;
      return json({
        partidas_lidas: await um('SELECT COUNT(*) AS n FROM feitas'), na_fila: (meta.fila || []).length,
        jogadores_vistos: await um('SELECT COUNT(*) AS n FROM perfis WHERE visto > 0'),
        pros: `${await um('SELECT COUNT(*) AS n FROM perfis WHERE pro IS NOT NULL')} de ${PROS.length}`,
        patches: await patchesRecentes(db),
        gravadas_hoje: meta.dia === new Date().toISOString().slice(0, 10) ? meta.escritas || 0 : 0,
        limite_diario: Number(env.LIMITE_ESCRITAS_DIA || 90000),
        pausado_ate_00h_utc: meta.dia === new Date().toISOString().slice(0, 10) && meta.escritas >= Number(env.LIMITE_ESCRITAS_DIA || 90000),
        buscando_em: !c ? null : c.i < LIGAS_TOPO.length ? ['Desafiante', 'Grão-Mestre', 'Mestre'][c.i] : DIVISOES[c.i - LIGAS_TOPO.length].join(' ') + ' página ' + c.pagina,
        erro: meta.erro || null,
      });
    }
    return json({ erro: 'não encontrado' }, 404);
  },
  async scheduled(_evento, env, ctx) { ctx.waitUntil(coletar(env)); },
};
