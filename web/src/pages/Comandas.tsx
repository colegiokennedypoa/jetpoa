import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { api, brl, hora, P, type Area, type Cliente, type ComandaItem, type ComandaResumo, type Usuario } from '../api'
import { useSessao } from '../sessao'
import { imprimirPagina } from '../imprimir'
import Lancamento from '../components/Lancamento'
import FecharConta from '../components/FecharConta'
import PagamentoPrevio from '../components/PagamentoPrevio'
import BuscaCliente from '../components/BuscaCliente'
import { AvisoMenor, IdadeCliente, SEM_IDADE } from '../components/Idade'

const IMPRESSAO: Record<string, [string, string]> = {
  P: ['Na fila', 'tag pend'],
  I: ['Impresso', 'tag ok'],
  E: ['Erro', 'tag erro'],
}

export default function Comandas() {
  const [lista, setLista] = useState<ComandaResumo[]>([])
  const [situacao, setSituacao] = useState<'A' | 'F'>('A')
  const [selId, setSelId] = useState<number | null>(null)
  const [abrindo, setAbrindo] = useState(false)
  const [msg, setMsg] = useState<{ tipo: 'ok' | 'erro'; texto: string }>()

  const carregar = useCallback(() =>
    api.comandas(situacao).then(setLista).catch(e => setMsg({ tipo: 'erro', texto: e.message })), [situacao])
  useEffect(() => {
    carregar()
    const t = setInterval(carregar, 10000)
    return () => clearInterval(t)
  }, [carregar])

  return (
    <section className="comandas">
      <aside className="lista-comandas">
        <div className="filtros">
          <button onClick={() => { setAbrindo(true); setSelId(null) }}>+ Abrir comanda</button>
          <span className="segmentado mini">
            <button className={situacao === 'A' ? 'ativo' : ''} onClick={() => { setSituacao('A'); setSelId(null) }}>Abertas</button>
            <button className={situacao === 'F' ? 'ativo' : ''} onClick={() => { setSituacao('F'); setSelId(null) }}>Fechadas 24h</button>
          </span>
          <span className="contador">{lista.length}</span>
        </div>
        {lista.map(c => (
          <button key={c.id} className={'comanda-card' + (c.id === selId ? ' sel' : '')} onClick={() => { setSelId(c.id); setAbrindo(false) }}>
            <span className="num">{c.numero}</span>
            <span className="info">
              <b>{c.identificacao ?? '—'}{c.menorIdade === 'S' && <span className="tag menor" title="Menor de idade">-18</span>}</b>
              <small>{c.entradaTipo === 'R' ? 'Rio' : 'Terra'}{c.area ? ` · ${c.area}` : ''} · {c.fechadaEm ? `fechada ${hora(c.fechadaEm)}` : `desde ${hora(c.abertaEm)}`}</small>
            </span>
            <span className="valor">{brl(c.valorTotal ?? c.total)}{c.enviosComErro > 0 && situacao === 'A' && <span className="tag erro" title="Falha de impressão">!</span>}</span>
          </button>
        ))}
        {lista.length === 0 && <p className="vazio">{situacao === 'A' ? 'Nenhuma comanda aberta.' : 'Nenhuma comanda fechada nas últimas 24h.'}</p>}
      </aside>

      <div className="detalhe">
        {msg && <p className={msg.tipo}>{msg.texto}</p>}
        {abrindo && (
          <AbrirComanda
            onCancelar={() => setAbrindo(false)}
            onAberta={id => { setAbrindo(false); setSelId(id); carregar(); setMsg({ tipo: 'ok', texto: 'Comanda aberta.' }) }}
          />
        )}
        {selId && !abrindo && (
          <DetalheComanda key={selId} id={selId} onMsg={setMsg}
            onMudou={carregar} onEncerrada={() => { setSelId(null); carregar() }} />
        )}
        {!selId && !abrindo && <p className="vazio">Selecione uma comanda ou abra uma nova.</p>}
      </div>
    </section>
  )
}

