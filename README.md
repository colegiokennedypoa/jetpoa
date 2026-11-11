# JetPOA · Bistrô (novo sistema)

Substitui o sistema Forms 6i do bistrô. Análise e proposta: [docs/01-analise-e-proposta.md](docs/01-analise-e-proposta.md).

> **Vai continuar o desenvolvimento?** Comece por [docs/03-guia-desenvolvedor.md](docs/03-guia-desenvolvedor.md): ambiente, mapa do código, regras que não podem ser quebradas, convenções e pendências.

## Estrutura
| Pasta | Conteúdo |
|---|---|
| `db/` | Scripts Oracle (esquema `BISTRO`, tablespaces `DB_BISTRO`, `DB_IND_BISTRO`, `DB_LOB_BISTRO`) |
| `src/JetPoa.Api` | API ASP.NET Core 8 + Dapper + ODP.NET (Oracle XE 11.2) |
| `web/` | Front-end React + TypeScript (Vite). O build vai para `src/JetPoa.Api/wwwroot` |

## Banco (ordem de execução)
1. `db/00_criar_esquema_bistro.sql`: como SYSTEM (tablespaces + usuário BISTRO)
2. `db/00b_ajustar_tamanho_db_bistro.sql`: como SYSTEM, só se o DB_BISTRO foi criado com 100 MB
3. `db/01_fase1_cadastros.sql`: como BISTRO (tabelas, sequences, dados iniciais)
4. `db/02_importar_legado.sql`: como BISTRO (importa produtos, kits, garçons e áreas do ADMINISTRATIVO)
5. `db/03_fase2_comanda.sql`: como BISTRO (comandas, itens, envios para produção)
6. `db/04_fase3_fechamento.sql`: como BISTRO (fechamento, descontos, isenções, pagamentos)
7. `db/05_fase4_nfce.sql`: como BISTRO (emitente, numeração, NFC-e, inutilização, impressora do caixa)
8. `db/06_numeracao_por_ambiente.sql` e `db/07_fase5_checkin_estoque.sql`: como BISTRO
9. `db/08_comanda_exige_cliente.sql`, `db/09_login_senha.sql` e `db/10_usuario_garcom.sql`: como BISTRO

## Check-in, estoque e relatórios (Fase 5)
- **Check-in (portaria TERRA / RIO):** cada aparelho lembra a sua portaria.
  - Busca o cliente por CPF, celular ou nome, cadastra na hora sem duplicar (CPF ou celular) ou usa só um nome.
  - Abre a comanda pelo número da pulseira e **lança as taxas do evento do dia**: estacionamento (terra), embarque (rio) e couvert se a entrada for antes do fim da janela.
  - As isenções por categoria do cliente (VIP, sócio…) são aplicadas no fechamento.
- **Check-out:** registra a saída.
  - Comanda aberta **sem nenhum item**: é encerrada automaticamente (cancelada, motivo "Saída sem consumo") e a pessoa sai sem passar no caixa.
  - Comanda aberta com consumo ou com taxas do evento: a saída é **bloqueada** e a pessoa é encaminhada ao caixa.
- **Eventos:** aba Eventos no Check-in, com data, taxa de terra, taxa de rio e couvert com horário. Substitui `TB_BAR_EVENTO`/`TB_BAR_COUVER`.
- **Estoque:**
  - baixa automática no fechamento (o kit baixa os componentes, ex.: COMBO 6 HEINEKEN baixa 6 Heineken);
  - entrada de compra, que atualiza o custo;
  - perda com motivo e ajuste (só gerente);
  - inventário que lança apenas a diferença;
  - histórico por produto e lista "abaixo do mínimo" (quanto comprar).
  - Saldo negativo é permitido: a venda nunca é bloqueada por estoque.
- **Relatórios** por período, com exportação CSV:
  - faturamento, ticket médio, descontos, taxa de serviço, CMV e margem, cancelamentos, NFC-e autorizadas × faturamento;
  - por dia, horário, forma de pagamento, entrada Terra/Rio, categoria, produto e garçom;
  - descontos por motivo e por operador/autorizador.
  - O CMV usa o custo atual do produto.

## NFC-e (Fase 4)
Emissão direta com a SEFAZ-RS (modelo 65, layout 4.00), **sem o Tomcat/AIOWS**: o XML é montado, assinado e transmitido pela própria API.

