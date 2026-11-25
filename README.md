# AutoPick LoL

Programa para Windows que, junto com o cliente do League of Legends:

- **aceita a partida** sozinho;
- **marca o pick** assim que a seleção abre e **trava** na sua vez, pela lista da rota em que você caiu (5 por rota; rota sem campeões ou modo sem rota: o app não escolhe nem bane);
- mostra no quadro da rota, ao vivo, o campeão marcado, travado e banido;
- **bane** pela lista da rota, esperando 5 s para não banir o que um aliado quer jogar;
- **põe os feitiços de invocador** da rota assim que a seleção abre, com o Flash no lado que você escolher;
- **cria a página de runas** do campeão travado: a sua (montada no editor, igual ao do cliente) ou a recomendada pelo LoL;
- mostra no editor as **runas mais usadas no BR com % de vitória**, e as dos **monos** e **pros** de cada campeão, vindas do seu servidor (pasta `servidor/`).

## Baixar

Baixe **AutoPickLoL-Setup.exe** na [última versão](https://github.com/guigon96-glitch/LOLAUTOPICK/releases/latest) e rode.
Ele instala para o seu usuário (sem administrador) em `%LOCALAPPDATA%\Programs\AutoPickLoL`, cria atalhos no Menu Iniciar e na Área de Trabalho e aparece em *Adicionar ou remover programas*.
O Windows pode avisar que o arquivo não é reconhecido (não é assinado): **Mais informações → Executar assim mesmo**.
Precisa só do .NET Framework 4.x, que já vem no Windows 10/11.

**Atualizações**: ao abrir, o app verifica a última versão no GitHub. Se houver uma nova, aparece o botão **Atualizar para 1.0.N**: ele baixa o instalador, instala em silêncio na mesma pasta e reabre o app. Suas listas e runas continuam.

Na pasta do app ficam `config.json` (suas listas e runas), `autopick.log` (registro) e a pasta `icones`. Ao desinstalar, o `config.json` é mantido.
Já usava a versão sem instalador? Copie o seu `config.json` para a pasta do app instalado.
Se o LoL não estiver em `C:\Riot Games\League of Legends`, ajuste `pastaLol` no `config.json`.

## Como funciona

- `app/Program.cs`: o programa inteiro (WinForms). Fala com o cliente pela API local dele (LCU), usando o `lockfile` da pasta do LoL. Campeões, runas e ícones vêm do Data Dragon (público, sempre do patch atual).
- `servidor/`: Cloudflare Worker + D1. A cada minuto lê algumas partidas ranqueadas Solo/Duo do BR de Esmeralda para cima na API da Riot e soma jogos e vitórias por página de runas (campeão + rota + patch). `GET /runas?campeao=103&rota=MIDDLE` devolve as 5 páginas mais usadas dos dois patches mais novos, os monos (8+ jogos com o campeão e metade das partidas do jogador, ordenados por elo) e os pros que jogam com ele. `GET /` mostra o andamento da coleta.
- `servidor/src/pros.js`: lista de pro players (Riot ID e time). A Riot não diz quem é pro, então a lista é mantida à mão; depois de um push o servidor acha cada conta e acompanha as partidas dela.

A automação do GitHub (`.github/workflows/build.yml`), a cada push na `main`: numera a versão (1.0.<número do build>, gravada em `app/Versao.cs`), compila o .exe, roda os testes, gera o instalador (`instalador/autopick.nsi`, NSIS) e publica a release `v1.0.N` com `AutoPickLoL-Setup.exe` e `AutoPickLoL.zip`.

## Pôr o servidor no ar

1. **Chave da Riot**: em developer.riotgames.com, *Register Product → Personal API Key* (não expira). A *Development API Key* serve para testar, mas vence a cada 24 h.
2. **Banco D1**: no painel da Cloudflare, *Storage & Databases → D1 → Create*, nome `autopick-runas`. Copie o **Database ID** para `database_id` em `servidor/wrangler.toml`.
3. **Worker**: *Workers & Pages → Create → Import a repository*, escolha este repositório e em **Root directory** ponha `servidor`.
4. **Segredo**: no Worker, *Settings → Variables and Secrets → Add*, tipo **Secret**, nome `RIOT_KEY`, valor = sua chave.
5. Abra o endereço do Worker (`https://lolautopick.<seu-subdominio>.workers.dev`): deve mostrar `partidas_lidas` subindo a cada minuto e `erro: null`.
6. No AutoPick, abra **Runas → um campeão** e cole esse endereço no painel "Mais usadas no BR".

Com a chave pessoal (100 pedidos a cada 2 min) o coletor lê ~5,7 mil partidas por dia (4 por minuto), gravando ~80 mil linhas no D1, abaixo do limite de 100 mil por dia do plano grátis. No plano pago dá para subir `PARTIDAS_POR_RODADA` em `wrangler.toml`.

## Testes

```bash
# bibliotecas de referência do .NET Framework 4.8 (compilar contra elas evita usar métodos que só existem no Mono)
curl -sfL -o ref.nupkg https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg && unzip -q -o ref.nupkg 'build/.NETFramework/v4.8/*' -d ref
mcs -target:winexe -win32icon:app/icone.ico -nostdlib -lib:ref/build/.NETFramework/v4.8 -r:mscorlib.dll -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll -out:AutoPickLoL.exe app/Program.cs app/Versao.cs && mono AutoPickLoL.exe --teste
makensis -DVERSAO=1.0.0 instalador/autopick.nsi
```

```bash
cd servidor && node teste.mjs
```

Teste da seleção de campeões sem o LoL: `app/lcu_falso.py` imita a API local do cliente numa partida de escolha alternada (instruções no topo do arquivo).

## Avisos

A API local do cliente não é oficialmente suportada pela Riot; ferramentas como Blitz e Porofessor fazem o mesmo, mas o uso é por sua conta.
AutoPick LoL não é endossado pela Riot Games.
