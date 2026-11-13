import { useEffect, useMemo, useState } from 'react'
import { api, brl, type Garcom, type ItemCarrinho, type ProdutoResumo } from '../api'

type Props = {
  comandaId: number
  origem: 'CAIXA' | 'GARCOM'
  garcomFixo?: number | null
  /** Mesa do último pedido da comanda: vem sugerida. */
  mesaAnterior?: string | null
  onLancado: (msg: string) => void
}

/** Seleção de produtos + carrinho + envio para a comanda (usado no caixa e no app do garçom). */
export default function Lancamento({ comandaId, origem, garcomFixo, mesaAnterior, onLancado }: Props) {
  const [produtos, setProdutos] = useState<ProdutoResumo[]>([])
  const [garcons, setGarcons] = useState<Garcom[]>([])
  const [garcomId, setGarcomId] = useState<number | null>(garcomFixo ?? null)
  const [busca, setBusca] = useState('')
  const [categoria, setCategoria] = useState<string>('')
  const [carrinho, setCarrinho] = useState<ItemCarrinho[]>([])
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string>()
  const [mesa, setMesa] = useState(mesaAnterior ?? '')
  const [aberto, setAberto] = useState(false)   // celular: pedido aberto sobre a tela (barra do rodapé)

  useEffect(() => {
    api.produtosVenda().then(setProdutos).catch(e => setErro(e.message))
    if (garcomFixo === undefined) api.garcons().then(g => setGarcons(g.filter(x => x.ativo === 'S'))).catch(() => {})
  }, [garcomFixo])

  useEffect(() => { if (garcomFixo !== undefined) setGarcomId(garcomFixo) }, [garcomFixo])
  useEffect(() => { setMesa(mesaAnterior ?? '') }, [comandaId, mesaAnterior])

  const categorias = useMemo(() => [...new Set(produtos.map(p => p.categoria))], [produtos])
  const filtrados = useMemo(() => {
    const termos = busca.trim().toUpperCase().split(/\s+/).filter(Boolean)
    return produtos.filter(p =>
      (!categoria || p.categoria === categoria) &&
      termos.every(t => p.nome.toUpperCase().includes(t)))
  }, [produtos, busca, categoria])

  const adicionar = (p: ProdutoResumo) => setCarrinho(c => {
    const i = c.findIndex(x => x.produtoId === p.id && !x.observacao)
    if (i >= 0) return c.map((x, k) => (k === i ? { ...x, quantidade: x.quantidade + 1 } : x))
    return [...c, { produtoId: p.id, nome: p.nome, preco: p.precoVenda, quantidade: 1, observacao: '' }]
  })
  const alterarQtd = (k: number, d: number) =>
    setCarrinho(c => c.map((x, i) => (i === k ? { ...x, quantidade: x.quantidade + d } : x)).filter(x => x.quantidade > 0))
  const alterarObs = (k: number, obs: string) => setCarrinho(c => c.map((x, i) => (i === k ? { ...x, observacao: obs } : x)))

  const total = carrinho.reduce((s, x) => s + x.preco * x.quantidade, 0)
  const totalItens = carrinho.reduce((s, x) => s + x.quantidade, 0)
  // quantidade já no pedido, para mostrar no botão do produto
  const noPedido = useMemo(() => {
    const m = new Map<number, number>()
    carrinho.forEach(x => m.set(x.produtoId, (m.get(x.produtoId) ?? 0) + x.quantidade))
    return m
  }, [carrinho])

  const enviar = async () => {
    if (carrinho.length === 0) return
    if (!mesa.trim()) { setErro('Informe a mesa onde entregar o pedido.'); return }
    setEnviando(true); setErro(undefined)
    try {
      const r = await api.lancar(comandaId, garcomId, origem, mesa.trim(), carrinho)
      setCarrinho([]); setBusca(''); setAberto(false)
      onLancado(`${r.lancados} item(ns) lançados · mesa ${mesa.trim()}${r.envios.length ? ` · ${r.envios.length} envio(s) para produção` : ''}.`)
    } catch (e) {
      setErro((e as Error).message)
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className="lancamento">
      <div className="catalogo">
        <div className="filtros">
          <input type="search" placeholder="Buscar produto…" value={busca} onChange={e => setBusca(e.target.value)} enterKeyHint="search" />
          {garcomFixo === undefined && (
            <select value={garcomId ?? ''} onChange={e => setGarcomId(e.target.value ? Number(e.target.value) : null)}>
              <option value="">Garçom…</option>
              {garcons.map(g => <option key={g.id} value={g.id}>{g.nome}</option>)}
            </select>
          )}
        </div>
        <div className="chips">
          <button className={!categoria ? 'chip ativo' : 'chip'} onClick={() => setCategoria('')}>Todos</button>
          {categorias.map(c => (
            <button key={c} className={categoria === c ? 'chip ativo' : 'chip'} onClick={() => setCategoria(c)}>{c}</button>
          ))}
        </div>
        <div className="produtos-grade">
          {filtrados.map(p => (
            <button key={p.id} className={noPedido.has(p.id) ? 'produto-btn no-pedido' : 'produto-btn'} onClick={() => adicionar(p)}>
              <span className="nome">{p.nome}</span>
              <span className="preco">{brl(p.precoVenda)}</span>
              {noPedido.has(p.id) && <span className="qtd-badge">{noPedido.get(p.id)}</span>}
            </button>
          ))}
          {filtrados.length === 0 && <p className="vazio">Nenhum produto.</p>}
        </div>
      </div>

      {aberto && <div className="carrinho-fundo" onClick={() => setAberto(false)} />}
      <div className={aberto ? 'carrinho aberto' : 'carrinho'}>
        <button type="button" className="carrinho-topo" onClick={() => setAberto(a => !a)} disabled={carrinho.length === 0}>
          <h4>Novo pedido{totalItens > 0 && ` · ${totalItens} ${totalItens === 1 ? 'item' : 'itens'}`}</h4>
          {carrinho.length > 0 && <span className="so-celular">{aberto ? 'Fechar ▼' : 'Ver itens ▲'}</span>}
        </button>
        <div className="carrinho-itens">
        {carrinho.length === 0 && <p className="vazio">Toque nos produtos para adicionar.</p>}
        {carrinho.map((x, k) => (
          <div key={k} className="carrinho-item">
            <div className="linha">
              <span className="nome">{x.nome}</span>
              <span className="qtd">
                <button className="sec" onClick={() => alterarQtd(k, -1)} aria-label="Diminuir">−</button>
                <b>{x.quantidade}</b>
                <button className="sec" onClick={() => alterarQtd(k, 1)} aria-label="Aumentar">+</button>
              </span>
            </div>
            <input placeholder="Observação (ex.: sem gelo)" value={x.observacao} onChange={e => alterarObs(k, e.target.value)} />
          </div>
        ))}
        </div>
        {erro && <p className="erro">{erro}</p>}
        <div className="mesa-envio">
          <label className="mesa">Mesa
            <input value={mesa} onChange={e => setMesa(e.target.value)} maxLength={20} placeholder="ex.: 12"
              title={mesaAnterior ? `Último pedido: mesa ${mesaAnterior}` : undefined} />
          </label>
          <button className="enviar" disabled={carrinho.length === 0 || enviando} onClick={enviar}>
            {enviando ? 'Enviando…' : `Enviar ${carrinho.length ? '· ' + brl(total) : ''}`}
          </button>
        </div>
      </div>
    </div>
  )
}
