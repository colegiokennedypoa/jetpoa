import { useEffect, useState, type FormEvent } from 'react'
import { api, brl, type ClienteCategoria, type ClienteCategoriaInput } from '../api'

type Msg = { tipo: 'ok' | 'erro'; texto: string }
type Form = Omit<ClienteCategoriaInput, 'descontoPctPadrao' | 'limiteComanda'> & { id: number | null; desconto: string; limite: string }

const PADRAO = 'Normal'
const vazio: Form = {
  id: null, nome: '', desconto: '0', limite: '', ativo: true,
  isentaEstacionamento: false, isentaEmbarque: false, isentaCouvert: false, isentaTaxaServico: false,
}

const regras = (k: ClienteCategoria) => [
  k.isentaEstacionamento === 'S' && 'estacionamento',
  k.isentaEmbarque === 'S' && 'embarque',
  k.isentaCouvert === 'S' && 'couvert',
  k.isentaTaxaServico === 'S' && 'taxa de serviço',
].filter(Boolean).join(', ')

/**
 * Categorias de cliente: as regras são aplicadas sozinhas no fechamento da conta
 * (isenções e desconto padrão não passam pela alçada do operador), por isso só gerente edita.
 */
export default function ClienteCategorias({ onMsg }: { onMsg: (m: Msg) => void }) {
  const [lista, setLista] = useState<ClienteCategoria[]>([])
  const [f, setF] = useState<Form | null>(null)
  const [enviando, setEnviando] = useState(false)

  const carregar = () => api.clienteCategorias(true).then(setLista).catch(e => onMsg({ tipo: 'erro', texto: e.message }))
  useEffect(() => { carregar() }, [])   // eslint-disable-line react-hooks/exhaustive-deps

  const paraInput = (x: Form): ClienteCategoriaInput => ({
    nome: x.nome, ativo: x.ativo,
    descontoPctPadrao: Number(x.desconto.replace(',', '.')) || 0,
    limiteComanda: x.limite.trim() ? Number(x.limite.replace(',', '.')) : null,
    isentaEstacionamento: x.isentaEstacionamento, isentaEmbarque: x.isentaEmbarque,
    isentaCouvert: x.isentaCouvert, isentaTaxaServico: x.isentaTaxaServico,
  })
  const doCadastro = (k: ClienteCategoria): Form => ({
    id: k.id, nome: k.nome, ativo: k.ativo === 'S',
    desconto: String(k.descontoPctPadrao), limite: k.limiteComanda == null ? '' : String(k.limiteComanda),
    isentaEstacionamento: k.isentaEstacionamento === 'S', isentaEmbarque: k.isentaEmbarque === 'S',
    isentaCouvert: k.isentaCouvert === 'S', isentaTaxaServico: k.isentaTaxaServico === 'S',
  })

  const salvar = async (id: number | null, dados: ClienteCategoriaInput, texto: string) => {
    setEnviando(true)
    try { await api.salvarClienteCategoria(id, dados); onMsg({ tipo: 'ok', texto }); setF(null); carregar() }
    catch (err) { onMsg({ tipo: 'erro', texto: (err as Error).message }) }
    finally { setEnviando(false) }
  }
  const enviar = (e: FormEvent) => { e.preventDefault(); if (f) salvar(f.id, paraInput(f), `Categoria ${f.nome.trim()} salva.`) }
  const alternar = (k: ClienteCategoria) => {
    const x = doCadastro(k)
    salvar(k.id, paraInput({ ...x, ativo: !x.ativo }), x.ativo ? `Categoria ${k.nome} inativada.` : `Categoria ${k.nome} reativada.`)
  }

  const set = <K extends keyof Form>(k: K, v: Form[K]) => setF(x => x && { ...x, [k]: v })
  const ehPadrao = f?.id != null && lista.find(k => k.id === f.id)?.nome === PADRAO

  return (
    <div>
      <p className="dica">
        As regras valem sozinhas no fechamento da conta de quem está na categoria, sem pedir gerente.
        A categoria de cada cliente é escolhida no cadastro do cliente.
      </p>
      <div className="filtros"><button onClick={() => setF({ ...vazio })}>+ Nova categoria</button></div>

      {f && (
        <form className="cartao" onSubmit={enviar}>
          <h3>{f.id ? 'Editar categoria' : 'Nova categoria'}</h3>
          <div className="grade">
            <label>Nome*<input value={f.nome} onChange={e => set('nome', e.target.value)} maxLength={60} required disabled={ehPadrao} autoFocus /></label>
            <label>Desconto padrão (%)
              <input type="number" min="0" max="100" step="0.01" value={f.desconto} onChange={e => set('desconto', e.target.value)} />
              <small>sobre o consumo, não sobre as taxas</small>
            </label>
            <label>Limite da comanda (R$)
              <input type="number" min="0.01" step="0.01" value={f.limite} onChange={e => set('limite', e.target.value)} placeholder="vazio = limite geral" />
            </label>
          </div>
          <fieldset className="isencoes">
            <legend>Isenta de</legend>
            <label className="check"><input type="checkbox" checked={f.isentaEstacionamento} onChange={e => set('isentaEstacionamento', e.target.checked)} /> Estacionamento (terra)</label>
            <label className="check"><input type="checkbox" checked={f.isentaEmbarque} onChange={e => set('isentaEmbarque', e.target.checked)} /> Embarque (rio)</label>
            <label className="check"><input type="checkbox" checked={f.isentaCouvert} onChange={e => set('isentaCouvert', e.target.checked)} /> Couvert</label>
            <label className="check"><input type="checkbox" checked={f.isentaTaxaServico} onChange={e => set('isentaTaxaServico', e.target.checked)} /> Taxa de serviço</label>
          </fieldset>
          {f.id && !ehPadrao && <label className="check"><input type="checkbox" checked={f.ativo} onChange={e => set('ativo', e.target.checked)} /> Ativa</label>}
          {ehPadrao && <p className="dica">{PADRAO} é a categoria dos clientes novos: as regras podem mudar, o nome não.</p>}
          <div className="acoes">
            <button type="submit" disabled={enviando}>{enviando ? 'Salvando…' : 'Salvar'}</button>
            <button type="button" className="sec" onClick={() => setF(null)}>Cancelar</button>
          </div>
        </form>
      )}

      <table>
        <thead><tr><th>Categoria</th><th>Isenta de</th><th className="num">Desconto</th><th className="num">Limite</th><th className="num">Clientes</th><th>Situação</th><th></th></tr></thead>
        <tbody>
          {lista.map(k => (
            <tr key={k.id} className={k.ativo === 'S' ? '' : 'inativo'}>
              <td>{k.nome}</td>
              <td>{regras(k) || '—'}</td>
              <td className="num">{k.descontoPctPadrao > 0 ? `${k.descontoPctPadrao.toLocaleString('pt-BR')}%` : '—'}</td>
              <td className="num">{k.limiteComanda == null ? 'geral' : brl(k.limiteComanda)}</td>
              <td className="num">{k.clientes.toLocaleString('pt-BR')}</td>
              <td>{k.ativo === 'S' ? 'Ativa' : 'Inativa'}</td>
              <td className="acoes-linha">
                <button className="sec" onClick={() => setF(doCadastro(k))}>Editar</button>
                {k.nome !== PADRAO && (
                  <button className="sec" disabled={enviando || (k.ativo === 'S' && k.clientes > 0)}
                    title={k.ativo === 'S' && k.clientes > 0 ? 'Mude a categoria dos clientes antes de inativar' : undefined}
                    onClick={() => alternar(k)}>{k.ativo === 'S' ? 'Inativar' : 'Reativar'}</button>
                )}
              </td>
            </tr>
          ))}
          {lista.length === 0 && <tr><td colSpan={7} className="vazio">Nenhuma categoria cadastrada.</td></tr>}
        </tbody>
      </table>
    </div>
  )
}
