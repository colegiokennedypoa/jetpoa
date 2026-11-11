import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, brl, gravarLocal, lerLocal, P, type Area, type Cliente, type Evento, type ProdutoResumo } from '../api'
import BuscaCliente from '../components/BuscaCliente'
import { useSessao } from '../sessao'

const CHAVE_ENTRADA = 'jetpoa.checkin.entrada'
type Msg = { tipo: 'ok' | 'erro'; texto: string }

/** Portaria TERRA / RIO: check-in (abre comanda + taxas do evento) e check-out. */
export default function Checkin() {
  const { pode } = useSessao()
  const [entrada, setEntrada] = useState<'T' | 'R'>(() => (lerLocal(CHAVE_ENTRADA) === 'R' ? 'R' : 'T'))
  const [evento, setEvento] = useState<Evento | null>(null)
  const [presentes, setPresentes] = useState<{ entrada: string; quantidade: number }[]>([])
  const [modo, setModo] = useState<'entrada' | 'saida' | 'eventos'>('entrada')
  const [msg, setMsg] = useState<Msg>()

  const atualizar = () => {
    api.eventoHoje().then(setEvento).catch(() => {})
    api.presentes().then(setPresentes).catch(() => {})
  }
  useEffect(atualizar, [])
  const escolher = (e: 'T' | 'R') => { setEntrada(e); gravarLocal(CHAVE_ENTRADA, e) }
  const qtd = (e: string) => presentes.find(p => p.entrada === e)?.quantidade ?? 0

  return (
    <section className="garcom">
      <div className="filtros">
        <span className="segmentado">
          <button className={entrada === 'T' ? 'ativo' : ''} onClick={() => escolher('T')}>Portaria TERRA</button>
          <button className={entrada === 'R' ? 'ativo' : ''} onClick={() => escolher('R')}>Portaria RIO</button>
        </span>
        <span className="segmentado mini">
          <button className={modo === 'entrada' ? 'ativo' : ''} onClick={() => setModo('entrada')}>Entrada</button>
          <button className={modo === 'saida' ? 'ativo' : ''} onClick={() => setModo('saida')}>Saída</button>
          {pode(P.eventoEditar) && <button className={modo === 'eventos' ? 'ativo' : ''} onClick={() => setModo('eventos')}>Eventos</button>}
        </span>
        <span className="contador">No local hoje: Terra {qtd('T')} · Rio {qtd('R')}</span>
      </div>
      {evento
        ? <p className="alerta-box"><b>{evento.nome}</b> · {entrada === 'T'
            ? (evento.produtoTerra ? `${evento.produtoTerra} ${brl(evento.valorTerra ?? 0)}` : 'sem taxa de terra')
            : (evento.produtoRio ? `${evento.produtoRio} ${brl(evento.valorRio ?? 0)}` : 'sem taxa de rio')}
            {evento.couvertProduto && ` · couvert ${brl(evento.valorCouvert ?? 0)} até ${new Date(evento.couvertFim!).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}`}</p>
        : <p className="vazio">Sem evento hoje: nenhuma taxa automática.</p>}
      {msg && <p className={msg.tipo}>{msg.texto}</p>}

      {modo === 'entrada' && <Entrada entrada={entrada} onMsg={m => { setMsg(m); atualizar() }} />}
      {modo === 'saida' && <Saida onMsg={m => { setMsg(m); atualizar() }} />}
      {modo === 'eventos' && <Eventos onMudou={atualizar} onMsg={setMsg} />}
    </section>
  )
}

