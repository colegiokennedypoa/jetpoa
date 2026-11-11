import { useEffect, useState, type FormEvent } from 'react'
import { api, type Garcom, type Perfil, type Usuario } from '../api'
import { useSessao } from '../sessao'

type Form = { nome: string; login: string; perfilId: number | null; senha: string; pin: string; ativo: boolean; garcomId: number | null }
const vazio: Form = { nome: '', login: '', perfilId: null, senha: '', pin: '', ativo: true, garcomId: null }

/**
 * Cadastro de usuários. Cada pessoa entra com login e senha; a senha definida aqui é provisória
 * (troca obrigatória no 1º acesso). O PIN é opcional: serve para gerentes autorizarem no caixa de outra pessoa.
 */
export default function Usuarios() {
  const { eu } = useSessao()
  const [lista, setLista] = useState<Usuario[]>([])
  const [perfis, setPerfis] = useState<Perfil[]>([])
  const [garcons, setGarcons] = useState<Garcom[]>([])
  const [editId, setEditId] = useState<number | null>(null)
  const [form, setForm] = useState<Form | null>(null)
  const [msg, setMsg] = useState<{ tipo: 'ok' | 'erro'; texto: string }>()

  const carregar = () => api.usuarios().then(setLista).catch(e => setMsg({ tipo: 'erro', texto: e.message }))
  const carregarGarcons = () => api.garcons().then(setGarcons).catch(() => {})
  useEffect(() => { carregar(); carregarGarcons(); api.perfis().then(setPerfis).catch(() => {}) }, [])

  const perfilSel = perfis.find(p => p.id === form?.perfilId)
  const ehGarcom = perfilSel?.nome === 'Garçom'
  // garçons livres (sem login) + o do próprio usuário em edição
  const garconsLivres = garcons.filter(g => g.ativo === 'S'
    && !lista.some(u => u.garcomId === g.id && u.id !== editId))

  const novo = () => {
    setEditId(null)
    setForm({ ...vazio, perfilId: perfis.find(p => p.nome === 'Caixa')?.id ?? null })
    setMsg(undefined)
  }
  const editar = (u: Usuario) => {
    setEditId(u.id)
    setForm({ nome: u.nome, login: u.login, perfilId: u.perfilId, senha: '', pin: '', ativo: u.ativo === 'S', garcomId: u.garcomId })
    setMsg(undefined)
  }

  const salvar = async (e: FormEvent) => {
    e.preventDefault()
    if (!form?.perfilId) return
    try {
      await api.salvarUsuario(editId, {
        nome: form.nome, login: form.login, perfilId: form.perfilId,
        senha: form.senha || null, pin: form.pin || null, ativo: form.ativo, garcomId: form.garcomId,
      })
      carregarGarcons()
      setMsg({ tipo: 'ok', texto: form.senha
        ? `Usuário salvo. Informe a senha provisória a ${form.nome}: será pedida a troca no primeiro acesso.`
        : 'Usuário salvo.' })
      setForm(null)
      carregar()
    } catch (err) {
      setMsg({ tipo: 'erro', texto: (err as Error).message })
    }
  }

  const set = <K extends keyof Form>(k: K, v: Form[K]) => setForm(f => f && { ...f, [k]: v })

  return (
    <section className="clientes">
      <div className="filtros">
        <button onClick={novo}>+ Novo usuário</button>
        <span className="contador">{lista.length} usuários</span>
      </div>
      {msg && <p className={msg.tipo}>{msg.texto}</p>}

      {form && (
        <form className="cartao" onSubmit={salvar}>
          <h3>{editId ? 'Editar usuário' : 'Novo usuário'}</h3>
          <div className="grade">
            <label>Nome*<input value={form.nome} onChange={e => set('nome', e.target.value)} required /></label>
            <label>Login*<input value={form.login} onChange={e => set('login', e.target.value)} required minLength={3} autoCapitalize="none" /></label>
            <label>Perfil
              <select value={form.perfilId ?? ''} onChange={e => set('perfilId', Number(e.target.value))}>
                {perfis.map(p => <option key={p.id} value={p.id}>{p.nome} · desconto até {p.limiteDescontoPct}%{p.autorizaDesconto === 'S' ? ' · autoriza' : ''}</option>)}
              </select>
            </label>
            <label>{editId ? 'Redefinir senha (vazio = manter)' : 'Senha provisória* (mín. 6)'}
              <input type="text" autoComplete="off" value={form.senha} onChange={e => set('senha', e.target.value)} required={!editId} minLength={6} />
            </label>
            <label>{ehGarcom ? 'Garçom (lança sempre em nome dele)' : 'Garçom vinculado (opcional)'}
              <select value={form.garcomId ?? ''} onChange={e => set('garcomId', e.target.value ? Number(e.target.value) : null)}>
                <option value="">{ehGarcom ? '— novo garçom com o nome do usuário —' : '— nenhum —'}</option>
                {garconsLivres.map(g => <option key={g.id} value={g.id}>{g.nome}</option>)}
              </select>
            </label>
            {perfilSel?.autorizaDesconto === 'S' && (
              <label>{editId ? 'Novo PIN de autorização (vazio = manter)' : 'PIN de autorização (4 a 8 dígitos)'}
                <input type="password" inputMode="numeric" autoComplete="new-password" value={form.pin}
                  onChange={e => set('pin', e.target.value.replace(/\D/g, ''))} minLength={4} maxLength={8} />
              </label>
            )}
          </div>
          {perfilSel?.autorizaDesconto === 'S' && (
            <p className="obs">O PIN é usado quando este gerente libera, no caixa de outra pessoa, desconto acima do limite, consumo interno ou cancelamento.</p>
          )}
          {editId && editId !== eu.id && <label className="check"><input type="checkbox" checked={form.ativo} onChange={e => set('ativo', e.target.checked)} /> Ativo</label>}
          {editId && <p className="obs">Redefinir a senha, mudar o perfil ou desativar encerra as sessões abertas desse usuário.</p>}
          <div className="acoes">
            <button type="submit">Salvar</button>
            <button type="button" className="sec" onClick={() => setForm(null)}>Cancelar</button>
          </div>
        </form>
      )}

      <table>
        <thead><tr><th>Nome</th><th>Login</th><th>Perfil</th><th>Garçom</th><th className="num">Desconto até</th><th>PIN</th><th>Último acesso</th><th></th></tr></thead>
        <tbody>
          {lista.map(u => (
            <tr key={u.id} className={u.ativo === 'S' ? '' : 'inativo'}>
              <td>{u.nome}{u.precisaTrocarSenha === 'S' && <small className="obs"> · senha provisória</small>}</td>
              <td>{u.login}</td><td>{u.perfil}</td>
              <td>{u.garcom ?? (u.perfil === 'Garçom' ? <span className="alerta">sem vínculo</span> : '—')}</td><td className="num">{u.limiteDescontoPct}%</td>
              <td>{u.autorizaDesconto === 'S' ? (u.temPin === 'S' ? 'Sim' : <span className="alerta">sem PIN</span>) : '—'}</td>
              <td>{u.ultimoLogin ? new Date(u.ultimoLogin).toLocaleString('pt-BR') : '—'}</td>
              <td><button className="sec" onClick={() => editar(u)}>Editar</button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
