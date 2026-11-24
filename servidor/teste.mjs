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

const jogador = (puuid, campeao, rota, win, keystone = 8112) => ({
  puuid, riotIdGameName: puuid.toUpperCase(), riotIdTagline: 'BR1', championId: campeao, teamPosition: rota, win,
  perks: { statPerks: { offense: 5008, flex: 5008, defense: 5011 },
    styles: [{ description: 'primaryStyle', style: 8100, selections: [keystone, 8126, 8138, 8135].map(perk => ({ perk })) },
             { description: 'subStyle', style: 8200, selections: [8226, 8210].map(perk => ({ perk })) }] } });
// p2 (o de mais PDL do Desafiante) joga sempre de Ahri (103): vira mono. O pro "pp" joga de Jinx (222).
const partida = (id, vence) => ({ info: { queueId: 420, gameDuration: 1800, gameVersion: '16.23.123.4567',
  participants: [jogador('p2', 103, 'MIDDLE', vence), jogador('x' + id, 103, 'MIDDLE', !vence, 8128), jogador('pp', 222, 'BOTTOM', true), { championId: 1, teamPosition: '', perks: {} }] } });

// funções puras
assert.strictEqual(paginaDe(jogador('a', 103, 'MIDDLE', true)), '8100|8200|8112,8126,8138,8135,8226,8210,5008,5008,5011');
assert.strictEqual(paginaDe({ perks: {} }), null);
assert.strictEqual(linhasDaPartida(partida('X', true)).length, 3, 'ignora jogador sem rota/runas');
assert.strictEqual(linhasDaPartida(partida('X', true))[0].nome, 'P2#BR1');
assert.strictEqual(linhasDaPartida({ info: { ...partida('X', true).info, queueId: 450 } }).length, 0, 'só Solo/Duo');
assert.strictEqual(linhasDaPartida({ info: { ...partida('X', true).info, gameDuration: 200 } }).length, 0, 'ignora remake');

// Riot falsa
const pedidos = [];
let respostas;
globalThis.fetch = async url => {
  pedidos.push(url);
  for (const [trecho, corpo] of respostas) if (url.includes(trecho)) return typeof corpo === 'number' ? new Response('', { status: corpo }) : Response.json(corpo);
  return new Response('', { status: 404 });
};
const ids = Array.from({ length: 10 }, (_, i) => 'BR1_' + i);
respostas = [
  ['/accounts/by-riot-id/Pro%20Player/BR1', { puuid: 'pp' }],
  ['challengerleagues', { tier: 'CHALLENGER', entries: [{ puuid: 'p1', leaguePoints: 900 }, { puuid: 'p2', leaguePoints: 1200 }] }],
  ['/by-puuid/pp/', ['BR1_P1', 'BR1_P2']],
  ['/by-puuid/', ids],
  ...ids.map((id, i) => [`/matches/${id}`, partida(id, i < 7)]),
  ['/matches/BR1_P', partida('P', true)],
];
const env = { DB: d1(), RIOT_KEY: 'chave-falsa', PARTIDAS_POR_RODADA: '4' };
const PROS = [{ riotId: 'Pro Player#BR1', time: 'LOUD' }];
// rodadas 1-2: mono (p2, mais PDL); 3: p1 (nada novo); 4: Grão-Mestre dá 404 e é pulado; 5: vez do pro
for (let i = 0; i < 5; i++) await coletar(env, PROS);
assert.ok(pedidos.every(u => u.startsWith('https://br1.api.riotgames.com') || u.startsWith('https://americas.api.riotgames.com')));
assert.ok(pedidos.find(u => u.includes('/by-puuid/')).includes('/by-puuid/p2/'), 'visita primeiro quem tem mais PDL');
assert.strictEqual(await env.DB.prepare('SELECT COUNT(*) AS n FROM feitas').first('n'), 12, '10 do mono + 2 do pro (5ª rodada)');

let r = await consultar(env.DB, 103, 'MIDDLE');
assert.deepStrictEqual(r.patches, ['16.23']);
assert.strictEqual(r.total, 24, '12 partidas x 2 Ahris');
assert.strictEqual(r.paginas[0].perks.length, 9);
assert.strictEqual(r.monos.length, 1);
assert.deepStrictEqual([r.monos[0].nome, r.monos[0].tier, r.monos[0].pdl, r.monos[0].jogos, r.monos[0].vitorias], ['P2#BR1', 'CHALLENGER', 1200, 10, 7]);
assert.strictEqual(r.monos[0].pagina.perks[0], 8112);
r = await consultar(env.DB, 222, 'BOTTOM');
assert.deepStrictEqual([r.pros.length, r.pros[0].nome, r.pros[0].pro, r.pros[0].jogos], [1, 'Pro Player#BR1', 'LOUD', 2]);
assert.strictEqual(r.monos.length, 0, 'pro com 2 jogos não é mono');
assert.strictEqual((await consultar(env.DB, 1, '')).monos.length, 0);

// não relê partida já feita; para sem erro no limite (429) e guarda a fila
pedidos.length = 0;
respostas = [['masterleagues', { tier: 'MASTER', entries: [{ puuid: 'p3', leaguePoints: 300 }] }], ['/by-puuid/', ['BR1_0', 'BR1_NOVA']], ['/matches/BR1_NOVA', 429]];
await coletar(env, PROS);
assert.ok(!pedidos.some(u => u.endsWith('/matches/BR1_0')), 'não repete partida');
const st0 = await (await worker.fetch(new Request('https://x/'), env)).json();
assert.strictEqual(st0.na_fila, 1, 'BR1_NOVA fica para depois');
assert.strictEqual(st0.erro, null);

// chave inválida fica registrada
respostas = [['/matches/', 403]];
await assert.rejects(coletar(env, PROS));
const st = await (await worker.fetch(new Request('https://x/'), env)).json();
assert.match(st.erro, /chave/);

// endpoint
const resp = await worker.fetch(new Request('https://x/runas?campeao=103&rota=middle'), env);
assert.strictEqual(resp.status, 200);
const corpo = await resp.json();
assert.strictEqual(corpo.total, 24);
assert.strictEqual(corpo.monos[0].nome, 'P2#BR1');
assert.strictEqual((await worker.fetch(new Request('https://x/runas?campeao=abc'), env)).status, 400);
assert.strictEqual((await worker.fetch(new Request('https://x/runas?campeao=103&rota=XX'), env)).status, 400);
console.log('ok');
