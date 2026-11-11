# JetPOA Bistrô — Análise do sistema atual e proposta do novo sistema

Levantamento feito em 11/11/2026, somente leitura, com o usuário `BISTRO_RO` no Oracle XE 11g (192.168.0.109) e com os fontes do Forms em `\\192.168.0.109\sistema\prontas`.

---

## 1. Onde está cada coisa hoje

| Peça | Onde fica | Observação |
|---|---|---|
| Dados do bistrô | Oracle XE 11.2, esquema **ADMINISTRATIVO**, tabelas `TB_BAR_*` | XE 11g é de 2011 e não tem mais suporte |
| Tela de caixa / comanda | Forms 6i `TEN10550` (mesa, garçom, couvert, estacionamento, fechamento, NFC-e) | Forms 6i é de 2000 e roda via `ifrun60` |
| Cadastro de produto | Forms `TEN10380` (produto, kit, ingrediente, código de barras, quesitos) | |
| Estoque / inventário | Forms `TEN10420`–`TEN10470`, relatórios `REN10480/10580` | |
| Abertura de mesa/comanda | Forms `TEN10560`, `TEN10570` | |
| Garçons, couvert, evento | Forms `TEN10590`–`TEN10640` | |
| **Check-in TERRA / RIO** | Web PL/SQL `WEB.JETPOA0050` (`registrarEntrada`/`registrarSaida`) | `ENTRADA_TIPO`: `T` = Terra, `A` = Rio (água) |
| **App do garçom** | Web PL/SQL `WEB.JETPOA0070` / `JETPOA0080` (HTML + JS gerados no banco) | usa o parâmetro `LIMITE_COMANDA` |
| Reserva / mailing | `WEB.JETPOA0020`, `TB_BAR_RESERVA`, `TB_BAR_MAILING` | |
| **NFC-e** | Package `SOURCE.PK_NFCE` (monta o XML no banco) + Tomcat **AIOWS** (`AssinaXML`, `EnviaSOAP`) com certificado A1 | SEFAZ-RS, modelo 65, série 1 |
| Relatório diário por e-mail | `SOURCE.PR_NOTIFICA_COMANDAS` / `PR_NOTIFICA_CAIXA` | |

### Impressão de produção (regra atual)

A rotina `PR_IMPRIMIT_COMANDAS` do `TEN10550` percorre os itens ainda não impressos da comanda e decide o destino pelo campo `TB_BAR_PRODUTO.TIPO_COMANDA`:

| TIPO_COMANDA | Fila no Windows | IP:porta | Impressora |
|---|---|---|---|
| `C` Cozinha | `COZINHA` | 192.168.101.50:9100 | Bematech MP-4200 HS |
| `B` Balcão (bar) | `BALCAO` | 192.168.101.55:9100 | G250 |
| `G` Garçom (pega direto: água, refri, lata) | `GARCON` | 192.168.101.60 | Bematech MP-4200 TH |
| `O` Outros (serviços, estacionamento) | — não imprime — | | |

- O controle é feito por flags em `TB_BAR_MESA_PRODUTO`: `FL_COMANDADO_COZINHA/BALCAO/GARCON/EVENTO`.
- O relatório é o `REN10550.RDF` (`DESNAME=COZINHA|BALCAO`). Em dia de evento usa o `REN10550E.RDF`.
- ⚠️ As impressoras e o AIOWS (`192.168.101.76:8888`) estão na rede `192.168.101.x`. O servidor mudou temporariamente para `192.168.0.109`, então é preciso confirmar se elas continuam acessíveis.

---

## 2. Modelo de dados atual (bistrô)

