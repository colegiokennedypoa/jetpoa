# JetPOA Bistrô: guia para continuar o desenvolvimento

Este guia é para quem assume o projeto. Ele explica o que o sistema é, onde cada coisa está, como rodar e publicar, e quais regras não podem ser quebradas. Leia junto com:
- [README.md](../README.md): funcionalidades por fase;
- [01-analise-e-proposta.md](01-analise-e-proposta.md): análise do sistema Forms antigo;
- [02-roteiro-virada.md](02-roteiro-virada.md): roteiro da troca do Forms pelo sistema novo.

*Situação em 11/11/2026.*

---

## 1. O que é
Sistema do bistrô do JetPOA. O complexo náutico de Porto Alegre tem duas entradas: **Terra** (estacionamento) e **Rio** (embarque). O sistema substitui o antigo **Oracle Forms 6i** e cobre:
- check-in na portaria e comandas por pulseira;
- lançamento de itens com impressão na cozinha, bar ou garçom;
- fechamento com descontos, isenções e pagamento dividido;
- **NFC-e direto com a SEFAZ-RS**;
- estoque, relatórios e controle de acesso por perfil.

| Camada | Tecnologia |
|---|---|
| API | .NET 8, minimal API, Dapper, Oracle.ManagedDataAccess (ODP.NET) |
| Banco | Oracle XE 11.2, esquema **`BISTRO`** (tablespaces `DB_BISTRO`, `DB_IND_BISTRO`, `DB_LOB_BISTRO`) |
| Front | React 19 + TypeScript + Vite 6. O build vai para `src/JetPoa.Api/wwwroot`, e a API serve o front |
| Testes | xUnit (`tests/JetPoa.Tests`), 28 testes de fechamento e NFC-e |
| Produção | Serviço Windows `JetPOABistro`, porta 5080, pasta `C:\Projeto\JetPOA\app` |

## 2. Regras que não podem ser quebradas
1. **O sistema novo só grava no esquema `BISTRO`.** O esquema `ADMINISTRATIVO` (Forms) e os outros são **somente leitura**. Eles são lidos para importar clientes e produtos e para continuar a numeração da NFC-e.
2. **A pasta de rede `\\192.168.0.109\sistema\prontas`** (fontes do Forms) é **somente leitura**.
3. **A NFC-e usa a série 1.** Em produção, o próximo número é `maior(último do BISTRO, último do Forms em ADMINISTRATIVO.TB_BAR_PEDIDO_NFCE) + 1`.
4. **Nunca zerar nem reduzir `nfce_numeracao.ultimo_numero`**, e nunca apagar linhas de `nfce` que já chegaram à SEFAZ, mesmo em homologação. Já aconteceu: uma limpeza de teste zerou a numeração e a nota seguinte foi rejeitada com a **539** (duplicidade). Hoje o sistema renumera e reenvia sozinho quando recebe a 539, mas a limpeza não pode provocar isso.
5. **O Forms e o sistema novo não podem emitir NFC-e de produção ao mesmo tempo.** O Forms numera só pela própria tabela. Veja o roteiro de virada (parar o job 401).
6. **Toda comanda exige um cliente cadastrado.** A regra está na API, nas telas e no banco (`db/08`).
7. **O CPF do cliente é único.** Há índice único no banco e aviso nas telas.
8. **Segredos ficam fora do Git:** conexão, senha do certificado e CSC estão em `C:\Projeto\certificado\52822987000123.conf`. O `.gitignore` já bloqueia `*.conf`, `*.pfx` e `*.p12`.
9. **Falha fiscal não desfaz o fechamento.** A comanda fica fechada e a nota pendente aparece na tela Fiscal.
10. **Venda nunca é bloqueada por estoque.** O saldo pode ficar negativo.

