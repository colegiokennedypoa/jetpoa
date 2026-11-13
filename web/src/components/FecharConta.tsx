import { useEffect, useMemo, useState } from 'react'
import {
  api, brl, danfeParaImprimir,
  type Apoio, type ComandaItem, type ComandaResumo, type DescontoManual, type Pagamento, type PedidoFechamento, type Simulacao,
} from '../api'
import { useSessao } from '../sessao'
import { imprimirPagina } from '../imprimir'
import { erroCpfCnpj, mascaraCpfCnpj, soDigitos } from '../documento'

type Props = {
  comanda: ComandaResumo
  itens: ComandaItem[]
  onFechada: (msg: string) => void
  onVoltar: () => void
}

const TAXAS: Record<string, string> = { ESTACIONAMENTO: 'Estacionamento', EMBARQUE: 'Embarque', COUVERT: 'Couvert' }
const r2 = (v: number) => Math.round(v * 100) / 100

export default function FecharConta({ comanda, itens, onFechada, onVoltar }: Props) {
  const ativos = useMemo(() => itens.filter(i => i.situacao === 'A'), [itens])
  const [apoio, setApoio] = useState<Apoio>()
  const [pedido, setPedido] = useState<PedidoFechamento>({ consumoInterno: false, isentarTaxas: [], descontos: [], taxaServicoPct: null })
  const [sim, setSim] = useState<Simulacao>()
  const { eu } = useSessao()
  const [autorizadorId, setAutorizadorId] = useState<number | null>(null)
  const [autorizadorPin, setAutorizadorPin] = useState('')
  const [pagamentos, setPagamentos] = useState<Pagamento[]>([])
  const [cpfNota, setCpfNota] = useState(mascaraCpfCnpj(comanda.clienteCpf))
  const docErro = erroCpfCnpj(cpfNota)
  const [erro, setErro] = useState<string>()
  const [enviando, setEnviando] = useState(false)

  useEffect(() => { api.apoioFechamento().then(setApoio).catch(e => setErro(e.message)) }, [])

  // recalcula a cada mudança (o servidor é a fonte da verdade)
  useEffect(() => {
    const t = setTimeout(() => {
      api.simular(comanda.id, pedido).then(s => { setSim(s); setErro(undefined) }).catch(e => setErro(e.message))
    }, 200)
    return () => clearTimeout(t)
  }, [comanda.id, pedido])

  const calc = sim?.calculo
  const total = calc?.total ?? 0
  const pago = r2(pagamentos.reduce((s, p) => s + (p.valor || 0), 0))
  const falta = r2(total - pago)
  const troco = r2(pagamentos.reduce((s, p) => s + (p.valorRecebido != null ? Math.max(0, p.valorRecebido - p.valor) : 0), 0))
  const taxasNaComanda = [...new Set(ativos.map(i => i.taxaTipo).filter((t): t is string => !!t))]
  const isentasAuto = new Set((calc?.descontos ?? []).filter(d => d.origem === 'CATEGORIA' && d.taxaTipo).map(d => d.taxaTipo))
  const autorizadores = (apoio?.autorizadores ?? []).filter(u => u.id !== eu.id)

  const setDesc = (k: number, d: Partial<DescontoManual>) =>
    setPedido(p => ({ ...p, descontos: p.descontos.map((x, i) => (i === k ? { ...x, ...d } : x)) }))
  const addDesc = () => setPedido(p => ({
    ...p, descontos: [...p.descontos, { escopo: 'TOTAL', itemId: null, categoriaId: null, tipo: 'P', valor: 10, motivoId: apoio?.motivos[0]?.id ?? null, observacao: null }],
  }))
  const delDesc = (k: number) => setPedido(p => ({ ...p, descontos: p.descontos.filter((_, i) => i !== k) }))
  const toggleTaxa = (t: string) => setPedido(p => ({
    ...p, isentarTaxas: p.isentarTaxas.includes(t) ? p.isentarTaxas.filter(x => x !== t) : [...p.isentarTaxas, t],
  }))

  const addPagamento = (formaId: number, valor = Math.max(falta, 0)) =>
    setPagamentos(ps => [...ps, { formaPagamentoId: formaId, valor: r2(valor), valorRecebido: null, autorizacao: null }])
  const setPag = (k: number, p: Partial<Pagamento>) => setPagamentos(ps => ps.map((x, i) => (i === k ? { ...x, ...p } : x)))
  const dividir = (n: number, formaId: number) => {
    const parte = Math.floor((total / n) * 100) / 100
    setPagamentos(Array.from({ length: n }, (_, i) => ({
      formaPagamentoId: formaId, valor: i === n - 1 ? r2(total - parte * (n - 1)) : parte, valorRecebido: null, autorizacao: null,
    })))
  }

  const confirmar = async () => {
    setEnviando(true); setErro(undefined)
    try {
      const r = await api.fechar(comanda.id, {
        pedido,
        autorizadorId: sim?.exigeAutorizacao ? autorizadorId : null,
        autorizadorPin: sim?.exigeAutorizacao ? autorizadorPin : null,
        pagamentos, cpfCnpjNota: soDigitos(cpfNota) || null,
      })
      const nf = r.nfce
      const nota = !nf ? ''
        : nf.situacao === 'AUTORIZADA' ? ` NFC-e ${nf.numero} autorizada.`
        : nf.situacao === 'CONTINGENCIA' ? ` NFC-e ${nf.numero} em contingência (será transmitida).`
        : ` NFC-e: ${nf.mensagem ?? nf.situacao}`
      // DANFE: impressora padrão desta máquina
      const danfe = danfeParaImprimir(nf)
      let erroDanfe = ''
      if (danfe) await imprimirPagina(`/api/fiscal/notas/${danfe}/danfe`).catch(e => { erroDanfe = ` DANFE não impresso: ${(e as Error).message}` })
      onFechada(`Comanda ${comanda.numero} fechada: ${brl(r.total)}${r.troco > 0 ? ` · TROCO ${brl(r.troco)}` : ''}.${nota}${erroDanfe}`)
    } catch (e) {
      setErro((e as Error).message)
    } finally {
      setEnviando(false)
    }
  }

  const formaNome = (id: number) => apoio?.formas.find(f => f.id === id)
  const motivo = (id: number | null) => apoio?.motivos.find(m => m.id === id)

  return (
    <div className="fechamento">
      <div className="fech-col">
        <h4>Descontos e isenções</h4>
        <label className="check"><input type="checkbox" checked={pedido.consumoInterno}
          onChange={e => setPedido(p => ({ ...p, consumoInterno: e.target.checked }))} /> Consumo interno (preço de custo · exige gerente)</label>

        {!pedido.consumoInterno && taxasNaComanda.length > 0 && (
          <div className="taxas">
            {taxasNaComanda.map(t => {
              const auto = isentasAuto.has(t)
              return (
                <label key={t} className="check">
                  <input type="checkbox" checked={auto || pedido.isentarTaxas.includes(t)} disabled={auto} onChange={() => toggleTaxa(t)} />
                  {' '}Isentar {TAXAS[t] ?? t}{auto && <small> (automático pela categoria)</small>}
                </label>
              )
            })}
          </div>
        )}

        {!pedido.consumoInterno && pedido.descontos.map((d, k) => (
          <div key={k} className="desc-linha">
            <select value={d.escopo === 'ITEM' ? `I${d.itemId}` : 'TOTAL'} onChange={e => {
              const v = e.target.value
              setDesc(k, v === 'TOTAL' ? { escopo: 'TOTAL', itemId: null } : { escopo: 'ITEM', itemId: Number(v.slice(1)) })
            }}>
              <option value="TOTAL">Total da conta</option>
              {ativos.filter(i => !i.taxaTipo).map(i => <option key={i.id} value={`I${i.id}`}>{i.quantidade}x {i.produto}</option>)}
            </select>
            <span className="segmentado mini">
              <button type="button" className={d.tipo === 'P' ? 'ativo' : ''} onClick={() => setDesc(k, { tipo: 'P' })}>%</button>
              <button type="button" className={d.tipo === 'V' ? 'ativo' : ''} onClick={() => setDesc(k, { tipo: 'V' })}>R$</button>
            </span>
            <input className="valor" type="number" min="0" step="0.01" value={d.valor} onChange={e => setDesc(k, { valor: Number(e.target.value) })} />
            <select value={d.motivoId ?? ''} onChange={e => setDesc(k, { motivoId: Number(e.target.value) || null })}>
              {apoio?.motivos.map(m => <option key={m.id} value={m.id}>{m.nome}</option>)}
            </select>
            <input placeholder={motivo(d.motivoId)?.exigeObservacao === 'S' ? 'Observação (obrigatória)' : 'Observação'}
              value={d.observacao ?? ''} onChange={e => setDesc(k, { observacao: e.target.value || null })} />
            <button className="sec" onClick={() => delDesc(k)} aria-label="Remover desconto">✕</button>
          </div>
        ))}
        {!pedido.consumoInterno && <button className="sec" onClick={addDesc}>+ Desconto</button>}

        <label className="linha-campo">Taxa de serviço %
          <input type="number" min="0" max="20" step="0.5" disabled={pedido.consumoInterno}
            value={pedido.taxaServicoPct ?? calc?.taxaServicoPct ?? 0}
            onChange={e => setPedido(p => ({ ...p, taxaServicoPct: e.target.value === '' ? null : Number(e.target.value) }))} />
        </label>
        <label className="linha-campo">CPF/CNPJ na nota
          <input value={cpfNota} onChange={e => setCpfNota(mascaraCpfCnpj(e.target.value))} inputMode="numeric" placeholder="opcional"
            maxLength={18} className={docErro ? 'invalido' : ''} aria-invalid={!!docErro} />
        </label>
        {docErro && <small className="erro-campo">{docErro}</small>}
      </div>

      <div className="fech-col">
        <h4>Resumo</h4>
        {calc && (
          <table className="resumo">
            <tbody>
              <tr><td>Subtotal</td><td className="num">{brl(calc.subtotal)}</td></tr>
              {calc.descontos.map((d, k) => <tr key={k} className="desc"><td>{d.descricao}</td><td className="num">− {brl(d.valor)}</td></tr>)}
              {calc.taxaServico > 0 && <tr><td>Taxa de serviço {calc.taxaServicoPct}%</td><td className="num">+ {brl(calc.taxaServico)}</td></tr>}
              <tr className="tot"><td>Total</td><td className="num">{brl(calc.total)}</td></tr>
            </tbody>
          </table>
        )}
        {calc && calc.descontoConcedido > 0 && (
          <p className={sim?.exigeAutorizacao ? 'alerta-box' : 'ok'}>
            Desconto concedido: {brl(calc.descontoConcedido)} ({calc.descontoConcedidoPct}%)
            {sim?.limiteOperadorPct != null && ` · limite do operador ${sim.limiteOperadorPct}%`}
          </p>
        )}
        {calc?.erros.map((e, k) => <p key={k} className="erro">{e}</p>)}

        <h4>Pagamento</h4>
        <div className="formas">
          {apoio?.formas.map(f => <button key={f.id} className="sec" disabled={falta <= 0} onClick={() => addPagamento(f.id)}>{f.nome}</button>)}
        </div>
        {pagamentos.map((p, k) => {
          const f = formaNome(p.formaPagamentoId)
          return (
            <div key={k} className="pag-linha">
              <span>{f?.nome}</span>
              <input className="valor" type="number" min="0" step="0.01" value={p.valor} onChange={e => setPag(k, { valor: Number(e.target.value) })} />
              {f?.aceitaTroco === 'S'
                ? <input className="valor" type="number" min="0" step="0.01" placeholder="Recebido" value={p.valorRecebido ?? ''}
                    onChange={e => setPag(k, { valorRecebido: e.target.value === '' ? null : Number(e.target.value) })} />
                : <input className="valor" placeholder="NSU (opc.)" value={p.autorizacao ?? ''} onChange={e => setPag(k, { autorizacao: e.target.value || null })} />}
              <button className="sec" onClick={() => setPagamentos(ps => ps.filter((_, i) => i !== k))} aria-label="Remover pagamento">✕</button>
            </div>
          )
        })}
        <div className="dividir">
          Dividir em
          {[2, 3, 4, 5].map(n => <button key={n} className="sec" disabled={!apoio || total <= 0}
            onClick={() => dividir(n, pagamentos[0]?.formaPagamentoId ?? apoio!.formas[0].id)}>{n}</button>)}
        </div>
        <p className={falta === 0 ? 'ok' : 'erro'}>
          {falta > 0 ? `Falta ${brl(falta)}` : falta < 0 ? `Excede ${brl(-falta)}` : 'Pagamento completo'}
          {troco > 0 && <b> · TROCO {brl(troco)}</b>}
        </p>
      </div>

      <div className="fech-col">
        <h4>Confirmação</h4>
        <p className="obs">Caixa: <b>{eu.nome}</b> ({eu.perfil})</p>
        {sim?.exigeAutorizacao && (
          <div className="autorizacao">
            <b>{calc?.consumoInterno ? 'Consumo interno' : 'Desconto acima do limite'}: autorização do gerente</b>
            <select value={autorizadorId ?? ''} onChange={e => setAutorizadorId(Number(e.target.value) || null)}>
              <option value="">Quem autoriza…</option>
              {autorizadores.map(u => <option key={u.id} value={u.id}>{u.nome} (até {u.limiteDescontoPct}%)</option>)}
            </select>
            <input type="password" inputMode="numeric" autoComplete="off" placeholder="PIN do autorizador" value={autorizadorPin}
              onChange={e => setAutorizadorPin(e.target.value.replace(/\D/g, ''))} />
          </div>
        )}
        {erro && <p className="erro">{erro}</p>}
        <button className="enviar" disabled={enviando || !calc || falta !== 0 || !!docErro || (!!sim?.exigeAutorizacao && (!autorizadorId || autorizadorPin.length < 4))} onClick={confirmar}>
          {enviando ? 'Fechando…' : `Fechar conta · ${brl(total)}`}
        </button>
        <button className="sec" onClick={onVoltar}>Voltar</button>
      </div>
    </div>
  )
}