```
TB_BAR_TIPO_PRODUTO (18 categorias)
   └── TB_BAR_PRODUTO (420; 200 ativos)   FL_TIPO: P=produto K=kit S=serviço
         ├── TB_BAR_PRODUTO_KIT          (combos/drinks → baixa os componentes no estoque)
         ├── TB_BAR_PRODUTO_INGREDIENTE  (ficha técnica, quase não usada: 2 linhas)
         ├── TB_BAR_PRODUTO_CODEBAR      (vazia → NFC-e sai "SEM GTIN")
         └── TB_BAR_PRODUTO_QUESITO / ITEM_QUESITO / QUESITO_AGRUPADOR (vazias)

TB_BAR_MESA  = comanda aberta (467)       nome/telefone/sexo SOLTOS, ENTRADA_TIPO T/A, VIP, FL_NORMAL_CUSTO, CD_AREA
   └── TB_BAR_MESA_PRODUTO = itens lançados (flags de impressão, garçom, observação)

TB_BAR_PEDIDO = comanda fechada (7.695)   FL_MEIO_PAGAMENTO D/P/C/A (UM só por comanda), NU_DOCUMENTO (CPF)
   ├── TB_BAR_PEDIDO_PRODUTO / _GARCON / _KIT
   └── TB_BAR_PEDIDO_NFCE (1.212)         chave, XML, request/response, protocolo

TB_BAR_MOVIMENTO_ESTOQUE (34.609)  V=venda A=ajuste/entrada C=compra
TB_BAR_FECHAMENTO / _ITEM          inventário diário
TB_BAR_EVENTO / TB_BAR_COUVER      dias de evento → cobra estacionamento (entrada Terra) e couvert por horário
TB_BAR_AREA (16)                   churrasqueiras, deck, lounges, gramado...
TB_BAR_GARCON (39), TB_BAR_PEDIDO_GARCON (pedido via app)
TB_BAR_PESSOA_SALDO / _MOVIMENTO / _DEPENDENTE   (crédito pré-pago, vazias, ligadas ao TB_PESSOA do sistema escolar)
TB_BAR_MAILING (4.833), TB_BAR_RESERVA (353)     contatos espalhados, sem chave de cliente
```

---

## 3. Problemas encontrados

### 3.1 Cliente
- **Não existe cadastro de cliente.** Nome, telefone, sexo e CPF são digitados de novo em cada comanda (`TB_BAR_MESA`), em cada pedido (`TB_BAR_PEDIDO`), no mailing e na reserva, sem nenhuma ligação entre eles.
- Por isso não dá para ver o histórico de quem é cliente, aplicar benefício por cliente (VIP, sócio, funcionário) nem fazer marketing com consentimento (LGPD).
- O campo `CD_AREA` é texto livre (`.`, `,`, `Hshd`, `Deck 4`, `0`), ou seja, a área na prática não é validada.

### 3.2 Desconto e isenção
Hoje o desconto é um **produto com preço negativo** lançado na comanda:
`DESCONTO R$10 OFF`, `DESCONTO R$5 OFF`, `VALOR JA PAGO 100 REAIS`, `NADA - ZERO (0,01)`, `CAIPIRINHA - BRINDE (0,01)`.

- Não há motivo, autorização nem limite por usuário. Qualquer operador dá qualquer desconto.
- Não há desconto percentual, por item ou por categoria.
- A isenção de estacionamento ou couvert é feita apagando o item ou lançando `ESTACIONAMENTO DESCONTO`.
- O modo "Preço de CUSTO" (`FL_NORMAL_CUSTO='C'`) reprecifica a comanda inteira pelo `VL_ULTIMA_COMPRA`. É uma isenção sem trilha de auditoria.
- Na NFC-e o desconto não aparece (`vDesc` sempre 0). Os itens negativos são simplesmente descartados do XML.

### 3.3 Fiscal (NFC-e). Prioridade alta; validar com o contador

Vendas entre 06/11/2025 e hoje:

| Meio | Comandas | Valor | Com NFC-e autorizada | Valor em NFC-e |
|---|---|---|---|---|
| Crédito | 1.147 | R$ 163.471 | 336 | R$ 30.852 |
| Débito | 1.008 | R$ 101.841 | 179 | R$ 14.413 |
| PIX | 672 | R$ 93.994 | 114 | R$ 10.515 |
| Dinheiro | 247 | R$ 22.770 | **0** | — |
| **Total** | **3.074** | **R$ 382.076** | **629 (20%)** | **R$ 55.780 (15%)** |