## 3. Ambiente da máquina servidor
| Item | Onde |
|---|---|
| Código (clone) | `C:\Users\JETPOA-SERVIDOR\Documents\jetpoa` |
| Repositório | `github.com/colegiokennedypoa/jetpoa`, branch `main` |
| Push | SSH com deploy key, pelo alias `github-jetpoa` (`git@github-jetpoa:colegiokennedypoa/jetpoa.git`) definido em `~/.ssh/config` |
| Programa instalado | `C:\Projeto\JetPOA\app` (versão anterior em `app.anterior`) |
| Configuração e segredos | `C:\Projeto\certificado\52822987000123.conf` |
| Certificado A1 | `C:\Projeto\certificado\52822987000123.pfx`, **vence em 21/11/2026** |
| Oracle | `192.168.0.109:1521/XE`, usuário `bistro` (IP temporário; o TNS antigo aponta `192.168.101.12`) |
| npm / git | **Não estão no PATH**: `"C:\Program Files\nodejs\npm.cmd"` e `"C:\Program Files\Git\cmd\git.exe"` |
| PowerShell | 5.1, com **execução de scripts bloqueada por política**. Rode os comandos dos `.ps1` direto no console ou peça ao administrador. Serviço e firewall exigem PowerShell **como Administrador** |

Formato do `.conf` (`Chave=Valor`, onde `__` separa níveis; variáveis de ambiente sobrescrevem; valor vazio não sobrescreve):
```ini
ConnectionStrings__Bistro=User Id=bistro;Password=...;Data Source=192.168.0.109:1521/XE;
Nfce__Ambiente=2          # 2 = homologação · 1 = produção
Nfce__Serie=1
Nfce__CertificadoArquivo=C:\Projeto\certificado\52822987000123.pfx
Nfce__CertificadoSenha=...
Nfce__CscId=...
Nfce__Csc=...
```

## 4. Rodar, testar e publicar
```powershell
# desenvolvimento
dotnet run --project src/JetPoa.Api            # API em :5080 (Swagger em /swagger)
cd web; & "C:\Program Files\nodejs\npm.cmd" run dev   # front em :5173 com proxy /api → 5080
```
Com o serviço de produção rodando, a porta 5080 já está ocupada. Rode a API de teste em outra porta: `dotnet run --project src/JetPoa.Api -- --urls http://127.0.0.1:5082`. Lembre que ela usa **o mesmo banco** de produção.

```powershell
dotnet test                                    # 28 testes
cd web; & "C:\Program Files\nodejs\npm.cmd" run build   # tsc + vite build → src/JetPoa.Api/wwwroot
```

**Publicar uma nova versão** (o que `scripts\atualizar.ps1` faz):
1. Build do front, `dotnet test` e `dotnet publish src\JetPoa.Api\JetPoa.Api.csproj -c Release -o C:\Projeto\JetPOA\app.nova`.
2. Como Administrador:
   ```powershell
   Stop-Service JetPOABistro; Remove-Item C:\Projeto\JetPOA\app.anterior -Recurse -Force
   Rename-Item C:\Projeto\JetPOA\app app.anterior; Rename-Item C:\Projeto\JetPOA\app.nova app; Start-Service JetPOABistro
   ```
3. Conferir em `http://127.0.0.1:5080/api/health`.
4. Para voltar à versão anterior, faça o inverso com `app.anterior`.

## 5. Mapa do código
```
db/                       scripts Oracle numerados (rodar em ordem; nunca editar um script já aplicado: criar o próximo número)
src/JetPoa.Api/
  Program.cs              DI, cookie de login, políticas por permissão, middleware de troca de senha, rotas
  Auth/Permissoes.cs      constantes de permissão e DoPerfil() (permissões de cada perfil); extensões user.Id() e user.Pode()
  Auth/AuthEndpoints.cs   /api/auth: login, sair, eu, trocar-senha, primeiro-acesso; bloqueio por tentativas; carimbo de segurança
  Data/                   Db (conexões), ArquivoConf (leitor do .conf), Clob
  Domain/                 regras puras e testáveis: Fechamento (rateio, alçada, isenções), Estoque, Documento (CPF/CNPJ), Senha (hash)
  Endpoints/              uma classe por área (Comanda, Fechamento, Cliente, Checkin, Estoque, Produto, Producao, Fiscal, Relatorio, Usuario, Implantacao, Catalogo)
  Fiscal/                 NFC-e: NfceXml (monta o XML 4.00), Assinador, SefazClient (SOAP/TLS), NfceService (fluxo, numeração, contingência), NfceWorker (rotina de 1 min), Danfe (ESC/POS e HTML)
  Impressao/              EscPos, ImpressoraRede (TCP 9100), ImpressaoProducao (fila em segundo plano com novas tentativas)
web/src/
  api.ts                  todas as chamadas HTTP e tipos; P = espelho das permissões do backend; evento de sessão expirada (401)
  sessao.ts               SessaoContext / useSessao() → { eu, pode(perm) }
  App.tsx                 login, abas filtradas por permissão, cabeçalho com usuário
  pages/                  uma tela por aba (Comandas, Garcom, Checkin, Producao, Fiscal, Estoque, Relatorios, Clientes, Produtos, Usuarios, Implantacao, Login)
  components/             Lancamento, FecharConta, BuscaCliente, EditarProduto
tests/JetPoa.Tests/       FechamentoTests, NfceTests (chave, QR Code, XML)
scripts/                  publicar.ps1, instalar-servico.ps1, atualizar.ps1
```

