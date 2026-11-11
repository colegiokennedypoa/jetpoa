import { useCallback, useEffect, useState } from 'react'
import { api, type ItemChecklist } from '../api'
import SeletorUsuario from '../components/SeletorUsuario'

const ICONE: Record<ItemChecklist['status'], [string, string]> = {
  ok: ['OK', 'tag ok'], aviso: ['Atenção', 'tag pend'], erro: ['Pendente', 'tag erro'], manual: ['Manual', 'tag pend'], info: ['Info', 'tag'],
}

/** Checklist da virada Forms → sistema novo e ferramentas de importação. */
export default function Implantacao() {
  const [itens, setItens] = useState<ItemChecklist[]>([])
  const [carregando, setCarregando] = useState(false)
  const [usuarioId, setUsuarioId] = useState<number | null>(null)
  const [pin, setPin] = useState('')
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const [previa, setPrevia] = useState<{ tipo: 'clientes' | 'produtos'; dados: any } | null>(null)
  const [msg, setMsg] = useState<{ tipo: 'ok' | 'erro'; texto: string }>()

  const carregar = useCallback((sefaz: boolean) => {
    setCarregando(true)
    api.checklist(sefaz).then(setItens).catch(e => setMsg({ tipo: 'erro', texto: e.message })).finally(() => setCarregando(false))
  }, [])
  useEffect(() => { carregar(false) }, [carregar])

  const rodar = async (tipo: 'clientes' | 'produtos', aplicar: boolean) => {
    if (!usuarioId) { setMsg({ tipo: 'erro', texto: 'Informe o Administrador e o PIN.' }); return }
    try {
      const dados = tipo === 'clientes' ? await api.importarClientes(aplicar, usuarioId, pin) : await api.sincronizarProdutos(aplicar, usuarioId, pin)
      setPrevia({ tipo, dados })
      if (aplicar) { setMsg({ tipo: 'ok', texto: tipo === 'clientes' ? `${dados.novos} clientes importados.` : 'Produtos sincronizados.' }); carregar(false) }
    } catch (e) { setMsg({ tipo: 'erro', texto: (e as Error).message }) }
  }

  const grupos = [...new Set(itens.map(i => i.grupo))]
  const pendentes = itens.filter(i => i.status === 'erro').length

  return (
    <section>
      <div className="filtros">
        <h3 style={{ margin: 0 }}>Checklist da virada</h3>
        <button className="sec" onClick={() => carregar(false)} disabled={carregando}>Atualizar</button>
        <button className="sec" onClick={() => carregar(true)} disabled={carregando}>Atualizar + testar SEFAZ</button>
        <span className="contador">{carregando ? 'verificando…' : pendentes ? `${pendentes} pendência(s) bloqueante(s)` : 'sem pendências bloqueantes'}</span>
      </div>
      {msg && <p className={msg.tipo}>{msg.texto}</p>}

      <table>
        <tbody>
          {grupos.map(g => itens.filter(i => i.grupo === g).map((i, k) => (
            <tr key={g + k}>
              <td style={{ width: 110 }}>{k === 0 ? <b>{g}</b> : ''}</td>
              <td style={{ width: 90 }}><span className={ICONE[i.status][1]}>{ICONE[i.status][0]}</span></td>
              <td>{i.titulo}</td>
              <td className="detalhe-erro" style={{ maxWidth: 600 }}>{i.detalhe}</td>
            </tr>
          )))}
        </tbody>
      </table>

      <h3>Importações do sistema antigo (Forms)</h3>
      <div className="cartao">
        <p className="vazio">Sempre veja a <b>prévia</b> antes de aplicar. Nada é alterado no Forms; só o sistema novo é gravado.</p>
        <div className="grade"><SeletorUsuario usuarioId={usuarioId} pin={pin} onUsuario={setUsuarioId} onPin={setPin} /></div>
        <div className="acoes">
          <button className="sec" onClick={() => rodar('clientes', false)}>Prévia: importar clientes</button>
          <button className="sec" onClick={() => rodar('produtos', false)}>Prévia: sincronizar produtos</button>
        </div>
      </div>

      {previa?.tipo === 'clientes' && (
        <div className="cartao">
          <h4>{previa.dados.aplicado ? 'Clientes importados' : 'Prévia da importação de clientes'}</h4>
          <p>{previa.dados.registrosLidos} registros lidos (mailing, reservas, pedidos e comandas) →
            <b> {previa.dados.clientesUnicos}</b> pessoas únicas · {previa.dados.jaCadastrados} já cadastradas ·
            <b> {previa.dados.novos} novas</b> ({previa.dados.comCpf} com CPF, {previa.dados.comEmail} com e-mail).</p>
          <p className="vazio">Ignorados: {previa.dados.semCpfNemCelular} sem CPF nem celular, {previa.dados.semNome} sem nome válido.
            Telefones com 9 dígitos recebem DDD 51. Ninguém é marcado como aceitando comunicação (LGPD): o consentimento deve ser colhido no próximo check-in.</p>
          {!previa.dados.aplicado && previa.dados.novos > 0 &&
            <button onClick={() => { if (window.confirm(`Importar ${previa.dados.novos} clientes?`)) rodar('clientes', true) }}>Importar {previa.dados.novos} clientes</button>}
        </div>
      )}
      {previa?.tipo === 'produtos' && (
        <div className="cartao">
          <h4>{previa.dados.aplicado ? 'Produtos sincronizados' : 'Prévia da sincronização de produtos'}</h4>
          <p>{previa.dados.novos.length} produto(s) novo(s) no Forms · {previa.dados.precosDiferentes.length} preço(s) diferente(s) ·
            {' '}{previa.dados.situacaoDiferente.length} ativo/inativo diferente(s) · {previa.dados.fiscalPreenchivel} com dados fiscais a completar.</p>
          {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
          {previa.dados.precosDiferentes.slice(0, 15).map((p: any) => <div key={p.id} className="obs">{p.nome}: sistema novo R$ {p.sistemaNovo} → Forms R$ {p.forms}</div>)}
          <p className="vazio">A sincronização usa o Forms como referência de preço e ativo/inativo. Dados fiscais só são preenchidos onde o sistema novo está vazio.</p>
          {!previa.dados.aplicado && (previa.dados.novos.length + previa.dados.precosDiferentes.length + previa.dados.situacaoDiferente.length + previa.dados.fiscalPreenchivel) > 0 &&
            <button onClick={() => rodar('produtos', true)}>Aplicar sincronização</button>}
        </div>
      )}
    </section>
  )
}
