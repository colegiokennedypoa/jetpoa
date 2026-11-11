import { useEffect, useState } from 'react'
import { api, brl, type Categoria, type ProdutoResumo } from '../api'

const TIPO = { P: 'Produto', K: 'Kit/Combo', S: 'Serviço' } as const

export default function Produtos() {
  const [categorias, setCategorias] = useState<Categoria[]>([])
  const [itens, setItens] = useState<ProdutoResumo[]>([])
  const [busca, setBusca] = useState('')
  const [categoriaId, setCategoriaId] = useState<number | undefined>()
  const [pendencia, setPendencia] = useState(false)
  const [erro, setErro] = useState<string>()

  useEffect(() => { api.categorias().then(setCategorias).catch(e => setErro(e.message)) }, [])

  useEffect(() => {
    const t = setTimeout(() => {
      api.produtos({ busca, categoriaId, pendenciaFiscal: pendencia })
        .then(r => { setItens(r); setErro(undefined) })
        .catch(e => setErro(e.message))
    }, 250)
    return () => clearTimeout(t)
  }, [busca, categoriaId, pendencia])

  return (
    <section>
      <div className="filtros">
        <input placeholder="Buscar produto…" value={busca} onChange={e => setBusca(e.target.value)} autoFocus />
        <select value={categoriaId ?? ''} onChange={e => setCategoriaId(e.target.value ? Number(e.target.value) : undefined)}>
          <option value="">Todas as categorias</option>
          {categorias.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}
        </select>
        <label><input type="checkbox" checked={pendencia} onChange={e => setPendencia(e.target.checked)} /> Só com pendência fiscal</label>
        <span className="contador">{itens.length} produtos</span>
      </div>
      {erro && <p className="erro">{erro}</p>}
      <table>
        <thead>
          <tr><th>Produto</th><th>Categoria</th><th>Tipo</th><th>Setor</th><th className="num">Preço</th><th className="num">Estoque</th><th>NCM</th><th>CFOP</th><th>CSOSN</th><th>NFC-e</th></tr>
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
              <td>{p.fiscalOk === 'S' ? <span className="tag ok">OK</span> : <span className="tag pend">Pendente</span>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
