# AutoPick LoL

Programa para Windows que, junto com o cliente do League of Legends:

- **aceita a partida** sozinho;
- **marca o pick** assim que a seleção abre e **trava** na sua vez, pela lista da rota em que você caiu (5 por rota + lista "Qualquer" de reserva);
- **bane** pela lista da rota, esperando 5 s para não banir o que um aliado quer jogar;
- **cria a página de runas** do campeão travado: a sua (montada no editor, igual ao do cliente) ou a recomendada pelo LoL;
- mostra no editor as **runas mais usadas no BR com % de vitória**, vindas do seu servidor (pasta `servidor/`).

## Baixar

Vá em **Releases → ultima**, baixe `AutoPickLoL.zip`, extraia e abra `AutoPickLoL.exe`.
O Windows pode avisar que o arquivo não é reconhecido (não é assinado): **Mais informações → Executar assim mesmo**.
Precisa só do .NET Framework 4.x, que já vem no Windows 10/11.

Ao lado do .exe ficam `config.json` (suas listas e runas), `autopick.log` (registro) e a pasta `icones`.
Se o LoL não estiver em `C:\Riot Games\League of Legends`, ajuste `pastaLol` no `config.json`.

## Como funciona

- `app/Program.cs`: o programa inteiro (WinForms). Fala com o cliente pela API local dele (LCU), usando o `lockfile` da pasta do LoL. Campeões, runas e ícones vêm do Data Dragon (público, sempre do patch atual).
- `servidor/`: Cloudflare Worker + D1. A cada minuto lê algumas partidas ranqueadas Solo/Duo do BR de Esmeralda para cima na API da Riot e soma jogos e vitórias por página de runas (campeão + rota + patch). `GET /runas?campeao=103&rota=MIDDLE` devolve as 5 páginas mais usadas dos dois patches mais novos. `GET /` mostra o andamento da coleta.

A automação do GitHub (`.github/workflows/build.yml`) compila o .exe, roda os testes e publica o .exe na release `ultima` a cada push na `main`.

## Pôr o servidor no ar

1. **Chave da Riot**: em developer.riotgames.com, *Register Product → Personal API Key* (não expira). A *Development API Key* serve para testar, mas vence a cada 24 h.
2. **Banco D1**: no painel da Cloudflare, *Storage & Databases → D1 → Create*, nome `autopick-runas`. Copie o **Database ID** para `database_id` em `servidor/wrangler.toml`.
3. **Worker**: *Workers & Pages → Create → Import a repository*, escolha este repositório e em **Root directory** ponha `servidor`.
4. **Segredo**: no Worker, *Settings → Variables and Secrets → Add*, tipo **Secret**, nome `RIOT_KEY`, valor = sua chave.
5. Abra o endereço do Worker (`https://lolautopick.<seu-subdominio>.workers.dev`): deve mostrar `partidas_lidas` subindo a cada minuto e `erro: null`.
6. No AutoPick, abra **Runas → um campeão** e cole esse endereço no painel "Mais usadas no BR".

Com a chave pessoal (100 pedidos a cada 2 min) o coletor lê ~7 mil partidas por dia. No plano pago do Workers dá para subir `PARTIDAS_POR_RODADA` em `wrangler.toml`.

## Testes

```bash
mcs -target:winexe -out:AutoPickLoL.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Web.Extensions.dll app/Program.cs && mono AutoPickLoL.exe --teste
```

```bash
cd servidor && node teste.mjs
```

## Avisos

A API local do cliente não é oficialmente suportada pela Riot; ferramentas como Blitz e Porofessor fazem o mesmo, mas o uso é por sua conta.
AutoPick LoL não é endossado pela Riot Games.