**Configuração:** fica no arquivo local `C:\Projeto\certificado\52822987000123.conf`, ao lado do certificado e **fora do Git**. A API lê esse arquivo na inicialização (caminho em `appsettings.json` → `ArquivoConfiguracao`). Formato `Chave=Valor`, onde `__` separa os níveis:
```ini
ConnectionStrings__Bistro=User Id=bistro;Password=...;Data Source=192.168.0.109:1521/XE;
Nfce__Ambiente=2                 # 2 = homologação (testes) · 1 = produção
Nfce__Serie=2
Nfce__CertificadoArquivo=C:\Projeto\certificado\52822987000123.pfx
Nfce__CertificadoSenha=...
Nfce__CscId=1
Nfce__Csc=...
```
As mesmas chaves também podem vir por variável de ambiente. Valor vazio no arquivo não sobrescreve.
- **Série 1 (decisão do JetPOA):** o sistema novo continua a série 1 do Forms. Em **produção**, o próximo número é sempre `maior(último do sistema novo, último do Forms) + 1`, lido de `ADMINISTRATIVO.TB_BAR_PEDIDO_NFCE` (hoje: **1213**). Em homologação a numeração é separada.
- **Virada para produção:** os dois sistemas **não podem emitir NFC-e de produção ao mesmo tempo**. O Forms numera só pela própria tabela e reutilizaria números já usados pelo novo. Antes de ligar `Nfce__Ambiente=1`:
  1. parar de fechar comandas no Forms, porque a tela TEN10550 chama `SOURCE.PK_NFCE.GERANFFJETPOA` a cada fechamento;
  2. desativar o job 401 do Forms (`pk_nfce.job`, a cada 30 min), como DBA: `exec dbms_job.broken(401, true);` e `commit;`.
- **Pendência herdada:** o Forms tem **583 números da série 1 sem autorização**, que precisam ser inutilizados (tela Fiscal → Inutilizar). A tela recusa faixas que contenham notas autorizadas pelo Forms.
- **Primeiro uso:** rodar em **homologação** (`Ambiente = 2`), emitir algumas notas, conferir na tela **Fiscal** e só então passar para produção.
- **Fluxo:** ao fechar a comanda, a NFC-e é emitida e o DANFE sai na impressora do setor **CAIXA** (configurar o IP na tela Produção). Falha fiscal **não** desfaz o fechamento: a comanda aparece em "Comandas fechadas sem NFC-e" na tela Fiscal.
- **Antes de numerar**, o sistema valida NCM, CFOP, CSOSN e CST PIS/COFINS de cada produto, incluindo CFOP × CSOSN (500 → 5405; 102 → 5102). Produto inválido não consome número.
- **Fora da nota:** serviços (tipo S) e taxas (estacionamento, embarque, couvert), além da taxa de serviço. Os pagamentos são rateados para o valor da nota. *Validar com o contador.*
- **Desconto:** vai em `vDesc` por item (rateio do fechamento). **Forma de pagamento:** `tPag` real (01, 03, 04, 05, 17) com o grupo `card` para cartão e PIX.
- **SEFAZ sem resposta:**
  - a nota online fica **VERIFICAR**;
  - é emitida outra em **contingência offline** (tpEmis 9) com o próximo número, e o DANFE sai com o aviso;
  - a cada minuto, a rotina transmite as contingências e consulta as notas a verificar. Se a original foi autorizada, ela é cancelada (substituída); se não chegou na SEFAZ, o número é inutilizado.
- **Tela Fiscal:** status do certificado (dias para vencer), status da SEFAZ, notas das últimas 48h, DANFE (HTML), XML, reimpressão, reenvio de rejeitada, cancelamento e inutilização. Cancelar e inutilizar só aparecem para Gerente/Administrador.
- **Testes:** `dotnet test` (28 testes). Chave e QR Code conferem com NFC-e autorizada pela SEFAZ-RS. Para conferir o hash do QR Code com o CSC real: `$env:JETPOA_TEST_CSC = "<csc>"`.

## Login e perfis
- **Entrada com usuário e senha** (cookie de sessão de 12 h, renovado com o uso). Sem nenhum usuário cadastrado, a tela de login cria o primeiro Administrador.
- **Senha provisória:** quem cadastra o usuário define uma senha provisória; no primeiro acesso a pessoa é obrigada a trocá-la (mínimo 6 caracteres). Botão **Senha** no topo para trocar quando quiser.
- **Segurança:** 5 senhas erradas bloqueiam o usuário por 15 minutos. Redefinir a senha, mudar o perfil ou desativar derruba as sessões abertas daquele usuário em até 30 s.
- **Cada ação registra quem estava logado** (abriu comanda, lançou, cancelou item, fechou, deu desconto, movimentou estoque, alterou produto, cancelou nota).
- **O que cada perfil vê e faz** (o servidor bloqueia cada rota; a tela só mostra o permitido):

  | Perfil | Pode |
  |---|---|
  | Administrador | tudo, inclusive Usuários e Implantação |
  | Gerente | tudo menos Usuários e Implantação: relatórios, produtos, impressoras, eventos, estoque (entrada, perda, ajuste, inventário), cancelar comanda com itens, cancelar/inutilizar NFC-e |
  | Caixa | check-in, comandas (abrir, lançar, fechar, cancelar item), clientes, ver produção/fila, emitir NFC-e, ver estoque |
  | Garçom | ver comandas e lançar itens; abrir comanda pela portaria (check-in/check-out) e cadastrar clientes, como o Check-in |
  | Check-in | portaria (check-in/check-out) e clientes |

  As permissões ficam em `src/JetPoa.Api/Auth/Permissoes.cs`.
