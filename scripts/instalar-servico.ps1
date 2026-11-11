<#
  Instala o JetPOA Bistrô como serviço do Windows (executar UMA vez, em PowerShell COMO ADMINISTRADOR).
  - Serviço "JetPOABistro", início automático, reinicia sozinho se cair.
  - Libera a porta 5080 no firewall SOMENTE para a rede local (tablets/celulares dos garçons e caixas).
  Antes: rode .\scripts\publicar.ps1
#>
param(
    [string]$Pasta = 'C:\Projeto\JetPOA\app',
    [int]$Porta = 5080
)
$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Execute este script em um PowerShell aberto como Administrador.' }

$exe = Join-Path $Pasta 'JetPoa.Api.exe'
if (-not (Test-Path $exe)) { throw "Não encontrei $exe. Rode antes .\scripts\publicar.ps1" }

if (Get-Service JetPOABistro -ErrorAction SilentlyContinue) {
    Write-Host 'Serviço já existe: use atualizar.ps1 para publicar nova versão.' -ForegroundColor Yellow
} else {
    New-Service -Name 'JetPOABistro' -DisplayName 'JetPOA Bistrô (comandas, produção e NFC-e)' `
        -Description 'Sistema do bistrô JetPOA: API + telas web na porta 5080.' `
        -BinaryPathName "`"$exe`" --urls http://0.0.0.0:$Porta" -StartupType Automatic | Out-Null
    # reinicia em 10s se cair (3 tentativas; zera contagem após 1 dia)
    sc.exe failure JetPOABistro reset= 86400 actions= restart/10000/restart/10000/restart/60000 | Out-Null
    Write-Host 'Serviço JetPOABistro criado.'
}

if (-not (Get-NetFirewallRule -DisplayName 'JetPOA Bistro 5080' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'JetPOA Bistro 5080' -Direction Inbound -Protocol TCP -LocalPort $Porta `
        -RemoteAddress LocalSubnet -Action Allow -Profile Any | Out-Null
    Write-Host "Firewall: porta $Porta liberada só para a rede local."
}

Start-Service JetPOABistro
Start-Sleep 5
try {
    $h = Invoke-RestMethod "http://127.0.0.1:$Porta/api/health" -TimeoutSec 10
    Write-Host "OK: serviço no ar · Oracle $($h.banco)" -ForegroundColor Green
    $ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notmatch 'VMware|Loopback' } | Select-Object -First 1).IPAddress
    Write-Host "Acesse nos tablets/celulares: http://${ip}:$Porta"
} catch {
    Write-Host 'O serviço iniciou mas não respondeu. Veja o Visualizador de Eventos > Aplicativo (fonte JetPOABistro).' -ForegroundColor Red
}
