# JetPOA · Bistrô (novo sistema)

Substitui o sistema Forms 6i do bistrô. Análise e proposta: [docs/01-analise-e-proposta.md](docs/01-analise-e-proposta.md).

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

## Fechamento (Fase 3)
- **Primeiro uso:** na tela **Usuários**, crie o Administrador. Os demais usuários (Gerente, Caixa…) exigem o PIN de um administrador.
- **Perfis:** Administrador (desconto até 100%, autoriza), Gerente (até 30%, autoriza), Caixa (até 5%), Garçom e Check-in (0%). Os limites ficam na tabela `perfil`.
- **Fechar conta:** o operador escolhe o nome e digita o PIN. Se o desconto passar do limite dele, ou se for consumo interno, a tela pede o PIN de quem autoriza.
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
# conexão (não fica salva em arquivo)
$env:ConnectionStrings__Bistro = "User Id=bistro;Password=<senha>;Data Source=192.168.0.109:1521/XE;"
dotnet run --project src/JetPoa.Api        # API em http://localhost:5080  (Swagger em /swagger)
cd web; npm run dev                        # front em http://localhost:5173 (proxy /api → 5080)
```

## Publicar (servidor)
```powershell
cd web; npm run build; cd ..
dotnet publish src/JetPoa.Api -c Release -o publish
```