- **Cada garçom tem o próprio login:**
  - na tela Usuários, o login fica vinculado a um garçom. No perfil Garçom, se nenhum garçom for escolhido, o sistema cria um com o nome do usuário;
  - tudo o que esse login lança sai em nome desse garçom, sem opção de trocar depois de entrar (o servidor ignora outro garçom enviado);
  - um garçom só pode ter um login;
  - perfil Garçom sem vínculo não consegue lançar;
  - o caixa sem vínculo continua escolhendo o garçom ao lançar.
- **PIN de autorização (opcional, só Gerente/Administrador):** serve para o gerente liberar, no caixa de outra pessoa, desconto acima do limite, consumo interno ou cancelamento de comanda com itens.

## Fechamento (Fase 3)
- **Perfis:** Administrador (desconto até 100%, autoriza), Gerente (até 30%, autoriza), Caixa (até 5%), Garçom e Check-in (0%). Os limites ficam na tabela `perfil`.
- **Fechar conta:** vale o limite de desconto de quem está logado. Se o desconto passar do limite, ou se for consumo interno, a tela pede o gerente e o PIN dele.
- **Descontos:** em % ou R$, no total ou num item, sempre com motivo (cadastro `motivo_desconto`; "Outro", "Reclamação" e "Parceria" exigem observação).
- **Isenções automáticas pela categoria do cliente:** estacionamento (terra), embarque (rio) e couvert, conforme `cliente_categoria`. Não contam na alçada. A isenção manual conta.
- **Taxa de serviço:** % padrão no parâmetro `TAXA_SERVICO_PCT` (hoje 0), editável no fechamento. Incide só sobre consumo, nunca sobre as taxas.
- **Pagamento dividido** entre várias formas, "dividir em N" e troco somente no dinheiro. Cada forma tem o código `tPag` para a NFC-e.
- **Desconto rateado por item** em `comanda_item.valor_desconto` (base do `vDesc` da NFC-e). Tudo fica registrado em `comanda_desconto`, `comanda_pagamento` e `auditoria`.
- **Consumo interno:** a conta é cobrada a preço de custo, sempre com autorização de gerente. Produtos sem custo cadastrado usam o preço de venda.

## Impressão de produção (Fase 2)
- Cada produto pertence a um **setor** (COZINHA, BAR, GARCOM, NENHUM). O IP e a porta de cada impressora ficam na tela **Produção**, que também tem o botão "Imprimir teste".
- Ao lançar itens, a API grava um **envio** por setor. Um serviço em segundo plano manda o ticket ESC/POS direto para `IP:9100`, sem fila do Windows.
- Se a impressora falhar, o envio fica em **Erro** e o serviço tenta de novo sozinho (até 5 vezes). Na tela Produção há "Tentar de novo" e "Reimprimir".
- Cancelar um item já enviado imprime um aviso de **CANCELAMENTO**, com o motivo, no setor.
- Os acentos saem removidos por padrão (parâmetro `IMPRESSAO_REMOVER_ACENTOS`). O ticket usa a página de código PC850, então dá para testar com acentos.

O sistema novo **só grava no esquema BISTRO**. O `ADMINISTRATIVO` (Forms) é apenas lido.

## Rodar em desenvolvimento
```powershell
# a conexão e os segredos vêm de C:\Projeto\certificado\52822987000123.conf
dotnet run --project src/JetPoa.Api        # API em http://localhost:5080  (Swagger em /swagger)
cd web; npm run dev                        # front em http://localhost:5173 (proxy /api → 5080)
```

## Implantação (Fase 6)
**Roteiro completo da virada:** [docs/02-roteiro-virada.md](docs/02-roteiro-virada.md). A tela **Implantação** mostra o checklist ao vivo: usuários, impressoras, NFC-e, certificado, SEFAZ, produtos pendentes, numeração e Forms parado.

```powershell
.\scripts\publicar.ps1           # build do front + testes + publish em C:\Projeto\JetPOA\app
.\scripts\instalar-servico.ps1   # (Administrador) serviço JetPOABistro, reinício automático, firewall 5080 só rede local
.\scripts\atualizar.ps1          # (Administrador) nova versão: compila/testa antes de parar e guarda a anterior
```
- **Produtos:** criar e editar (preço, setor de impressão, estoque mínimo e dados fiscais com orientação), só Gerente/Administrador, com auditoria.
- **Importar clientes do Forms:** mailing, reservas, pedidos e comandas, com deduplicação por CPF/celular. Mostra prévia antes de gravar.
- **Sincronizar produtos do Forms** durante o paralelo: novos, preço e ativo. Dados fiscais só preenchem o que está vazio.

> Numeração da NFC-e separada por ambiente (`db/06_numeracao_por_ambiente.sql`): testes em homologação não consomem números de produção.

> **Regras de cadastro:** toda comanda exige cliente cadastrado (`db/08_comanda_exige_cliente.sql`) e o CPF nunca se repete (índice único + aviso nas telas).