### Banco (esquema BISTRO)
| Script | Conteúdo |
|---|---|
| `00`, `00b` | (SYSTEM) tablespaces e usuário BISTRO |
| `01` | `perfil`, `usuario`, `setor_producao`, `categoria`, `produto`, `produto_componente` (kits), `produto_codigo_barras`, `garcom`, `area`, `cliente_categoria`, `cliente`, `parametro`, `auditoria` |
| `02` | importação de produtos, kits, garçons e áreas do ADMINISTRATIVO |
| `03` | `comanda`, `comanda_item`, `envio_producao` |
| `04` | `motivo_desconto`, `forma_pagamento`, `comanda_desconto`, `comanda_pagamento` |
| `05` | `emitente`, `nfce_numeracao`, `nfce`, `nfce_inutilizacao` |
| `06` | numeração da NFC-e separada por ambiente |
| `07` | `evento`, `movimento_estoque`, colunas de check-in |
| `08` | comanda exige cliente |
| `09` | login com senha: hash, troca obrigatória, bloqueio, carimbo de segurança, último acesso |

Todos os scripts até o `09` **já foram aplicados** no banco de produção. As colunas são snake_case e o Dapper mapeia para PascalCase (`MatchNamesWithUnderscores`).

## 6. Convenções e "como fazer"
- **Nova rota protegida:** dentro do `MapXxx`, coloque a rota num grupo com `.RequireAuthorization(Permissoes.Algo)`. Toda rota exige login por padrão (FallbackPolicy). Use `AllowAnonymous()` só com justificativa.
- **Quem fez a ação:** use `user.Id()` (parâmetro `ClaimsPrincipal user` na rota). **Nunca** receba o id do usuário no corpo da requisição. Grave em `auditoria` as ações sensíveis.
- **Regra condicional dentro da rota:** `if (!user.Pode(Permissoes.X)) return Results.Forbid();`.
- **Nova permissão:**
  1. criar a constante em `Permissoes.cs`;
  2. incluir em `Todas` e nos perfis certos em `DoPerfil()`;
  3. espelhar em `P` no `web/src/api.ts`;
  4. na tela, usar `useSessao().pode(P.x)` para esconder botões.
  O servidor é quem garante a regra. A tela só esconde o que não está liberado.
- **PIN** existe só para um gerente **autorizar no caixa de outra pessoa**: desconto acima da alçada, consumo interno, cancelar comanda com itens. Não use o PIN como login.
- **Sessão:** cookie `jetpoa.sessao` (12 h, renova com o uso).
  - A cada 30 s a sessão é revalidada contra `usuario.carimbo_seguranca`. Trocar a senha, o perfil ou desativar o usuário derruba a sessão.
  - A claim `trocar=S` bloqueia tudo fora de `/api/auth` até a troca da senha.
- **Regras de cálculo** ficam em `Domain/` (sem banco), com teste em `tests/`.
- **Mensagens de erro** para o usuário em português: `Results.UnprocessableEntity(new { erro = "..." })`. O front exibe o campo `erro`.
- **Banco:** mudanças só por novo script `db/NN_descricao.sql`. Rode como BISTRO e registre no README e na tabela acima.
- **Commits:** em português, um por etapa, com push para `main`. No PowerShell 5.1, passe a mensagem por arquivo: `git commit -F msg.txt`.
- **Textos de tela e documentação** em português do Brasil, com a linguagem do bistrô (comanda, caixa, garçom, portaria).

