# JetPOA Bistrô — backup diário (tarefa agendada "JetPOA Backup", 04:00, usuário SYSTEM).
#
# Junta num único .zip tudo o que é preciso para reinstalar em outro computador:
#   bistro.zip        esquema Oracle BISTRO (estrutura + dados), gerado pelo JetPoa.Backup
#   certificado\      .conf (conexão, CSC, senha do certificado) e o certificado A1 (.pfx)
#   app\              programa publicado (o que roda no serviço JetPOABistro)
#   ferramenta\       JetPoa.Backup (para restaurar o banco sem compilar nada)
#   posicao-comandas\ arquivos de posição dos últimos 7 dias
# e envia para o S3 (s3://jetpoa/jetpoa-bistro/). Guarda as últimas 14 cópias também em C:\Projeto\JetPOA\backup.
# Restauração: docs\04-backup.md.
#
# Rodar à mão (PowerShell como Administrador):
#   powershell -NoProfile -ExecutionPolicy Bypass -File C:\Projeto\JetPOA\backup\backup-diario.ps1

param(
    [string]$Raiz = 'C:\Projeto\JetPOA',
    [string]$Certificado = 'C:\Projeto\certificado',
    [string]$S3 = 's3://jetpoa/jetpoa-bistro',
    [int]$ManterLocal = 14
)

$ErrorActionPreference = 'Stop'
$pasta = Join-Path $Raiz 'backup'
$log = Join-Path $pasta 'backup.log'
$status = Join-Path $pasta 'ultimo-backup.json'
$stamp = Get-Date -Format 'yyyy-MM-dd_HHmm'
$trab = Join-Path $pasta "trabalho-$stamp"
$final = Join-Path $pasta "jetpoa-backup-$stamp.zip"
$problemas = New-Object System.Collections.Generic.List[string]

function Log([string]$m) {
    $l = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $m"
    Write-Output $l
    Add-Content -Path $log -Value $l -Encoding UTF8
}

New-Item -ItemType Directory -Force $pasta | Out-Null
Log "==== backup $stamp"
try {
    New-Item -ItemType Directory -Force $trab | Out-Null

    # 1. banco (a máquina virtual do Oracle precisa estar ligada)
    $ferramenta = Join-Path $pasta 'ferramenta\JetPoa.Backup.exe'
    $dump = Join-Path $trab 'bistro.zip'
    & $ferramenta exportar $dump --conf (Join-Path $Certificado '52822987000123.conf') 2>&1 | ForEach-Object { Log "  $_" }
    if ($LASTEXITCODE -ne 0) { $problemas.Add('banco não exportado (Oracle desligado?)') }
    else {
        & $ferramenta verificar $dump 2>&1 | ForEach-Object { Log "  $_" }
        if ($LASTEXITCODE -ne 0) { $problemas.Add('exportação do banco não passou na verificação') }
    }

    # 2. arquivos
    Copy-Item $Certificado (Join-Path $trab 'certificado') -Recurse
    Copy-Item (Join-Path $Raiz 'app') (Join-Path $trab 'app') -Recurse
    Copy-Item (Join-Path $pasta 'ferramenta') (Join-Path $trab 'ferramenta') -Recurse
    Copy-Item $MyInvocation.MyCommand.Path $trab
    $pos = Join-Path $trab 'posicao-comandas'
    New-Item -ItemType Directory -Force $pos | Out-Null
    Get-ChildItem (Join-Path $Raiz 'posicao-comandas') -Filter 'posicao-*.html' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-7) } | Copy-Item -Destination $pos
    Set-Content -Path (Join-Path $trab 'LEIA-ME.txt') -Encoding UTF8 -Value @"
Backup JetPOA Bistrô de $(Get-Date -Format 'dd/MM/yyyy HH:mm') no computador $env:COMPUTERNAME.
Como reinstalar em outro computador: docs\04-backup.md no GitHub colegiokennedypoa/jetpoa.
Contém dados pessoais de clientes, o certificado A1 e senhas: guarde com cuidado.
"@

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($trab, $final, [IO.Compression.CompressionLevel]::Optimal, $false)
    $mb = [math]::Round((Get-Item $final).Length / 1MB, 1)
    Log "  pacote: $final ($mb MB)"

    # 3. fora do computador: S3
    $env:AWS_SHARED_CREDENTIALS_FILE = 'C:\Users\JETPOA-SERVIDOR\.aws\credentials'
    $env:AWS_CONFIG_FILE = 'C:\Users\JETPOA-SERVIDOR\.aws\config'
    $env:AWS_DEFAULT_REGION = 'us-east-2'
    $aws = 'C:\Program Files\Amazon\AWSCLIV2\aws.exe'
    & $aws s3 cp $final "$S3/$(Split-Path $final -Leaf)" --only-show-errors 2>&1 | ForEach-Object { Log "  $_" }
    if ($LASTEXITCODE -ne 0) { $problemas.Add('envio ao S3 falhou') }
    else { Log "  enviado: $S3/$(Split-Path $final -Leaf)" }

    # 4. limpeza local
    Get-ChildItem $pasta -Filter 'jetpoa-backup-*.zip' | Sort-Object Name -Descending | Select-Object -Skip $ManterLocal | Remove-Item -Force
}
catch {
    $problemas.Add($_.Exception.Message)
}
finally {
    if (Test-Path $trab) { Remove-Item $trab -Recurse -Force -ErrorAction SilentlyContinue }
}

$ok = $problemas.Count -eq 0
Log ($(if ($ok) { 'OK' } else { 'PROBLEMA: ' + ($problemas -join '; ') }))
@{ quando = (Get-Date -Format 's'); ok = $ok; arquivo = (Split-Path $final -Leaf); problemas = @($problemas) } |
    ConvertTo-Json | Set-Content -Path $status -Encoding UTF8
if (-not $ok) { exit 1 }