function AbrirComanda({ onAberta, onCancelar }: { onAberta: (id: number) => void; onCancelar: () => void }) {
  const [areas, setAreas] = useState<Area[]>([])
  const [numero, setNumero] = useState('')
  const [entrada, setEntrada] = useState<'T' | 'R'>('T')
  const [areaId, setAreaId] = useState<number | null>(null)
  const [cliente, setCliente] = useState<Cliente | null>(null)
  const [erro, setErro] = useState<string>()
  const [idade, setIdade] = useState(SEM_IDADE)

  useEffect(() => { api.areas().then(setAreas).catch(() => {}) }, [])
  useEffect(() => setIdade(SEM_IDADE), [cliente])

  const salvar = async (e: FormEvent) => {
    e.preventDefault()
    setErro(undefined)
    if (!cliente) { setErro('Selecione ou cadastre o cliente antes de abrir a comanda.'); return }
    try {
      const r = await api.abrirComanda({
        numero: Number(numero), clienteId: cliente.id, identificacao: null, entradaTipo: entrada, areaId, ...idade,
      })
      onAberta(r.id)
    } catch (err) {
      setErro((err as Error).message)
    }
  }

  return (
    <form className="cartao" onSubmit={salvar}>
      <h3>Abrir comanda</h3>
      <div className="grade">
        <label>Número*<input value={numero} onChange={e => setNumero(e.target.value.replace(/\D/g, ''))} inputMode="numeric" required autoFocus /></label>
        <label>Entrada
          <span className="segmentado">
            <button type="button" className={entrada === 'T' ? 'ativo' : ''} onClick={() => setEntrada('T')}>Terra</button>
            <button type="button" className={entrada === 'R' ? 'ativo' : ''} onClick={() => setEntrada('R')}>Rio</button>
          </span>
        </label>
        <label>Área (opcional)
          <select value={areaId ?? ''} onChange={e => setAreaId(e.target.value ? Number(e.target.value) : null)}>
            <option value="">—</option>
            {areas.map(a => <option key={a.id} value={a.id}>{a.codigo} · {a.nome}</option>)}
          </select>
        </label>
        <div className="largo campo">
          <span className="rotulo">Cliente* (obrigatório)</span>
          <BuscaCliente cliente={cliente} onCliente={setCliente} />
          {cliente && <IdadeCliente cliente={cliente} valor={idade} onChange={setIdade} />}
        </div>
      </div>
      {erro && <p className="erro">{erro}</p>}
      <div className="acoes">
        <button type="submit" disabled={!cliente}>Abrir</button>
        <button type="button" className="sec" onClick={onCancelar}>Cancelar</button>
      </div>
    </form>
  )
}

function CancelarComanda({ id, temItens, onVoltar, onCancelada }: {
  id: number; temItens: boolean; onVoltar: () => void; onCancelada: () => void
}) {
  const { eu, pode } = useSessao()
  // com itens lançados, quem não é gerente precisa da autorização (PIN) de um gerente
  const precisaAutorizador = temItens && !pode(P.comandaCancelar)
  const [usuarios, setUsuarios] = useState<Usuario[]>([])
  const [usuarioId, setUsuarioId] = useState<number | null>(null)
  const [pin, setPin] = useState('')
  const [motivo, setMotivo] = useState('')
  const [erro, setErro] = useState<string>()

  useEffect(() => {
    if (precisaAutorizador) api.autorizadores().then(u => setUsuarios(u.filter(x => x.id !== eu.id))).catch(() => {})
  }, [precisaAutorizador, eu.id])

  const confirmar = async (e: FormEvent) => {
    e.preventDefault()
    if (precisaAutorizador && !usuarioId) return
    try { await api.cancelarComanda(id, motivo, precisaAutorizador ? usuarioId : null, precisaAutorizador ? pin : null); onCancelada() }
    catch (err) { setErro((err as Error).message) }
  }

  return (
    <form className="cartao" onSubmit={confirmar}>
      <h3>Cancelar comanda</h3>
      {precisaAutorizador && <p className="alerta-box">A comanda tem itens lançados: é preciso a autorização de um gerente.</p>}
      <div className="grade">
        <label>Motivo*<input value={motivo} onChange={e => setMotivo(e.target.value)} required minLength={3} /></label>
        {precisaAutorizador && <>
          <label>Autorizado por
            <select value={usuarioId ?? ''} onChange={e => setUsuarioId(Number(e.target.value) || null)} required>
              <option value="">Selecione…</option>
              {usuarios.map(u => <option key={u.id} value={u.id}>{u.nome} ({u.perfil})</option>)}
            </select>
          </label>
          <label>PIN do gerente<input type="password" inputMode="numeric" autoComplete="off" value={pin} onChange={e => setPin(e.target.value.replace(/\D/g, ''))} required /></label>
        </>}
      </div>
      {erro && <p className="erro">{erro}</p>}
      <div className="acoes">
        <button type="submit">Confirmar cancelamento</button>
        <button type="button" className="sec" onClick={onVoltar}>Voltar</button>
      </div>
    </form>
  )
}