## 7. Sistema antigo (Forms), só para consulta
- Tabelas do bistrô: `ADMINISTRATIVO.TB_BAR_*`. A NFC-e antiga está no pacote `SOURCE.PK_NFCE` (job 401 a cada 30 min).
- O app do garçom e o check-in antigos estão em `WEB.JETPOA00xx` (PL/SQL HTML).
- A tela de caixa é a `TEN10550`, nas fontes em `\\192.168.0.109\sistema\prontas` (somente leitura).
- O Forms assinava a NFC-e pelo Tomcat AIOWS (`C:\apache-tomcat-7.0.47`). O sistema novo **não depende dele**.
- Impressoras antigas: COZINHA `192.168.101.50`, BALCÃO `.55`, GARÇOM `.60`, porta 9100. Hoje a rede `192.168.101.x` não responde deste servidor.
- Não há sqlplus nesta máquina. Para consultas avulsas, use um programa .NET pequeno com ODP.NET (o próprio projeto serve de exemplo) ou o Swagger.

## 8. Situação atual e pendências
**Pronto e no Git:**
- Fase 1, cadastros;
- Fase 2, comandas e impressão;
- Fase 3, fechamento;
- Fase 4, NFC-e, validada em homologação;
- Fase 5, check-in, estoque e relatórios;
- Fase 6, implantação;
- comanda exige cliente e CPF único;
- **login com senha e permissões por perfil** (último commit).

**Operacional, cabe ao responsável do bistrô e à TI:**
- [ ] Trocar a versão instalada pela publicada em `C:\Projeto\JetPOA\app.nova` (seção 4). Depois, os usuários `admin` e `eduarda` entram com o **PIN atual como senha** e trocam no primeiro acesso.
- [ ] **Renovar o certificado A1** (vence em 21/11/2026) e atualizar o `.conf`.
- [ ] Restabelecer a rede `192.168.101.x`: impressoras de produção e servidor no IP definitivo. Depois, ajustar a conexão no `.conf`.
- [ ] Contador: completar NCM, CFOP, CSOSN e CST dos produtos pendentes (77 em 11/11). Validar também o tratamento fiscal de serviços e taxas fora da nota e o rateio de pagamentos.
- [ ] Virada D0 conforme [02-roteiro-virada.md](02-roteiro-virada.md): parar o Forms e o job 401, CSC de produção e `Nfce__Ambiente=1`.
- [ ] Depois da virada: inutilizar os 583 números da série 1 que o Forms deixou sem autorização (tela Fiscal).

**Ideias e melhorias técnicas, não pedidas ainda:**
- CMV dos relatórios usa o custo **atual** do produto, não o custo da época da venda.
- Testes automatizados de API e permissões (hoje os 37 testes de permissão foram feitos por script manual, fora do repositório).
- HTTPS na rede local, se o acesso passar a ser feito fora da LAN. Hoje o firewall libera a 5080 só para a sub-rede local.
- Backup do esquema BISTRO (expdp agendado), a combinar com o DBA.

## 9. Histórico (git log)
| Commit | Etapa |
|---|---|
| `41c6d49` | Fases 1 a 3 e início da fase 4 (banco) |
| `194c509` | Fase 4: NFC-e direta com a SEFAZ-RS, tela Fiscal e testes |
| `9a8fcba` | Configuração local via `.conf` |
| `fa2cf6b`, `24a23d1` | TLS com SEFAZ-RS, numeração por ambiente, validação em homologação |
| `f52cffd` | Série 1 continua a numeração do Forms |
| `23fa44b` | Fase 5: check-in, eventos, estoque, relatórios |
| `ea619ff`, `71e5df3` | Fase 6: implantação, serviço Windows, roteiro de virada |
| `f46fd7c`, `8cf6aef`, `68f5b1f` | Comanda exige cliente, CPF único, tratamento automático da rejeição 539 |
| `ccc5da4` | Login com usuário e senha e permissões por perfil |