function Entrada({ entrada, onMsg }: { entrada: 'T' | 'R'; onMsg: (m: Msg) => void }) {
  const [areas, setAreas] = useState<Area[]>([])
  const [numero, setNumero] = useState('')
  const [areaId, setAreaId] = useState<number | null>(null)
  const [cliente, setCliente] = useState<Cliente | null>(null)
  const [enviando, setEnviando] = useState(false)

  useEffect(() => { api.areas().then(setAreas).catch(() => {}) }, [])

  const campoNumero = useRef<HTMLInputElement>(null)
  // pronto para o próximo: limpa pulseira, área e cliente e volta o cursor para a pulseira
  const limpar = () => { setNumero(''); setCliente(null); setAreaId(null); campoNumero.current?.focus() }

  const enviar = async (e: FormEvent) => {
    e.preventDefault()
    if (!cliente) { onMsg({ tipo: 'erro', texto: 'Selecione ou cadastre o cliente antes do check-in.' }); return }
    setEnviando(true)
    try {
      const r = await api.checkin({
        numero: Number(numero), entradaTipo: entrada, areaId, clienteId: cliente.id,
        novoCliente: null, identificacao: null, observacao: null,
      })
      onMsg({ tipo: 'ok', texto: `Comanda ${r.numero} aberta para ${r.cliente} (${r.entrada === 'R' ? 'Rio' : 'Terra'})` + (r.taxas.length ? ` · lançado: ${r.taxas.map(t => `${t.nome} ${brl(t.valor)}`).join(', ')}` : '') })
      limpar()
    } catch (err) {
      onMsg({ tipo: 'erro', texto: (err as Error).message })
    } finally { setEnviando(false) }
  }

  return (
    <form className="cartao" onSubmit={enviar}>
      <div className="grade">
        <label>Nº da pulseira / comanda*
          <input ref={campoNumero} className="numero-grande" value={numero} onChange={e => setNumero(e.target.value.replace(/\D/g, ''))} inputMode="numeric" required autoFocus />
        </label>
        <label>Área
          <select value={areaId ?? ''} onChange={e => setAreaId(Number(e.target.value) || null)}>
            <option value="">—</option>
            {areas.map(a => <option key={a.id} value={a.id}>{a.codigo} · {a.nome}</option>)}
          </select>
        </label>
        <div className="largo campo">
          <span className="rotulo">Cliente* (obrigatório)</span>
          <BuscaCliente cliente={cliente} onCliente={setCliente} />
        </div>
      </div>
      <div className="acoes">
        <button type="submit" disabled={enviando || !cliente}>{enviando ? 'Abrindo…' : `Check-in ${entrada === 'R' ? 'RIO' : 'TERRA'}`}</button>
      </div>
    </form>
  )
}

function Saida({ onMsg }: { onMsg: (m: Msg) => void }) {
  const [numero, setNumero] = useState('')
  const enviar = async (e: FormEvent) => {
    e.preventDefault()
    try {
      const r = await api.checkout(Number(numero))
      onMsg({ tipo: 'ok', texto: r.semConsumo
        ? `Saída liberada: comanda ${r.numero} (${r.cliente}) sem consumo, encerrada automaticamente.`
        : `Saída registrada: comanda ${r.numero} (${r.cliente}) · ${r.situacao === 'F' ? `paga ${brl(r.total)}` : 'cancelada'}` })
      setNumero('')
    } catch (err) { onMsg({ tipo: 'erro', texto: (err as Error).message }) }
  }
  return (
    <form className="cartao" onSubmit={enviar}>
      <label>Nº da pulseira / comanda
        <input className="numero-grande" value={numero} onChange={e => setNumero(e.target.value.replace(/\D/g, ''))} inputMode="numeric" required autoFocus />
      </label>
      <div className="acoes"><button type="submit">Registrar saída</button></div>
    </form>
  )
}

const hoje = () => new Date().toISOString().slice(0, 10)
const hm = (iso: string | null) => (iso ? new Date(iso).toTimeString().slice(0, 5) : '')

