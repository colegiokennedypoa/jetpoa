/**
 * Impressão do caixa (conferência, DANFE): pelo navegador desta máquina, na impressora padrão.
 * Busca a página e imprime por um iframe oculto, sem trocar de tela.
 * Para imprimir direto, sem a janela de impressão, inicie o Chrome/Edge do caixa com --kiosk-printing.
 */
export async function imprimirPagina(url: string): Promise<void> {
  const r = await fetch(url, { credentials: 'same-origin' })
  const html = await r.text()
  if (!r.ok) {
    const msg = new DOMParser().parseFromString(html, 'text/html').body?.textContent?.trim()
    throw new Error(msg || `Não foi possível imprimir (${r.status}).`)
  }
  await new Promise<void>(resolve => {
    const frame = document.createElement('iframe')
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden'
    frame.onload = () => {
      const w = frame.contentWindow
      const remover = () => setTimeout(() => frame.remove(), 1000)
      w?.addEventListener('afterprint', remover)
      setTimeout(remover, 60000)   // garante a limpeza se o navegador não disparar afterprint
      w?.focus(); w?.print()
      resolve()
    }
    frame.srcdoc = html
    document.body.appendChild(frame)
  })
}
