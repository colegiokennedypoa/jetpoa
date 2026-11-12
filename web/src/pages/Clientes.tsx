import { useEffect, useState, type FormEvent } from 'react'
import { api, P, type Cliente, type ClienteCategoria, type ClienteInput } from '../api'
import { useSessao } from '../sessao'
import { cpfValido, erroCpf, mascaraCpf, soDigitos } from '../documento'

const vazio: ClienteInput = {
  cpf: null, nome: '', celular: null, email: null, dataNascimento: null, sexo: null,
  categoriaId: null, aceitaComunicacao: false, observacao: null, ativo: true,
}

const fmtCpf = (v: string | null) => (v ? mascaraCpf(v) : '—')
const fmtCel = (v: string | null) => v?.replace(/(\d{2})(\d{4,5})(\d{4})$/, '($1) $2-$3') ?? '—'

export default function Clientes() {
  const podeEditar = useSessao().pode(P.clientesEditar)
  const [categorias, setCategorias] = useState<ClienteCategoria[]>([])
  const [busca, setBusca] = useState('')
  const [lista, setLista] = useState<Cliente[]>([])
  const [editId, setEditId] = useState<number | null>(null)
  const [form, setForm] = useState<ClienteInput | null>(null)
  const [msg, setMsg] = useState<{ tipo: 'erro' | 'ok'; texto: string }>()

  const [cpfDono, setCpfDono] = useState<Cliente | null>(null)

  useEffect(() => { api.clienteCategorias().then(setCategorias).catch(() => {}) }, [])

  // CPF não pode duplicar: avisa enquanto digita
  const cpfDigitado = soDigitos(form?.cpf)
  const [cpfTocado, setCpfTocado] = useState(false)
  const cpfErro = erroCpf(form?.cpf, cpfTocado)
  useEffect(() => {
    if (!cpfValido(cpfDigitado)) { setCpfDono(null); return }
    let vivo = true
    api.clientePorCpf(cpfDigitado).then(c => { if (vivo) setCpfDono(c && c.id !== editId ? c : null) })
    return () => { vivo = false }
  }, [cpfDigitado, editId])

  const carregar = () => api.clientes(busca).then(setLista).catch(e => setMsg({ tipo: 'erro', texto: e.message }))
  useEffect(() => { const t = setTimeout(carregar, 250); return () => clearTimeout(t) }, [busca]) // eslint-disable-line react-hooks/exhaustive-deps

  const novo = () => { setEditId(null); setForm({ ...vazio }); setMsg(undefined); setCpfTocado(false) }
  const editar = (c: Cliente) => {
    setEditId(c.id); setCpfTocado(false)
    setForm({
      cpf: c.cpf, nome: c.nome, celular: c.celular, email: c.email,
      dataNascimento: c.dataNascimento?.substring(0, 10) ?? null, sexo: c.sexo,
      categoriaId: c.categoriaId, aceitaComunicacao: c.aceitaComunicacao === 'S',
      observacao: c.observacao, ativo: c.ativo === 'S',
    })
    setMsg(undefined)
  }

  const salvar = async (e: FormEvent) => {
    e.preventDefault()
    if (!form) return
    const e1 = erroCpf(form.cpf)
    if (e1) { setCpfTocado(true); setMsg({ tipo: 'erro', texto: e1 }); return }
    const dados = { ...form, cpf: soDigitos(form.cpf) || null }
    try {
      if (editId) await api.atualizarCliente(editId, dados)
      else await api.criarCliente(dados)
      setMsg({ tipo: 'ok', texto: 'Cliente salvo.' })
      setForm(null)
      carregar()
    } catch (err) {
      setMsg({ tipo: 'erro', texto: (err as Error).message })
    }
  }

  const set = <K extends keyof ClienteInput>(k: K, v: ClienteInput[K]) => setForm(f => f && { ...f, [k]: v })
  const txt = (v: string) => (v.trim() === '' ? null : v)

  return (
    <section className="clientes">
      <div className="filtros">
        <input placeholder="Buscar por nome, CPF ou celular…" value={busca} onChange={e => setBusca(e.target.value)} autoFocus />
        {podeEditar && <button onClick={novo}>+ Novo cliente</button>}
        <span className="contador">{lista.length} encontrados</span>
      </div>
      {msg && <p className={msg.tipo}>{msg.texto}</p>}

      {form && (
        <form className="cartao" onSubmit={salvar}>
          <h3>{editId ? 'Editar cliente' : 'Novo cliente'}</h3>
          <div className="grade">
            <label>Nome*<input value={form.nome} onChange={e => set('nome', e.target.value)} required /></label>
            <label>CPF<input value={mascaraCpf(form.cpf)} onChange={e => set('cpf', txt(mascaraCpf(e.target.value)))}
              onBlur={() => setCpfTocado(true)} inputMode="numeric" placeholder="000.000.000-00" maxLength={14}
              className={cpfErro ? 'invalido' : ''} aria-invalid={!!cpfErro} />
              {cpfErro && <small className="erro-campo">{cpfErro}</small>}</label>
            <label>Celular (com DDD)<input value={form.celular ?? ''} onChange={e => set('celular', txt(e.target.value))} inputMode="tel" /></label>
            <label>E-mail<input type="email" value={form.email ?? ''} onChange={e => set('email', txt(e.target.value))} /></label>
            <label>Nascimento<input type="date" value={form.dataNascimento ?? ''} onChange={e => set('dataNascimento', txt(e.target.value))} /></label>
            <label>Sexo
              <select value={form.sexo ?? ''} onChange={e => set('sexo', txt(e.target.value))}>
                <option value="">—</option><option value="F">Feminino</option><option value="M">Masculino</option><option value="O">Outro</option>
              </select>
            </label>
            <label>Categoria
              <select value={form.categoriaId ?? ''} onChange={e => set('categoriaId', e.target.value ? Number(e.target.value) : null)}>
                <option value="">Normal</option>
                {categorias.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}
              </select>
            </label>
            <label className="largo">Observação<input value={form.observacao ?? ''} onChange={e => set('observacao', txt(e.target.value))} /></label>
          </div>
          {cpfDono && (
            <p className="alerta-box">CPF já cadastrado para <b>{cpfDono.nome}</b> (cliente nº {cpfDono.id}).{' '}
              <button type="button" className="sec" onClick={() => editar(cpfDono)}>Abrir este cadastro</button></p>
          )}
          <label className="check"><input type="checkbox" checked={form.aceitaComunicacao} onChange={e => set('aceitaComunicacao', e.target.checked)} /> Aceita receber comunicações (LGPD)</label>
          {editId && <label className="check"><input type="checkbox" checked={form.ativo} onChange={e => set('ativo', e.target.checked)} /> Ativo</label>}
          <div className="acoes">
            <button type="submit" disabled={!!cpfDono || !!erroCpf(form.cpf, false)}>Salvar</button>
            <button type="button" className="sec" onClick={() => setForm(null)}>Cancelar</button>
          </div>
        </form>
      )}

      <table>
        <thead><tr><th>Nome</th><th>CPF</th><th>Celular</th><th>Categoria</th><th>LGPD</th><th></th></tr></thead>
        <tbody>
          {lista.map(c => (
            <tr key={c.id} className={c.ativo === 'S' ? '' : 'inativo'}>
              <td>{c.nome}</td><td>{fmtCpf(c.cpf)}</td><td>{fmtCel(c.celular)}</td><td>{c.categoria}</td>
              <td>{c.aceitaComunicacao === 'S' ? 'Sim' : 'Não'}</td>
              <td>{podeEditar && <button className="sec" onClick={() => editar(c)}>Editar</button>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
