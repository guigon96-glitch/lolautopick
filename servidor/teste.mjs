// Teste: D1 simulado sobre o SQLite do Node e API da Riot falsa. Rode: node teste.mjs
import assert from 'node:assert';
import { DatabaseSync } from 'node:sqlite';
import worker, { coletar, consultar, paginaDe, linhasDaPartida } from './src/index.js';

function d1() {
  const s = new DatabaseSync(':memory:');
  const preparado = (q, args = []) => ({
    bind: (...a) => preparado(q, a),
    first: async col => { const r = s.prepare(q).get(...args); return r ? (col ? r[col] : r) : null; },
    all: async () => ({ results: s.prepare(q).all(...args) }),
    run: async () => s.prepare(q).run(...args),
    exec: () => s.prepare(q).run(...args),
  });
  return { prepare: q => preparado(q), batch: async l => { s.exec('BEGIN'); try { l.forEach(p => p.exec()); s.exec('COMMIT'); } catch (e) { s.exec('ROLLBACK'); throw e; } } };
}

const jogador = (campeao, rota, win, keystone = 8112) => ({
  championId: campeao, teamPosition: rota, win,
  perks: { statPerks: { offense: 5008, flex: 5008, defense: 5011 },
    styles: [{ description: 'primaryStyle', style: 8100, selections: [keystone, 8126, 8138, 8135].map(perk => ({ perk })) },
             { description: 'subStyle', style: 8200, selections: [8226, 8210].map(perk => ({ perk })) }] } });
const partida = (id, vence) => ({ metadata: { matchId: id }, info: { queueId: 420, gameDuration: 1800, gameVersion: '15.23.123.4567',
  participants: [jogador(103, 'MIDDLE', vence), jogador(103, 'MIDDLE', !vence, 8128), jogador(222, 'BOTTOM', true), { championId: 1, teamPosition: '', perks: {} }] } });

// funções puras
assert.strictEqual(paginaDe(jogador(103, 'MIDDLE', true)), '8100|8200|8112,8126,8138,8135,8226,8210,5008,5008,5011');
assert.strictEqual(paginaDe({ perks: {} }), null);
assert.strictEqual(linhasDaPartida(partida('X', true)).length, 3, 'ignora jogador sem rota/runas');
assert.strictEqual(linhasDaPartida({ info: { ...partida('X', true).info, queueId: 450 } }).length, 0, 'só Solo/Duo');
assert.strictEqual(linhasDaPartida({ info: { ...partida('X', true).info, gameDuration: 200 } }).length, 0, 'ignora remake');

// coleta com Riot falsa
const pedidos = [];
let respostas;
globalThis.fetch = async url => {
  pedidos.push(url);
  for (const [trecho, corpo] of respostas) if (url.includes(trecho)) return typeof corpo === 'number' ? new Response('', { status: corpo }) : Response.json(corpo);
  return new Response('', { status: 404 });
};
respostas = [
  ['challengerleagues', { entries: [{ puuid: 'p1' }, { puuid: 'p2' }] }],
  ['grandmasterleagues', { entries: [{ puuid: 'p3' }, {}] }],
  ['/by-puuid/', ['BR1_1', 'BR1_2', 'BR1_3']],
  ['/matches/BR1_1', partida('BR1_1', true)],
  ['/matches/BR1_2', partida('BR1_2', true)],
  ['/matches/BR1_3', partida('BR1_3', false)],
];
const env = { DB: d1(), RIOT_KEY: 'chave-falsa', PARTIDAS_POR_RODADA: '5' };
await coletar(env);
assert.ok(pedidos.every(u => u.startsWith('https://br1.api.riotgames.com') || u.startsWith('https://americas.api.riotgames.com')));
assert.strictEqual(await env.DB.prepare('SELECT COUNT(*) AS n FROM jogadores').first('n'), 3);
assert.strictEqual(await env.DB.prepare('SELECT COUNT(*) AS n FROM feitas').first('n'), 3);

const r = await consultar(env.DB, 103, 'MIDDLE');
assert.deepStrictEqual(r.patches, ['15.23']);
assert.strictEqual(r.total, 6);
assert.strictEqual(r.paginas[0].jogos, 3);
assert.deepStrictEqual(r.paginas.map(p => p.vitorias).sort(), [1, 2], 'conta vitórias por página');
assert.strictEqual(r.paginas[0].perks.length, 9);

// segunda rodada: não relê partidas já feitas, e para sem erro no limite (429)
pedidos.length = 0;
respostas = [['/by-puuid/', ['BR1_1', 'BR1_4']], ['/matches/BR1_4', 429]];
await coletar(env);
assert.ok(!pedidos.some(u => u.endsWith('/matches/BR1_1')), 'não repete partida');
assert.strictEqual(await env.DB.prepare('SELECT COUNT(*) AS n FROM fila').first('n'), 1, 'BR1_4 fica para depois');

// chave inválida fica registrada
respostas = [['/by-puuid/', 403], ['/matches/', 403]];
await assert.rejects(coletar(env));
const st = await (await worker.fetch(new Request('https://x/'), env)).json();
assert.match(st.erro, /chave/);

// endpoint
const resp = await worker.fetch(new Request('https://x/runas?campeao=103&rota=middle'), env);
assert.strictEqual(resp.status, 200);
assert.strictEqual((await resp.json()).total, 6);
assert.strictEqual((await worker.fetch(new Request('https://x/runas?campeao=abc'), env)).status, 400);
assert.strictEqual((await worker.fetch(new Request('https://x/runas?campeao=103&rota=XX'), env)).status, 400);
console.log('ok');