function Eventos({ onMudou, onMsg }: { onMudou: () => void; onMsg: (m: Msg) => void }) {
  const [lista, setLista] = useState<Evento[]>([])
  const [servicos, setServicos] = useState<ProdutoResumo[]>([])
  const [edit, setEdit] = useState<{ id: number | null; data: string; nome: string; terra: number | null; rio: number | null; couvert: number | null; ini: string; fim: string; ativo: boolean } | null>(null)

  const carregar = () => api.eventos().then(setLista).catch(() => {})
  useEffect(() => {
    carregar()
    // taxas possíveis: serviços (ativos ou não, ex.: COUVER está inativo no cadastro)
    api.produtos({ somenteAtivos: false })
      .then(p => setServicos(p.filter(x => x.categoria === 'Serviços' || x.tipo === 'S')))
      .catch(() => {})
  }, [])

  const salvar = async (e: FormEvent) => {
    e.preventDefault()
    if (!edit) return
    const dt = (h: string) => (h ? `${edit.data}T${h}:00` : null)
    try {
      await api.salvarEvento(edit.id, {
        data: edit.data, nome: edit.nome, produtoTerraId: edit.terra, produtoRioId: edit.rio, couvertProdutoId: edit.couvert,
        couvertInicio: edit.couvert ? dt(edit.ini) : null, couvertFim: edit.couvert ? dt(edit.fim) : null, ativo: edit.ativo,
      })
      onMsg({ tipo: 'ok', texto: 'Evento salvo.' }); setEdit(null); carregar(); onMudou()
    } catch (err) { onMsg({ tipo: 'erro', texto: (err as Error).message }) }
  }

  const sel = (v: number | null, f: (x: number | null) => void) => (
    <select value={v ?? ''} onChange={e => f(Number(e.target.value) || null)}>
      <option value="">— nenhum —</option>
      {servicos.map(s => <option key={s.id} value={s.id}>{s.nome} · {brl(s.precoVenda)}</option>)}
    </select>
  )

  return (
    <div>
      <div className="filtros"><button onClick={() => setEdit({ id: null, data: hoje(), nome: '', terra: null, rio: null, couvert: null, ini: '17:00', fim: '23:00', ativo: true })}>+ Novo evento</button></div>
      {edit && (
        <form className="cartao" onSubmit={salvar}>
          <div className="grade">
            <label>Data*<input type="date" value={edit.data} onChange={e => setEdit({ ...edit, data: e.target.value })} required /></label>
            <label>Nome*<input value={edit.nome} onChange={e => setEdit({ ...edit, nome: e.target.value })} required /></label>
            <label>Taxa entrada TERRA{sel(edit.terra, x => setEdit({ ...edit, terra: x }))}</label>
            <label>Taxa entrada RIO{sel(edit.rio, x => setEdit({ ...edit, rio: x }))}</label>
            <label>Couvert{sel(edit.couvert, x => setEdit({ ...edit, couvert: x }))}</label>
            {edit.couvert && <>
              <label>Couvert das<input type="time" value={edit.ini} onChange={e => setEdit({ ...edit, ini: e.target.value })} /></label>
              <label>até<input type="time" value={edit.fim} onChange={e => setEdit({ ...edit, fim: e.target.value })} /></label>
            </>}
          </div>
          <label className="check"><input type="checkbox" checked={edit.ativo} onChange={e => setEdit({ ...edit, ativo: e.target.checked })} /> Ativo</label>
          <div className="acoes"><button type="submit">Salvar</button><button type="button" className="sec" onClick={() => setEdit(null)}>Cancelar</button></div>
        </form>
      )}
      <table>
        <thead><tr><th>Data</th><th>Evento</th><th>Terra</th><th>Rio</th><th>Couvert</th><th></th></tr></thead>
        <tbody>
          {lista.map(e => (
            <tr key={e.id} className={e.ativo === 'S' ? '' : 'inativo'}>
              <td>{new Date(e.data).toLocaleDateString('pt-BR')}</td><td>{e.nome}</td>
              <td>{e.produtoTerra ? `${e.produtoTerra} ${brl(e.valorTerra ?? 0)}` : '—'}</td>
              <td>{e.produtoRio ? `${e.produtoRio} ${brl(e.valorRio ?? 0)}` : '—'}</td>
              <td>{e.couvertProduto ? `${brl(e.valorCouvert ?? 0)} · ${hm(e.couvertInicio)}–${hm(e.couvertFim)}` : '—'}</td>
              <td><button className="sec" onClick={() => setEdit({
                id: e.id, data: e.data.slice(0, 10), nome: e.nome, terra: e.produtoTerraId, rio: e.produtoRioId, couvert: e.couvertProdutoId,
                ini: hm(e.couvertInicio) || '17:00', fim: hm(e.couvertFim) || '23:00', ativo: e.ativo === 'S',
              })}>Editar</button></td>
            </tr>
          ))}
          {lista.length === 0 && <tr><td colSpan={6} className="vazio">Nenhum evento nos últimos 30 dias.</td></tr>}
        </tbody>
      </table>
    </div>
  )
}
