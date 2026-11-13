import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { api, brl, hora, type FormaPagamento, type PagamentoPrevio as Previo } from '../api'

type Props = {
  comandaId: number
  aberta: boolean
  /** mostrar o formulário para registrar um novo pagamento */
  registrando: boolean
  onFechar: () => void
  onMudou: () => void
  onMsg: (m: { tipo: 'ok' | 'erro'; texto: string }) => void
}

/** Pagamentos feitos antes do fechamento (sinal, pacote, PIX adiantado): abatidos ao fechar a conta. */
export default function PagamentoPrevio({ comandaId, aberta, registrando, onFechar, onMudou, onMsg }: Props) {
  const [lista, setLista] = useState<Previo[]>([])
  const [formas, setFormas] = useState<FormaPagamento[]>([])
  const [formaId, setFormaId] = useState<number | null>(null)
  const [valor, setValor] = useState('')
  const [autorizacao, setAutorizacao] = useState('')
  const [obs, setObs] = useState('')
  const [enviando, setEnviando] = useState(false)

  const carregar = useCallback(() => api.pagamentosPrevios(comandaId).then(setLista).catch(() => {}), [comandaId])
  useEffect(() => { carregar() }, [carregar])
  useEffect(() => {
    if (!registrando || formas.length) return
    api.apoioFechamento().then(a => {
      const f = a.formas.filter(x => x.codigo !== 'PRE_PAGO')
      setFormas(f); setFormaId(id => id ?? f[0]?.id ?? null)
    }).catch(e => onMsg({ tipo: 'erro', texto: e.message }))
  }, [registrando, formas.length, onMsg])

  const registrar = async (e: FormEvent) => {
    e.preventDefault()
    const v = Number(valor.replace(',', '.'))
    if (!formaId || !(v > 0)) { onMsg({ tipo: 'erro', texto: 'Informe a forma e o valor pago.' }); return }
    setEnviando(true)
    try {
      await api.registrarPagamentoPrevio(comandaId, { formaPagamentoId: formaId, valor: v, autorizacao: autorizacao || null, observacao: obs || null })
      onMsg({ tipo: 'ok', texto: `Pagamento prévio de ${brl(v)} registrado: será abatido no fechamento.` })
      setValor(''); setAutorizacao(''); setObs(''); onFechar(); carregar(); onMudou()
    } catch (err) {
      onMsg({ tipo: 'erro', texto: (err as Error).message })
    } finally {
      setEnviando(false)
    }
  }

  const remover = async (p: Previo) => {
    if (!window.confirm(`Remover o pagamento prévio de ${brl(p.valor)} (${p.forma})?\nUse só se o valor foi devolvido ou lançado errado.`)) return
    try {
      await api.removerPagamentoPrevio(comandaId, p.id)
      onMsg({ tipo: 'ok', texto: 'Pagamento prévio removido.' }); carregar(); onMudou()
    } catch (err) { onMsg({ tipo: 'erro', texto: (err as Error).message }) }
  }

  if (lista.length === 0 && !registrando) return null
  const formaSel = formas.find(f => f.id === formaId)

  return (
    <div className="cartao">
      <h4 style={{ margin: 0 }}>Pagamento prévio</h4>
      {lista.length > 0 && (
        <table>
          <thead><tr><th>Forma</th><th className="num">Valor</th><th>Quando</th><th>Obs.</th><th></th></tr></thead>
          <tbody>
            {lista.map(p => (
              <tr key={p.id}>
                <td>{p.forma}{p.autorizacao && <small> · NSU {p.autorizacao}</small>}</td>
                <td className="num">{brl(p.valor)}</td>
                <td>{hora(p.criadoEm)}{p.registradoPor && <small> · {p.registradoPor}</small>}</td>
                <td>{p.observacao ?? ''}</td>
                <td>{aberta && <button className="sec" onClick={() => remover(p)}>Remover</button>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {registrando && aberta && (
        <form onSubmit={registrar} className="grade">
          <label>Forma
            <select value={formaId ?? ''} onChange={e => setFormaId(Number(e.target.value) || null)}>
              {formas.map(f => <option key={f.id} value={f.id}>{f.nome}</option>)}
            </select>
          </label>
          <label>Valor pago*
            <input type="number" min="0.01" step="0.01" value={valor} onChange={e => setValor(e.target.value)} required autoFocus />
          </label>
          {formaSel?.aceitaTroco !== 'S' && (
            <label>NSU / autorização<input value={autorizacao} onChange={e => setAutorizacao(e.target.value)} maxLength={40} placeholder="opcional" /></label>
          )}
          <label>Observação<input value={obs} onChange={e => setObs(e.target.value)} maxLength={200} placeholder="ex.: sinal da reserva" /></label>
          <div className="acoes">
            <button type="submit" disabled={enviando}>{enviando ? 'Registrando…' : 'Registrar pagamento'}</button>
            <button type="button" className="sec" onClick={onFechar}>Cancelar</button>
          </div>
        </form>
      )}
    </div>
  )
}