function DetalheComanda({ id, onMudou, onMsg, onEncerrada }: {
  id: number; onMudou: () => void; onEncerrada: () => void; onMsg: (m: { tipo: 'ok' | 'erro'; texto: string }) => void
}) {
  const { eu, pode } = useSessao()
  const [dados, setDados] = useState<{ comanda: ComandaResumo; itens: ComandaItem[] }>()
  const [modo, setModo] = useState<'ver' | 'lancar' | 'fechar' | 'cancelar'>('ver')
  const [imprimindo, setImprimindo] = useState(false)
  const [registrandoPrevio, setRegistrandoPrevio] = useState(false)
  const lancando = modo === 'lancar'
  const setLancando = (f: (l: boolean) => boolean) => setModo(m => (f(m === 'lancar') ? 'lancar' : 'ver'))

  const carregar = useCallback(() => api.comanda(id).then(setDados).catch(e => onMsg({ tipo: 'erro', texto: e.message })), [id, onMsg])
  useEffect(() => {
    carregar()
    const t = setInterval(carregar, 5000)
    return () => clearInterval(t)
  }, [carregar])

  if (!dados) return <p className="vazio">Carregando…</p>
  const { comanda: c, itens } = dados

  const imprimirConferencia = async () => {
    setImprimindo(true)
    try {
      await imprimirPagina(`/api/comandas/${c.id}/conferencia`)
      onMsg({ tipo: 'ok', texto: `Conferência da comanda ${c.numero} enviada para a impressora.` })
    } catch (e) {
      onMsg({ tipo: 'erro', texto: (e as Error).message })
    } finally {
      setImprimindo(false)
    }
  }

  const cancelar = async (item: ComandaItem) => {
    const motivo = window.prompt(`Cancelar ${item.quantidade}x ${item.produto}?\nInforme o motivo:`)
    if (!motivo) return
    try {
      await api.cancelarItem(c.id, item.id, motivo)
      onMsg({ tipo: 'ok', texto: 'Item cancelado' + (item.impressao ? ' · aviso enviado ao setor.' : '.') })
      carregar(); onMudou()
    } catch (e) {
      onMsg({ tipo: 'erro', texto: (e as Error).message })
    }
  }

  return (
    <div>
      <div className="cabecalho-comanda">
        <span className="num">{c.numero}</span>
        <div>
          <h3>{c.identificacao ?? '—'} {c.clienteCategoria && <span className="tag ok">{c.clienteCategoria}</span>}</h3>
          <small>{c.entradaTipo === 'R' ? 'Entrada pelo Rio' : 'Entrada por Terra'}{c.area ? ` · ${c.area}` : ''} · aberta às {hora(c.abertaEm)}</small>
        </div>
        <span className="total">{brl(c.valorTotal ?? c.total)}
          {c.situacao === 'A' && c.pagoPrevio > 0 && <small className="pago-previo">pago antes {brl(c.pagoPrevio)}</small>}
        </span>
        {c.situacao === 'A' ? (
          <span className="acoes-cab">
            {pode(P.comandaLancar) && <button onClick={() => setLancando(l => !l)}>{lancando ? 'Fechar lançamento' : '+ Lançar itens'}</button>}
            {pode(P.comandaFechar) && <button className="sec" onClick={imprimirConferencia} disabled={c.qtdItens === 0 || imprimindo}
              title="Imprime a lista de itens e o total para o cliente conferir antes de pagar">{imprimindo ? 'Imprimindo…' : 'Imprimir conferência'}</button>}
            {pode(P.comandaFechar) && <button className="sec" onClick={() => setRegistrandoPrevio(r => !r)}
              title="Cliente pagou antes (sinal, pacote, PIX adiantado): o valor é abatido no fechamento">Pagamento prévio</button>}
            {pode(P.comandaFechar) && <button onClick={() => setModo('fechar')} disabled={c.qtdItens === 0 || c.clienteId == null}
              title={c.clienteId == null ? 'Vincule um cliente antes de fechar' : undefined}>Fechar conta</button>}
            {pode(P.comandaFechar) && <button className="sec" onClick={() => setModo('cancelar')}>Cancelar comanda</button>}
          </span>
        ) : <span className="tag ok">{c.situacao === 'F' ? 'Fechada' : 'Cancelada'}</span>}
      </div>
      <AvisoMenor comanda={c} />
      {pode(P.comandaFechar) && (
        <PagamentoPrevio comandaId={c.id} aberta={c.situacao === 'A'} registrando={registrandoPrevio && modo !== 'fechar'}
          onFechar={() => setRegistrandoPrevio(false)} onMudou={() => { carregar(); onMudou() }} onMsg={onMsg} />
      )}

      {c.clienteId == null && c.situacao !== 'C' && (
        <div className="cartao alerta-box">
          <b>Comanda sem cliente cadastrado.</b> {c.situacao === 'A'
            ? 'Selecione ou cadastre o cliente para poder fechar.'
            : 'Comanda fechada antes da regra: vincule o cliente para regularizar.'}
          <BuscaCliente cliente={null} onCliente={async cli => {
            if (!cli) return
            try { await api.vincularCliente(c.id, cli.id); onMsg({ tipo: 'ok', texto: `Cliente ${cli.nome} vinculado à comanda ${c.numero}.` }); carregar(); onMudou() }
            catch (e) { onMsg({ tipo: 'erro', texto: (e as Error).message }) }
          }} />
        </div>
      )}

      {modo === 'fechar' && (
        <FecharConta comanda={c} itens={itens} onVoltar={() => setModo('ver')}
          onFechada={m => { onMsg({ tipo: 'ok', texto: m }); onEncerrada() }} />
      )}
      {modo === 'cancelar' && (
        <CancelarComanda id={c.id} temItens={c.qtdItens > 0} onVoltar={() => setModo('ver')}
          onCancelada={() => { onMsg({ tipo: 'ok', texto: `Comanda ${c.numero} cancelada.` }); onEncerrada() }} />
      )}

      {lancando && (
        <Lancamento comandaId={c.id} origem="CAIXA" mesaAnterior={c.mesa} garcomFixo={eu.garcomId ?? undefined}
          onLancado={m => { onMsg({ tipo: 'ok', texto: m }); setModo('ver'); carregar(); onMudou() }} />
      )}

      <table>
        <thead><tr><th>Item</th><th className="num">Qtd</th><th className="num">Unit.</th><th className="num">Total</th><th>Setor</th><th>Produção</th><th>Garçom</th><th>Hora</th><th></th></tr></thead>
        <tbody>
          {itens.map(i => (
            <tr key={i.id} className={i.situacao === 'C' ? 'inativo' : ''} title={i.motivoCancelamento ? `Cancelado: ${i.motivoCancelamento}` : undefined}>
              <td>{i.produto}{i.observacao && <small className="obs"> · {i.observacao}</small>}</td>
              <td className="num">{i.quantidade}</td>
              <td className="num">{brl(i.precoUnitario)}</td>
              <td className="num">{brl(i.total)}</td>
              <td>{i.setor}</td>
              <td>{i.impressao ? <span className={IMPRESSAO[i.impressao][1]} title={i.erroImpressao ?? ''}>{IMPRESSAO[i.impressao][0]}</span> : '—'}</td>
              <td>{i.garcom ?? '—'}</td>
              <td>{hora(i.lancadoEm)}</td>
              <td>{i.situacao === 'A' && c.situacao === 'A' && pode(P.itemCancelar) && <button className="sec" onClick={() => cancelar(i)}>Cancelar</button>}
                {i.valorDesconto > 0 && <small className="obs">desc. {brl(i.valorDesconto)}</small>}</td>
            </tr>
          ))}
          {itens.length === 0 && <tr><td colSpan={9} className="vazio">Nenhum item lançado.</td></tr>}
        </tbody>
      </table>
    </div>
  )
}
