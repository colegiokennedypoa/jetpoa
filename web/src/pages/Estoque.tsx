import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { api, brl, type ItemEstoque, type MovimentoEstoque } from '../api'
import SeletorUsuario from '../components/SeletorUsuario'

const TIPO: Record<string, string> = { V: 'Venda', E: 'Entrada', A: 'Ajuste', P: 'Perda', I: 'Inventário' }
type Msg = { tipo: 'ok' | 'erro'; texto: string }

export default function Estoque() {
  const [itens, setItens] = useState<ItemEstoque[]>([])
  const [busca, setBusca] = useState('')
  const [abaixo, setAbaixo] = useState(false)
  const [modo, setModo] = useState<'lista' | 'movimento' | 'inventario' | 'historico'>('lista')
  const [selecionado, setSelecionado] = useState<ItemEstoque | null>(null)
  const [msg, setMsg] = useState<Msg>()

  const carregar = useCallback(() => api.estoque(abaixo).then(setItens).catch(e => setMsg({ tipo: 'erro', texto: e.message })), [abaixo])
  useEffect(() => { carregar() }, [carregar])

  const filtrados = useMemo(() => {
    const t = busca.trim().toUpperCase().split(/\s+/).filter(Boolean)
    return itens.filter(i => t.every(x => i.nome.toUpperCase().includes(x)))
  }, [itens, busca])

  const valorEstoque = itens.reduce((s, i) => s + Math.max(i.estoqueAtual, 0) * (i.precoCusto ?? 0), 0)

  return (
    <section>
      <div className="filtros">
        <input placeholder="Buscar produto…" value={busca} onChange={e => setBusca(e.target.value)} />
        <label><input type="checkbox" checked={abaixo} onChange={e => setAbaixo(e.target.checked)} /> Só abaixo do mínimo</label>
        <span className="segmentado mini">
          <button className={modo === 'lista' ? 'ativo' : ''} onClick={() => setModo('lista')}>Saldos</button>
          <button className={modo === 'movimento' ? 'ativo' : ''} onClick={() => { setModo('movimento'); setSelecionado(null) }}>Entrada / perda</button>
          <button className={modo === 'inventario' ? 'ativo' : ''} onClick={() => setModo('inventario')}>Inventário</button>
          <button className={modo === 'historico' ? 'ativo' : ''} onClick={() => setModo('historico')}>Movimentos</button>
        </span>
        <span className="contador">{filtrados.length} produtos · estoque a custo {brl(valorEstoque)}</span>
      </div>
      {msg && <p className={msg.tipo}>{msg.texto}</p>}

      {modo === 'movimento' && <Movimento itens={itens} inicial={selecionado} onFim={m => { setMsg(m); carregar() }} />}
      {modo === 'inventario' && <Inventario itens={filtrados} onFim={m => { setMsg(m); carregar(); setModo('lista') }} />}
      {modo === 'historico' && <Historico produto={selecionado} />}

      {(modo === 'lista' || modo === 'movimento') && (
        <table>
          <thead><tr><th>Produto</th><th>Categoria</th><th className="num">Saldo</th><th className="num">Mínimo</th><th className="num">Comprar</th><th className="num">Vendido hoje</th><th className="num">Custo</th><th></th></tr></thead>
          <tbody>
            {filtrados.map(i => (
              <tr key={i.id}>
                <td>{i.nome}</td><td>{i.categoria}</td>
                <td className={'num' + (i.estoqueAtual < 0 || (i.estoqueMinimo != null && i.estoqueAtual < i.estoqueMinimo) ? ' alerta' : '')}>{i.estoqueAtual}</td>
                <td className="num">{i.estoqueMinimo ?? '—'}</td>
                <td className="num">{i.comprar ?? ''}</td>
                <td className="num">{i.vendidoHoje || ''}</td>
                <td className="num">{i.precoCusto != null ? brl(i.precoCusto) : '—'}</td>
                <td className="acoes-linha">
                  <button className="sec" onClick={() => { setSelecionado(i); setModo('movimento') }}>Movimentar</button>
                  <button className="sec" onClick={() => { setSelecionado(i); setModo('historico') }}>Histórico</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

function Movimento({ itens, inicial, onFim }: { itens: ItemEstoque[]; inicial: ItemEstoque | null; onFim: (m: Msg) => void }) {
  const [produtoId, setProdutoId] = useState<number | null>(inicial?.id ?? null)
  const [tipo, setTipo] = useState<'E' | 'P' | 'A'>('E')
  const [qtd, setQtd] = useState('')
  const [custo, setCusto] = useState('')
  const [obs, setObs] = useState('')
  const [usuarioId, setUsuarioId] = useState<number | null>(null)
  const [pin, setPin] = useState('')
  useEffect(() => { setProdutoId(inicial?.id ?? null) }, [inicial])

  const enviar = async (e: FormEvent) => {
    e.preventDefault()
    try {
      const r = await api.movimentarEstoque({
        produtoId: produtoId!, tipo, quantidade: Number(qtd.replace(',', '.')),
        custoUnitario: tipo === 'E' && custo ? Number(custo.replace(',', '.')) : null, observacao: obs || null, usuarioId: usuarioId!, pin,
      })
      const p = itens.find(i => i.id === produtoId)
      onFim({ tipo: 'ok', texto: `${TIPO[tipo]} registrada em ${p?.nome}: saldo agora ${r.saldo}.` })
      setQtd(''); setCusto(''); setObs(''); setPin('')
    } catch (err) { onFim({ tipo: 'erro', texto: (err as Error).message }) }
  }

  return (
    <form className="cartao" onSubmit={enviar}>
      <h3>Movimentar estoque</h3>
      <div className="grade">
        <label>Produto*
          <select value={produtoId ?? ''} onChange={e => setProdutoId(Number(e.target.value) || null)} required>
            <option value="">Selecione…</option>
            {itens.map(i => <option key={i.id} value={i.id}>{i.nome} (saldo {i.estoqueAtual})</option>)}
          </select>
        </label>
        <label>Tipo
          <span className="segmentado">
            <button type="button" className={tipo === 'E' ? 'ativo' : ''} onClick={() => setTipo('E')}>Entrada</button>
            <button type="button" className={tipo === 'P' ? 'ativo' : ''} onClick={() => setTipo('P')}>Perda</button>
            <button type="button" className={tipo === 'A' ? 'ativo' : ''} onClick={() => setTipo('A')}>Ajuste ±</button>
          </span>
        </label>
        <label>Quantidade*<input value={qtd} onChange={e => setQtd(e.target.value)} inputMode="decimal" required placeholder={tipo === 'A' ? 'use - para reduzir' : ''} /></label>
        {tipo === 'E' && <label>Custo unitário (atualiza o custo)<input value={custo} onChange={e => setCusto(e.target.value)} inputMode="decimal" /></label>}
        <label className="largo">{tipo === 'E' ? 'Observação (ex.: NF / fornecedor)' : 'Motivo*'}<input value={obs} onChange={e => setObs(e.target.value)} required={tipo !== 'E'} /></label>
        <SeletorUsuario usuarioId={usuarioId} pin={pin} onUsuario={setUsuarioId} onPin={setPin} somenteGerentes={tipo === 'A'} />
      </div>
      <div className="acoes"><button type="submit">Registrar</button></div>
    </form>
  )
}

function Inventario({ itens, onFim }: { itens: ItemEstoque[]; onFim: (m: Msg) => void }) {
  const [contagem, setContagem] = useState<Record<number, string>>({})
  const [obs, setObs] = useState('')
  const [usuarioId, setUsuarioId] = useState<number | null>(null)
  const [pin, setPin] = useState('')
  const informados = Object.entries(contagem).filter(([, v]) => v.trim() !== '')

  const enviar = async (e: FormEvent) => {
    e.preventDefault()
    try {
      const r = await api.inventario({
        itens: informados.map(([id, v]) => ({ produtoId: Number(id), contado: Number(v.replace(',', '.')) })),
        observacao: obs || null, usuarioId: usuarioId!, pin,
      })
      onFim({ tipo: 'ok', texto: `Inventário registrado: ${r.ajustados} produto(s) ajustado(s) de ${informados.length} contado(s).` })
    } catch (err) { onFim({ tipo: 'erro', texto: (err as Error).message }) }
  }

  return (
    <form onSubmit={enviar}>
      <p className="alerta-box">Informe a quantidade contada só dos produtos conferidos. O sistema lança a diferença em relação ao saldo atual.</p>
      <table>
        <thead><tr><th>Produto</th><th className="num">Saldo sistema</th><th className="num">Contado</th><th className="num">Diferença</th></tr></thead>
        <tbody>
          {itens.map(i => {
            const v = contagem[i.id] ?? ''
            const dif = v.trim() === '' ? null : Number(v.replace(',', '.')) - i.estoqueAtual
            return (
              <tr key={i.id}>
                <td>{i.nome}</td><td className="num">{i.estoqueAtual}</td>
                <td className="num"><input className="valor" style={{ width: 90 }} inputMode="decimal" value={v} onChange={e => setContagem(c => ({ ...c, [i.id]: e.target.value }))} /></td>
                <td className={'num' + (dif ? ' alerta' : '')}>{dif == null ? '' : dif > 0 ? `+${dif}` : dif}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
      <div className="cartao" style={{ marginTop: '0.8rem' }}>
        <div className="grade">
          <label className="largo">Observação<input value={obs} onChange={e => setObs(e.target.value)} /></label>
          <SeletorUsuario usuarioId={usuarioId} pin={pin} onUsuario={setUsuarioId} onPin={setPin} />
        </div>
        <div className="acoes"><button type="submit" disabled={informados.length === 0}>Registrar inventário ({informados.length})</button></div>
      </div>
    </form>
  )
}

function Historico({ produto }: { produto: ItemEstoque | null }) {
  const [dias, setDias] = useState(7)
  const [movs, setMovs] = useState<MovimentoEstoque[]>([])
  useEffect(() => { api.movimentosEstoque(produto?.id ?? null, dias).then(setMovs).catch(() => {}) }, [produto, dias])
  return (
    <div>
      <div className="filtros">
        <b>{produto ? produto.nome : 'Todos os produtos'}</b>
        <select value={dias} onChange={e => setDias(Number(e.target.value))}>
          {[1, 7, 30, 90].map(d => <option key={d} value={d}>Últimos {d} dia(s)</option>)}
        </select>
      </div>
      <table>
        <thead><tr><th>Data</th><th>Produto</th><th>Tipo</th><th className="num">Qtd</th><th className="num">Saldo</th><th>Comanda</th><th>Usuário</th><th>Observação</th></tr></thead>
        <tbody>
          {movs.map(m => (
            <tr key={m.id}>
              <td>{new Date(m.criadoEm).toLocaleString('pt-BR')}</td><td>{m.produto}</td><td>{TIPO[m.tipo]}</td>
              <td className={'num' + (m.quantidade < 0 ? ' alerta' : '')}>{m.quantidade > 0 ? `+${m.quantidade}` : m.quantidade}</td>
              <td className="num">{m.saldoApos}</td><td>{m.comandaNumero ?? ''}</td><td>{m.usuario ?? ''}</td>
              <td className="detalhe-erro">{m.observacao ?? ''}{m.custoUnitario != null ? ` · custo ${brl(m.custoUnitario)}` : ''}</td>
            </tr>
          ))}
          {movs.length === 0 && <tr><td colSpan={8} className="vazio">Sem movimentos no período.</td></tr>}
        </tbody>
      </table>
    </div>
  )
}