Causas encontradas no código e nos dados:
1. **Venda em dinheiro nunca gera NFC-e.** A view `VW_BAR_PEDIDO_NFCE` filtra `fl_meio_pagamento != 'D'`.
2. **Itens sem dados fiscais são excluídos do cupom em silêncio.** O cursor só aceita itens com CFOP preenchido, CSOSN 102/500 e PIS/COFINS 01/04/49. Hoje ficam **fora do cupom 41 dos 46 kits ativos** (drinks e combos), 32 dos 150 produtos e os serviços (estacionamento). A nota sai com valor menor que o cobrado.
3. **583 tentativas sem protocolo.** Os motivos são NCM inválido/inexistente, *CFOP não permitido para o CSOSN* e *Duplicidade de NF-e*. A numeração é `MAX+1` sem controle de concorrência e sem **inutilização** das numerações perdidas. Em 458 casos a resposta não foi gravada.
4. `tPag` é sempre `99` (Outros). Cartão e PIX deveriam sair como `03`/`04`/`17` (com grupo `card` quando for o caso).
5. Não há **contingência offline** (tpEmis 9). Se a SEFAZ ou a internet cair, a venda fica sem documento.
6. O token CSC está fixo no código. O cancelamento usa a justificativa fixa "Teste de emissao no ambiente de producao".
7. A geração depende de um job no banco que chama o Tomcat por HTTP sem retorno ao caixa. O operador não vê se a nota foi autorizada.

### 3.4 Tecnologia
- Forms 6i + Oracle XE 11g + PL/SQL gerando HTML: tudo isso está fora de suporte, é difícil de manter e só roda em Windows com runtime antigo.
- A regra de negócio está espalhada entre Forms, packages PL/SQL e Tomcat.
- A impressão depende das filas do Windows na máquina que roda o Forms.

---

## 4. Proposta do novo sistema

### 4.1 Arquitetura

```
 Tablets/celulares (garçom)  ─┐
 Caixa (PC)                   ├──►  Web App (PWA, navegador)  ──►  API (servidor local)  ──► Banco PostgreSQL
 Check-in TERRA / RIO         │                                      │
 Tela da cozinha (opcional)  ─┘                                      ├──► Impressoras ESC/POS via TCP 9100 (cozinha .50, bar .55, garçom .60)
                                                                     ├──► SEFAZ-RS NFC-e 4.00 (certificado A1, contingência offline)
                                                                     └──► E-mail/relatórios
```

