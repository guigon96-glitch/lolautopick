# Cliente do LoL falso (API local LCU) para testar o AutoPick: simula uma seleção em escolha alternada.
# Uso: openssl req -x509 -newkey rsa:2048 -nodes -keyout k.pem -out c.pem -subj "/CN=127.0.0.1"; python3 app/lcu_falso.py c.pem k.pem 45
#      com um lockfile "LeagueClient:1:50123:senha:https" na pastaLol do config.json, e o AutoPick aberto.
# Planejamento (0-6 s) -> bans de todos ao mesmo tempo (6-26 s) -> pick do aliado 0 (26-30 s) -> pick do jogador (30 s em diante).
import json, ssl, sys, time, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

EU = 2
CAMPEOES = {28: 'Evelynn', 131: 'Diana', 102: 'Shyvana', 99: 'Lux', 238: 'Zed', 157: 'Yasuo', 11: 'Master Yi', 64: 'Lee Sin'}
ALIAS = {11: 'MasterYi', 64: 'LeeSin'}
inicio = time.time()
log = []
acoes = []
nid = 0
for cel in range(10):
    nid += 1; acoes.append({'id': nid, 'actorCellId': cel, 'type': 'ban', 'championId': 0, 'completed': False, 'isInProgress': False, 'isAllyAction': cel < 5})
pick_ids = {}
for cel in [0, 5, 6, 1, 2, 7, 8, 3, 4, 9]:
    nid += 1; acoes.append({'id': nid, 'actorCellId': cel, 'type': 'pick', 'championId': 0, 'completed': False, 'isInProgress': False, 'isAllyAction': cel < 5}); pick_ids[cel] = nid
time_aliado = [{'cellId': c, 'assignedPosition': ['top', 'middle', 'jungle', 'bottom', 'utility'][c], 'championId': 0, 'championPickIntent': 0} for c in range(5)]
time_aliado[1]['championPickIntent'] = 131  # aliado quer Diana
trava = threading.Lock()

def avancar():
    t = time.time() - inicio
    for a in acoes:
        if a['completed']: continue
        if a['type'] == 'ban':
            a['isInProgress'] = 6 <= t < 26
            if t >= 26: a['completed'] = True  # quem não baniu, perdeu a vez
        elif a['actorCellId'] == 0:
            a['isInProgress'] = 26 <= t < 30
            if t >= 30: a['completed'] = True; a['championId'] = 64
        elif a['actorCellId'] == EU:
            a['isInProgress'] = t >= 30
    fase = 'PLANNING' if t < 6 else 'BAN_PICK'
    for m in time_aliado:
        acao = next(a for a in acoes if a['type'] == 'pick' and a['actorCellId'] == m['cellId'])
        if acao['championId']: m['championId'] = acao['championId']
    return fase

class H(BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def responder(self, obj, status=200):
        corpo = json.dumps(obj).encode()
        self.send_response(status); self.send_header('Content-Type', 'application/json'); self.send_header('Content-Length', str(len(corpo))); self.end_headers(); self.wfile.write(corpo)
    def do_GET(self):
        with trava:
            fase = avancar(); p = self.path
            if p == '/lol-game-data/assets/v1/champion-summary.json':
                return self.responder([{'id': -1, 'name': 'None', 'alias': 'None'}] + [{'id': i, 'name': n, 'alias': ALIAS.get(i, n)} for i, n in CAMPEOES.items()])
            if p == '/lol-gameflow/v1/gameflow-phase': return self.responder('ChampSelect')
            if p == '/lol-champ-select/v1/session':
                bans = [a['championId'] for a in acoes if a['type'] == 'ban' and a['completed'] and a['championId']]
                return self.responder({'localPlayerCellId': EU, 'actions': [[a for a in acoes if a['type'] == 'ban'], [a for a in acoes if a['type'] == 'pick']],
                                       'myTeam': time_aliado, 'theirTeam': [{'cellId': c, 'championId': 0} for c in range(5, 10)],
                                       'bans': {'myTeamBans': [], 'theirTeamBans': []}, 'timer': {'phase': fase}})
            if p in ('/lol-champ-select/v1/pickable-champion-ids', '/lol-champ-select/v1/bannable-champion-ids'): return self.responder(list(CAMPEOES))
            if p.startswith('/lol-perks/'): return self.responder([])
            return self.responder({'erro': p}, 404)
    def do_PATCH(self):
        corpo = json.loads(self.rfile.read(int(self.headers['Content-Length'])) or b'{}')
        with trava:
            avancar(); aid = int(self.path.rsplit('/', 1)[1]); a = next(x for x in acoes if x['id'] == aid)
            if a['completed']: return self.responder({'message': 'acao ja completa'}, 500)
            a['championId'] = corpo.get('championId', 0)
            log.append('%5.1fs PATCH %s %s cel %d -> %s' % (time.time() - inicio, a['type'], 'em andamento' if a['isInProgress'] else 'marcacao', a['actorCellId'], CAMPEOES.get(a['championId'])))
            self.send_response(204); self.end_headers()
    def do_POST(self):
        with trava:
            avancar()
            if self.path.endswith('/complete'):
                aid = int(self.path.split('/')[-2]); a = next(x for x in acoes if x['id'] == aid)
                if not a['isInProgress']: log.append('%5.1fs COMPLETE recusado (fora da vez) %s' % (time.time() - inicio, a['type'])); return self.responder({'message': 'fora da vez'}, 500)
                a['completed'] = True; a['isInProgress'] = False
                log.append('%5.1fs COMPLETE %s -> %s' % (time.time() - inicio, a['type'], CAMPEOES.get(a['championId'])))
                self.send_response(204); self.end_headers(); return
            self.responder({}, 404)

srv = ThreadingHTTPServer(('127.0.0.1', 50123), H)
ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER); ctx.load_cert_chain(sys.argv[1], sys.argv[2])
srv.socket = ctx.wrap_socket(srv.socket, server_side=True)
threading.Thread(target=srv.serve_forever, daemon=True).start()
time.sleep(float(sys.argv[3]))
print('\n'.join(log))
