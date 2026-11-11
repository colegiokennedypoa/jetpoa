import { useEffect, useState } from 'react'
import { api, brl, type Categoria, type ProdutoResumo } from '../api'
import EditarProduto from '../components/EditarProduto'

const TIPO = { P: 'Produto', K: 'Kit/Combo', S: 'Serviço' } as const

export default function Produtos() {
  const [categorias, setCategorias] = useState<Categoria[]>([])
  const [itens, setItens] = useState<ProdutoResumo[]>([])
  const [busca, setBusca] = useState('')
  const [categoriaId, setCategoriaId] = useState<number | undefined>()
  const [pendencia, setPendencia] = useState(false)
  const [erro, setErro] = useState<string>()
  const [editando, setEditando] = useState<number | 'novo' | null>(null)
  const [msg, setMsg] = useState<{ tipo: 'ok' | 'erro'; texto: string }>()
  const [versao, setVersao] = useState(0)

  useEffect(() => { api.categorias().then(setCategorias).catch(e => setErro(e.message)) }, [])

  useEffect(() => {
    const t = setTimeout(() => {
      api.produtos({ busca, categoriaId, pendenciaFiscal: pendencia })
        .then(r => { setItens(r); setErro(undefined) })
        .catch(e => setErro(e.message))
    }, 250)
    return () => clearTimeout(t)
  }, [busca, categoriaId, pendencia, versao])

  return (
    <section>
      <div className="filtros">
        <input placeholder="Buscar produto…" value={busca} onChange={e => setBusca(e.target.value)} autoFocus />
        <select value={categoriaId ?? ''} onChange={e => setCategoriaId(e.target.value ? Number(e.target.value) : undefined)}>
          <option value="">Todas as categorias</option>
          {categorias.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}
        </select>
        <label><input type="checkbox" checked={pendencia} onChange={e => setPendencia(e.target.checked)} /> Só com pendência fiscal</label>
        <button onClick={() => setEditando('novo')}>+ Novo produto</button>
        <span className="contador">{itens.length} produtos</span>
      </div>
      {erro && <p className="erro">{erro}</p>}
      {msg && <p className={msg.tipo}>{msg.texto}</p>}
      {editando !== null && (
        <EditarProduto key={String(editando)} id={editando === 'novo' ? null : editando} categorias={categorias}
          onFim={m => { setEditando(null); if (m) setMsg(m); setVersao(v => v + 1) }} />
      )}
      <table>
        <thead>
          <tr><th>Produto</th><th>Categoria</th><th>Tipo</th><th>Setor</th><th className="num">Preço</th><th className="num">Estoque</th><th>NCM</th><th>CFOP</th><th>CSOSN</th><th>NFC-e</th><th></th></tr>
        </thead>
        <tbody>
          {itens.map(p => (
            <tr key={p.id}>
              <td>{p.nome}</td>
              <td>{p.categoria}</td>
              <td>{TIPO[p.tipo]}</td>
              <td>{p.setor}</td>
              <td className="num">{brl(p.precoVenda)}</td>
              <td className={'num' + (p.estoqueMinimo != null && p.estoqueAtual < p.estoqueMinimo ? ' alerta' : '')}>{p.estoqueAtual}</td>
              <td>{p.ncm ?? '—'}</td>
              <td>{p.cfop ?? '—'}</td>
              <td>{p.csosn ?? '—'}</td>
              <td>{p.fiscalOk === 'S' ? <span className="tag ok">OK</span>
                : p.fiscalOk === 'X' ? <span className="tag" title="Serviço/taxa não entra na NFC-e">fora da nota</span>
                : <span className="tag pend">Pendente</span>}</td>
              <td><button className="sec" onClick={() => { setEditando(p.id); window.scrollTo({ top: 0 }) }}>Editar</button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