- **Roda localmente** no servidor do JetPOA, porque o caixa não pode parar se a internet cair. A internet só é necessária para a SEFAZ e, em contingência, a NFC-e é transmitida depois.
- **Backend recomendado: .NET 8 (C#).** Já está instalado neste servidor, tem a biblioteca de NFC-e mais madura do Brasil (DFe.NET/Zeus: assinatura, autorização, contingência, cancelamento, inutilização e DANFE NFC-e em ESC/POS) e lida bem com Windows e impressoras de rede.
- **Frontend:** React + TypeScript como PWA, uma única aplicação com perfis (Caixa, Garçom, Check-in, Gerente, Cozinha).
- **Banco:** PostgreSQL 16, sem custo e com suporte atual. Os produtos e o histórico seriam migrados do Oracle por script. A alternativa é manter o Oracle durante a transição; ver a pergunta na seção 6.

### 4.2 Módulos

1. **Cadastro de Clientes (novo)**
   - CPF (opcional e validado), nome, celular/WhatsApp, e-mail, nascimento, sexo, consentimento LGPD.
   - **Categoria** (Normal, VIP, Sócio/Cotista, Funcionário, Parceiro), que define os benefícios automáticos.
   - Histórico de visitas, consumo e ticket médio, e se costuma chegar por terra ou pelo rio.
   - Importação inicial deduplicada por CPF/telefone a partir de `TB_BAR_MAILING`, `TB_BAR_RESERVA`, `TB_BAR_MESA` e `TB_BAR_PEDIDO`.
2. **Entrada TERRA / RIO**
   - O check-in abre a comanda **vinculada ao cliente**, com o tipo de entrada, a área e o horário.
   - A tarifa por tipo de entrada vira regra configurável (estacionamento na terra, taxa de embarque/ancoragem no rio, couvert por janela de horário em dia de evento) no lugar das tabelas `TB_BAR_EVENTO`/`TB_BAR_COUVER` com produto fixo.
3. **Produtos e estoque** (base: os 420 produtos atuais)
   - Produto, kit/combo e ficha técnica com baixa dos componentes, categoria, preço e preço de custo.
   - **Setor de produção** (Cozinha, Bar, Garçom, Nenhum) como tabela própria com IP/porta da impressora.
   - Dados fiscais **obrigatórios e validados** (NCM em tabela oficial, CFOP x CSOSN compatíveis, CEST quando houver). Não dá para vender produto sem fiscal válido.
   - Entrada de compra, inventário diário com divergência e justificativa (como hoje no `TB_BAR_FECHAMENTO`).
4. **Comanda / pedidos**
   - Garçom lança pelo celular, caixa lança no PC e o leitor de código de barras continua funcionando.
   - Limite de comanda (`LIMITE_COMANDA`), por cliente ou por categoria.
   - **Impressão de produção:** ao enviar o pedido, cada item vai para a impressora do seu setor (ESC/POS direto no IP:9100), com fila, nova tentativa automática, reimpressão e alerta se a impressora estiver offline. Opcionalmente há uma tela de cozinha (KDS).
5. **Fechamento com descontos e isenções (novo)**
   - **Pagamento dividido**: vários meios na mesma comanda (ex.: parte PIX, parte cartão) e divisão por pessoa.
   - **Desconto** em % ou R$, por item, por categoria ou no total, sempre com **motivo obrigatório** (lista configurável: cortesia da casa, aniversário, reclamação, parceria...).
   - **Alçada por perfil**: o operador dá até X% e acima disso o gerente autoriza com PIN.
   - **Isenção de taxas** (estacionamento, couvert, taxa de serviço) automática pela categoria do cliente ou manual com autorização.
   - **Consumo interno / funcionário** com preço de custo, substituindo o modo "CUSTO" atual, com trilha de auditoria.
   - **Crédito / valor já pago**: lançado como pagamento antecipado, não como produto negativo.
   - Tudo registrado em **log de auditoria** (quem, quando, quanto e por quê) e em relatório de descontos por operador e por motivo.
6. **NFC-e (integração direta SEFAZ-RS)**
   - Emissão **síncrona no fechamento**, com o DANFE NFC-e impresso no caixa e o QR Code.
   - Todos os itens vendidos entram no cupom (os kits saem pelo valor de venda do kit). O desconto vai em `vDesc` rateado nos itens. Cada meio de pagamento sai com seu `tPag` correto.
   - Numeração transacional (sem `MAX+1`), **contingência offline** automática, **inutilização** de faixas, cancelamento com justificativa digitada e consulta de status.
   - Painel de notas pendentes ou rejeitadas, com o motivo em português e reenvio.
7. **Relatórios e gerencial**: vendas por dia, produto, garçom, área e tipo de entrada; CMV; estoque mínimo/compra (como a `VW_BAR_PRODUTO`); descontos; e o e-mail diário que existe hoje.

### 4.3 Fases sugeridas

| Fase | Entrega |
|---|---|
| 1 | Base (API, banco, login/perfis) + migração de produtos, categorias, kits e garçons + cadastro de clientes com importação |
| 2 | Comanda (caixa + app garçom) + impressão de produção por setor/IP |
| 3 | Fechamento com pagamento dividido, descontos, isenções, alçada e auditoria |
| 4 | NFC-e completa (homologação → produção), contingência e inutilização |
| 5 | Check-in Terra/Rio, eventos/couvert, estoque/inventário, relatórios |
| 6 | Operação em paralelo e desligamento do Forms |

---

## 5. Dados que precisam ser saneados antes do go-live
- NCM/CFOP/CSOSN/PIS-COFINS dos 75 produtos ativos fora do padrão (41 kits, 32 produtos, 2 serviços), **com o contador**.
- Situação das NFC-e sem protocolo: verificar no portal da SEFAZ e **inutilizar** as numerações não usadas.
- Áreas digitadas como texto livre em comandas antigas.

## 6. Decisões em aberto
1. **Banco:** migrar para PostgreSQL (recomendado) ou continuar no Oracle durante a transição?
2. **Certificado A1:** confirmar validade e onde o `.pfx` está hoje (o AIOWS usa).
3. **CSC (token NFC-e):** confirmar ID e token vigentes na SEFAZ-RS.
4. **Taxa de serviço (10%)**: hoje não existe. Deve existir?
5. **Regras de isenção por categoria de cliente:** quais categorias existem e o que cada uma ganha?
