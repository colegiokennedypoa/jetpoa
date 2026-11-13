# JetPOA Bistrô — Backup e reinstalação

## O que é feito todo dia
A tarefa agendada **JetPOA Backup** roda às **04:00**. Se o computador estiver desligado nessa hora, ela roda assim que ele for ligado.
Ela executa `C:\Projeto\JetPOA\backup\backup-diario.ps1`, que gera **um único arquivo** `jetpoa-backup-AAAA-MM-DD_HHMM.zip` com:

| Dentro do .zip | O que é |
|---|---|
| `bistro.zip` | Esquema Oracle **BISTRO** completo: estrutura (tabelas, índices, chaves, sequências, triggers) e todos os dados. |
| `certificado\` | `52822987000123.conf` (conexão, CSC, senha do certificado) e o certificado A1 `.pfx`. |
| `app\` | O programa publicado que roda no serviço `JetPOABistro`. |
| `ferramenta\` | `JetPoa.Backup.exe`, que restaura o banco sem precisar compilar nada. |
| `posicao-comandas\` | Arquivos de posição das comandas dos últimos 7 dias. |

- **Fora do computador:** o arquivo é enviado para o Amazon S3, em `s3://jetpoa/jetpoa-bistro/` (região us-east-2, criptografado no S3).
- **No computador:** as últimas 14 cópias ficam em `C:\Projeto\JetPOA\backup`.

> O Oracle roda na máquina virtual `kennedy` (ead.vmx) **deste mesmo computador**. Se o computador quebrar, só a cópia do S3 sobra.
> O backup contém dados pessoais de clientes, o certificado A1 e senhas: só quem administra o sistema deve ter acesso ao bucket.

### Como saber se está funcionando
- `C:\Projeto\JetPOA\backup\ultimo-backup.json`: data, `ok` e os problemas do último backup.
- `C:\Projeto\JetPOA\backup\backup.log`: histórico de todas as execuções.
- No S3: `aws s3 ls s3://jetpoa/jetpoa-bistro/`.
- Problemas comuns:
  - "banco não exportado": a máquina virtual do Oracle estava desligada. Os arquivos são copiados mesmo assim.
  - "envio ao S3 falhou": sem internet ou credencial da AWS inválida.

### Instalar ou atualizar a rotina (PowerShell **como Administrador**, na pasta do projeto)
```powershell
dotnet publish tools\JetPoa.Backup\JetPoa.Backup.csproj -c Release -o C:\Projeto\JetPOA\backup\ferramenta
Copy-Item scripts\backup-diario.ps1 C:\Projeto\JetPOA\backup\ -Force
Register-ScheduledTask -TaskName 'JetPOA Backup' -Force -User 'SYSTEM' -RunLevel Highest `
  -Action (New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -ExecutionPolicy Bypass -File C:\Projeto\JetPOA\backup\backup-diario.ps1') `
  -Trigger (New-ScheduledTaskTrigger -Daily -At 04:00) `
  -Settings (New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2))
```
- **Rodar agora**, para testar: `Start-ScheduledTask 'JetPOA Backup'`.
- **Credenciais da AWS:** a tarefa roda como SYSTEM e usa as do usuário `JETPOA-SERVIDOR` (`C:\Users\JETPOA-SERVIDOR\.aws`). Se trocar as chaves da AWS, rode `aws configure` nesse usuário.

---

## Reinstalar em outro computador
1. **Baixar o backup mais recente.** Instale o AWS CLI e configure a mesma credencial com `aws configure`. Depois:
   ```powershell
   aws s3 ls s3://jetpoa/jetpoa-bistro/
   aws s3 cp s3://jetpoa/jetpoa-bistro/jetpoa-backup-<data>.zip C:\Temp\
   ```
   Descompacte em `C:\Temp\restaurar`.
2. **Oracle.** Instale o Oracle XE 11g, ou outro Oracle 11g+. Como SYSTEM, rode `db\00_criar_esquema_bistro.sql`:
   - ajuste o caminho dos datafiles;
   - defina a senha do BISTRO;
   - pule o bloco de *grants* do ADMINISTRATIVO se ele não existir.
3. **Banco.** O esquema BISTRO precisa estar vazio:
   ```powershell
   C:\Temp\restaurar\ferramenta\JetPoa.Backup.exe restaurar C:\Temp\restaurar\bistro.zip --conexao "User Id=bistro;Password=<senha>;Data Source=<host>:1521/XE"
   ```
   A ferramenta faz o seguinte, nesta ordem:
   1. confere o arquivo;
   2. cria as sequências e as tabelas;
   3. grava os dados com os mesmos ids;
   4. cria índices, chaves estrangeiras e triggers;
   5. confere a contagem de linhas de cada tabela.
4. **Configuração.** Copie `certificado\` para `C:\Projeto\certificado`. Em `52822987000123.conf`, ajuste o `Data Source` de `ConnectionStrings__Bistro` se o Oracle mudou de endereço ou de senha.
5. **Programa.**
   - Instale o **ASP.NET Core Runtime 8** (Hosting Bundle).
   - Copie `app\` para `C:\Projeto\JetPOA\app` e `ferramenta\` para `C:\Projeto\JetPOA\backup\ferramenta`.
   - Crie o serviço como no [roteiro de virada](02-roteiro-virada.md), D-7 item 1.
6. **Antes da primeira venda:**
   - **NFC-e: nunca deixe a numeração voltar.** Notas emitidas depois do horário do backup não estão nele. Confira o último número autorizado na tela Fiscal e no portal da SEFAZ-RS. Se o banco restaurado estiver atrás, ajuste `nfce_numeracao` para um número **acima** do último usado. Reaproveitar número gera rejeição por duplicidade.
   - **Comandas do dia:** o que foi lançado depois do backup se perdeu. Use o arquivo de posição mais recente (em `posicao-comandas\`, se ainda estiver acessível) para cobrar ou relançar.
7. **Rotina de backup:** reinstale-a no computador novo, com o bloco "Instalar ou atualizar a rotina" acima.

## Testar a restauração sem mexer no BISTRO
Recomendado uma vez por mês e depois de mudanças grandes no banco. Pede a senha do SYSTEM e faz tudo sozinho:
```powershell
C:\Projeto\JetPOA\backup\ferramenta\JetPoa.Backup.exe testar C:\Projeto\JetPOA\backup\jetpoa-backup-<data>.zip
```
1. Cria o esquema temporário `BISTRO_RESTORE`, nos mesmos tablespaces.
2. Restaura nele o backup e confere a quantidade de linhas.
3. Compara todos os objetos com o BISTRO.
4. Apaga o `BISTRO_RESTORE`.

Termina com **"TESTE DE RESTAURAÇÃO OK."**. Primeiro teste aprovado em 13/11/2026: 26 tabelas, 3.245 linhas e 132 objetos.