# JetPOA Bistrô — Roteiro de virada (Forms → sistema novo)

A tela **Implantação** mostra o checklist ao vivo. Este roteiro diz **quando** e **como** fazer cada item.

---

## Situação em 11/11/2026
| Item | Situação |
|---|---|
| Forms | Último fechamento de comanda e última NFC-e em **28/10/2026**. O job de NFC-e (401) está parado desde então. |
| Rede | Servidor temporariamente em `192.168.0.109`. Impressoras (`.101.50/.55/.60`) e Tomcat AIOWS (`.101.76`) na rede `192.168.101.x` **não respondem**. |
| Certificado A1 | **Vence em 21/11/2026.** Renovar antes da virada. |
| NFC-e no sistema novo | Validada na SEFAZ-RS de **homologação**: autorização, cancelamento e inutilização. |
| Série | **Série 1.** Em produção continua do nº **1213**, depois do último número do Forms. |

---

## D-7 a D-1 · Preparação (sem parar o Forms)
1. **Publicar e instalar o serviço** (PowerShell **como Administrador**, na pasta do projeto):
   ```powershell
   .\scripts\publicar.ps1
   .\scripts\instalar-servico.ps1
   ```
   O sistema fica em `http://<ip-do-servidor>:5080` para caixa, tablets e celulares da rede local.

   > **Execução de scripts bloqueada?** (`...não pode ser carregado porque a execução de scripts foi desabilitada`)
   > É a política padrão do Windows. Em vez de alterá-la, cole os comandos direto no PowerShell **como Administrador**:
   > ```powershell
   > New-Service -Name JetPOABistro -DisplayName 'JetPOA Bistrô' -BinaryPathName '"C:\Projeto\JetPOA\app\JetPoa.Api.exe" --urls http://0.0.0.0:5080' -StartupType Automatic
   > sc.exe failure JetPOABistro reset= 86400 actions= restart/10000/restart/10000/restart/60000
   > New-NetFirewallRule -DisplayName 'JetPOA Bistro 5080' -Direction Inbound -Protocol TCP -LocalPort 5080 -RemoteAddress LocalSubnet -Action Allow
   > Start-Service JetPOABistro
   > ```
   > Para publicar sem o script: `cd web; npm run build; cd ..` e depois `dotnet publish src\JetPoa.Api\JetPoa.Api.csproj -c Release -o C:\Projeto\JetPOA\app`.
2. **Usuários** (tela Usuários): Administrador → Gerentes → Caixas → Garçons/Portaria. Cada um recebe uma senha provisória e troca no primeiro acesso. Gerentes cadastram também um PIN de autorização.
3. **Impressoras** (tela Produção): conferir o IP de Cozinha, Bar e Garçom e usar "Imprimir teste" em cada uma. O **Caixa** (DANFE e conferência) imprime na impressora padrão do Windows do computador do caixa: deixe a térmica como padrão e, de preferência, abra o navegador com `--kiosk-printing`.
   - Se a Bematech imprimir caracteres estranhos, colocar a impressora em modo **ESC/POS** (menu da impressora ou utilitário Bematech).
4. **Contador** (tela Produtos → "Só com pendência fiscal"): completar NCM, CFOP, CSOSN e CST dos produtos pendentes (77 em 11/11).
5. **Clientes** (tela Implantação → "Prévia: importar clientes" → "Importar"): cerca de 2.545 clientes do mailing, reservas e comandas antigas, sem duplicar. Ninguém entra como "aceita comunicação": o consentimento LGPD é colhido no check-in.
6. **Eventos** (Check-in → Eventos): cadastrar os próximos eventos com taxas de terra/rio e couvert.
7. **Treino em homologação** (`Nfce__Ambiente=2`): abrir, lançar, imprimir na produção, fechar com desconto e emitir NFC-e de teste. **Notas de homologação não têm valor fiscal.**

## D0 · Virada (com o bistrô fechado)
1. **Forms**:
   - fechar ou cancelar todas as comandas abertas no Forms (checklist "Comandas abertas no Forms" = 0);
   - parar o job de NFC-e do Forms, como DBA: `exec dbms_job.broken(401, true); commit;`
   - tirar o atalho do Forms das máquinas do caixa.
2. **Sincronizar produtos** (tela Implantação): traz preços e produtos novos que tenham sido mudados no Forms.
3. **NFC-e em produção**: em `C:\Projeto\certificado\52822987000123.conf`, ajustar e reiniciar o serviço (`Restart-Service JetPOABistro`):
   ```ini
   Nfce__Ambiente=1
   Nfce__Serie=1
   Nfce__CscId=<ID do CSC de PRODUÇÃO>
   Nfce__Csc=<CSC de PRODUÇÃO>
   ```
4. **Checklist** (Implantação → "Atualizar + testar SEFAZ"): nenhum item "Pendente".
5. **Primeira venda real**: valor baixo, conferir a NFC-e **AUTORIZADA** na tela Fiscal e o DANFE impresso.

## D+1 em diante · Acompanhamento
- **Fiscal** (diário): comandas sem NFC-e, rejeitadas e contingências. As contingências são transmitidas sozinhas a cada minuto.
- **Relatórios**: conferir o fechamento do dia (forma de pagamento × maquininha/PIX).
- **Pendência herdada do Forms**: inutilizar os números da série 1 que o Forms nunca autorizou (583 registros sem protocolo), pela tela Fiscal → Inutilizar. A tela recusa números autorizados.
- **Atualizações do sistema**: `.\scripts\atualizar.ps1`. Ele compila e testa antes de parar o serviço e guarda a versão anterior.

## Plano de volta (rollback)
Se algo impedir a operação no D0:
1. **Cobrar com o sistema parado**: abra `C:\Projeto\JetPOA\posicao-comandas\posicao-<data de hoje>.html` no navegador. Ele mostra cada comanda aberta com itens e valor a pagar, no retrato dos últimos 2 minutos antes da parada.
2. **NFC-e**: o sistema novo continua fechando comandas mesmo sem NFC-e. As notas pendentes ficam na tela Fiscal para emitir depois.
3. **Voltar ao Forms** (último recurso):
   - parar o serviço: `Stop-Service JetPOABistro`;
   - reativar o job: `exec dbms_job.broken(401, false); commit;`
   - o Forms continua a numeração do maior número da própria tabela. **Antes de voltar**, confira se o sistema novo emitiu notas de produção: elas estão em `BISTRO.NFCE` (série 1, ambiente 1). Se houver, avise o responsável técnico, porque o Forms tentaria reutilizar esses números.
4. **Voltar versão do sistema novo**: ver o cabeçalho de `scripts\atualizar.ps1`.

## Onde fica cada coisa
| O quê | Onde |
|---|---|
| Código | GitHub `colegiokennedypoa/jetpoa` |
| Programa instalado | `C:\Projeto\JetPOA\app` (serviço `JetPOABistro`, porta 5080) |
| Posição das comandas (contingência) | `C:\Projeto\JetPOA\posicao-comandas` (um arquivo por dia) |
| Configuração e segredos | `C:\Projeto\certificado\52822987000123.conf` (fora do Git) |
| Certificado A1 | `C:\Projeto\certificado\52822987000123.pfx` |
| Banco | Oracle XE `192.168.0.109:1521/XE`, esquema `BISTRO` (o Forms continua no `ADMINISTRATIVO`) |
| Scripts do banco | `db\00` a `db\13` (ordem no README) |
| Guia do desenvolvedor | [`docs\03-guia-desenvolvedor.md`](03-guia-desenvolvedor.md) |
