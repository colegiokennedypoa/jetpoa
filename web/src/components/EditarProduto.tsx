import { useEffect, useState, type FormEvent } from 'react'
import { api, type Categoria, type ProdutoCompleto, type Setor } from '../api'
type F = {
  nome: string; descricao: string; categoriaId: number | null; tipo: 'P' | 'K' | 'S'; setorProducaoId: number | null; unidade: string
  precoVenda: string; precoCusto: string; controlaEstoque: boolean; estoqueMinimo: string; vendivel: boolean; ativo: boolean
  ncm: string; cest: string; cfop: string; csosn: string; cstPisCofins: string; gtin: string
}

const num = (s: string) => (s.trim() === '' ? null : Number(s.replace(',', '.')))
const txt = (s: string) => (s.trim() === '' ? null : s.trim())

/** Edição/criação de produto: preço, setor de produção (impressora), estoque e dados fiscais da NFC-e. */
export default function EditarProduto({ id, categorias, onFim }: {
  id: number | null; categorias: Categoria[]; onFim: (msg?: { tipo: 'ok' | 'erro'; texto: string }) => void
}) {
  const [setores, setSetores] = useState<Setor[]>([])
  const [orig, setOrig] = useState<ProdutoCompleto | null>(null)
  const [f, setF] = useState<F | null>(null)
  const [erro, setErro] = useState<string>()

  useEffect(() => { api.setores().then(s => setSetores(s.filter(x => x.codigo !== 'CAIXA'))).catch(() => {}) }, [])
  useEffect(() => {
    if (id == null) {
      setF({ nome: '', descricao: '', categoriaId: null, tipo: 'P', setorProducaoId: null, unidade: 'UN', precoVenda: '', precoCusto: '',
        controlaEstoque: true, estoqueMinimo: '', vendivel: true, ativo: true, ncm: '', cest: '', cfop: '', csosn: '', cstPisCofins: '', gtin: '' })
      return
    }
    api.produto(id).then(p => {
      setOrig(p)
      setF({
        nome: p.nome, descricao: p.descricao ?? '', categoriaId: p.categoriaId, tipo: p.tipo, setorProducaoId: p.setorProducaoId, unidade: p.unidade,
        precoVenda: String(p.precoVenda), precoCusto: p.precoCusto != null ? String(p.precoCusto) : '', controlaEstoque: p.controlaEstoque === 'S',
        estoqueMinimo: p.estoqueMinimo != null ? String(p.estoqueMinimo) : '', vendivel: p.vendivel === 'S', ativo: p.ativo === 'S',
        ncm: p.ncm ?? '', cest: p.cest ?? '', cfop: p.cfop ?? '', csosn: p.csosn ?? '', cstPisCofins: p.cstPisCofins ?? '', gtin: p.gtin ?? '',
      })
    }).catch(e => setErro(e.message))
  }, [id])

  if (!f) return <p className="vazio">Carregando…</p>
  const set = <K extends keyof F>(k: K, v: F[K]) => setF(x => x && { ...x, [k]: v })

  const salvar = async (e: FormEvent) => {
    e.preventDefault()
    setErro(undefined)
    try {
      const r = await api.salvarProduto(id, {
        nome: f.nome, descricao: txt(f.descricao), categoriaId: f.categoriaId!, tipo: f.tipo, setorProducaoId: f.setorProducaoId!, unidade: txt(f.unidade),
        precoVenda: num(f.precoVenda) ?? 0, precoCusto: num(f.precoCusto), controlaEstoque: f.controlaEstoque, estoqueMinimo: num(f.estoqueMinimo),
        vendivel: f.vendivel, ativo: f.ativo, ordem: orig?.ordem ?? 0, ncm: txt(f.ncm), cest: txt(f.cest), cfop: txt(f.cfop), csosn: txt(f.csosn),
        cstPisCofins: txt(f.cstPisCofins), gtin: txt(f.gtin),
      })
      onFim({
        tipo: r.avisosFiscais.length ? 'erro' : 'ok',
        texto: `Produto salvo.${r.avisosFiscais.length ? ' Atenção, ainda não emite NFC-e: ' + r.avisosFiscais.join(' ') : ''}`,
      })
    } catch (err) { setErro((err as Error).message) }
  }

  return (
    <form className="cartao" onSubmit={salvar}>
      <h3>{id ? `Editar produto${orig?.legadoCdProduto ? ` (Forms nº ${orig.legadoCdProduto})` : ''}` : 'Novo produto'}</h3>
      <div className="grade">
        <label className="largo">Nome*<input value={f.nome} onChange={e => set('nome', e.target.value)} required /></label>
        <label>Categoria*
          <select value={f.categoriaId ?? ''} onChange={e => set('categoriaId', Number(e.target.value) || null)} required>
            <option value="">Selecione…</option>
            {categorias.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}
          </select>
        </label>
        <label>Tipo
          <select value={f.tipo} onChange={e => set('tipo', e.target.value as F['tipo'])}>
            <option value="P">Produto</option><option value="K">Kit / combo</option><option value="S">Serviço (fora da NFC-e)</option>
          </select>
        </label>
        <label>Imprime em (setor)*
          <select value={f.setorProducaoId ?? ''} onChange={e => set('setorProducaoId', Number(e.target.value) || null)} required>
            <option value="">Selecione…</option>
            {setores.map(s => <option key={s.id} value={s.id}>{s.nome}</option>)}
          </select>
        </label>
        <label>Preço de venda*<input value={f.precoVenda} onChange={e => set('precoVenda', e.target.value)} inputMode="decimal" required /></label>
        <label>Preço de custo<input value={f.precoCusto} onChange={e => set('precoCusto', e.target.value)} inputMode="decimal" /></label>
        <label>Unidade<input value={f.unidade} onChange={e => set('unidade', e.target.value)} maxLength={6} /></label>
        <label>Estoque mínimo<input value={f.estoqueMinimo} onChange={e => set('estoqueMinimo', e.target.value)} inputMode="decimal" disabled={f.tipo !== 'P'} /></label>
      </div>
      <div className="acoes">
        <label className="check"><input type="checkbox" checked={f.ativo} onChange={e => set('ativo', e.target.checked)} /> Ativo</label>
        <label className="check"><input type="checkbox" checked={f.vendivel} onChange={e => set('vendivel', e.target.checked)} /> Aparece para venda</label>
        {f.tipo === 'P' && <label className="check"><input type="checkbox" checked={f.controlaEstoque} onChange={e => set('controlaEstoque', e.target.checked)} /> Controla estoque</label>}
      </div>

      <h4>Dados fiscais (NFC-e) — conferir com o contador</h4>
      <div className="grade">
        <label>NCM (8 dígitos)<input value={f.ncm} onChange={e => set('ncm', e.target.value)} inputMode="numeric" maxLength={10} /></label>
        <label>CFOP<select value={f.cfop} onChange={e => set('cfop', e.target.value)}>
          <option value="">—</option><option value="5102">5102 · venda (tributado no Simples)</option><option value="5405">5405 · venda com ST</option>
          {f.cfop && !['5102', '5405'].includes(f.cfop) && <option value={f.cfop}>{f.cfop}</option>}
        </select></label>
        <label>CSOSN<select value={f.csosn} onChange={e => set('csosn', e.target.value)}>
          <option value="">—</option><option value="102">102 · sem permissão de crédito</option><option value="500">500 · ICMS cobrado por ST</option>
          <option value="103">103 · isento (faixa)</option><option value="300">300 · imune</option><option value="400">400 · não tributado</option>
        </select></label>
        <label>CST PIS/COFINS<select value={f.cstPisCofins} onChange={e => set('cstPisCofins', e.target.value)}>
          <option value="">—</option><option value="49">49 · outras operações de saída</option><option value="04">04 · monofásico (alíquota zero)</option>
          <option value="06">06 · alíquota zero</option><option value="01">01 · tributável</option><option value="99">99 · outras</option>
        </select></label>
        <label>CEST (7 dígitos, se houver ST)<input value={f.cest} onChange={e => set('cest', e.target.value)} inputMode="numeric" maxLength={9} /></label>
        <label>Código de barras (GTIN)<input value={f.gtin} onChange={e => set('gtin', e.target.value)} inputMode="numeric" maxLength={14} /></label>
      </div>

      {erro && <p className="erro">{erro}</p>}
      <div className="acoes"><button type="submit">Salvar</button><button type="button" className="sec" onClick={() => onFim()}>Cancelar</button></div>
    </form>
  )
}
