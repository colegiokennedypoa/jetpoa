import { useEffect, useState, type FormEvent } from 'react'
import { api, type Perfil, type Usuario } from '../api'

type Form = { nome: string; login: string; perfilId: number | null; pin: string; ativo: boolean }
const vazio: Form = { nome: '', login: '', perfilId: null, pin: '', ativo: true }

/** Cadastro de usuários (operadores e autorizadores). Alterações exigem PIN de um Administrador. */
export default function Usuarios() {
  const [lista, setLista] = useState<Usuario[]>([])
  const [perfis, setPerfis] = useState<Perfil[]>([])
  const [editId, setEditId] = useState<number | null>(null)
  const [form, setForm] = useState<Form | null>(null)
  const [adminId, setAdminId] = useState<number | null>(null)
  const [adminPin, setAdminPin] = useState('')
  const [msg, setMsg] = useState<{ tipo: 'ok' | 'erro'; texto: string }>()

  const carregar = () => api.usuarios().then(setLista).catch(e => setMsg({ tipo: 'erro', texto: e.message }))
  useEffect(() => { carregar(); api.perfis().then(setPerfis).catch(() => {}) }, [])

  const primeiro = lista.length === 0
  const admins = lista.filter(u => u.perfil === 'Administrador' && u.ativo === 'S')

  const novo = () => {
    setEditId(null)
    setForm({ ...vazio, perfilId: primeiro ? perfis.find(p => p.nome === 'Administrador')?.id ?? null : perfis.find(p => p.nome === 'Caixa')?.id ?? null })
    setMsg(undefined)
  }
  const editar = (u: Usuario) => {
    setEditId(u.id)
    setForm({ nome: u.nome, login: u.login, perfilId: u.perfilId, pin: '', ativo: u.ativo === 'S' })
    setMsg(undefined)
  }

  const salvar = async (e: FormEvent) => {
    e.preventDefault()
    if (!form?.perfilId) return
    try {
      await api.salvarUsuario(editId, {
        nome: form.nome, login: form.login, perfilId: form.perfilId, pin: form.pin || null, ativo: form.ativo,
        admin: primeiro ? null : adminId ? { usuarioId: adminId, pin: adminPin } : null,
      })
      setMsg({ tipo: 'ok', texto: 'Usuário salvo.' })
      setForm(null); setAdminPin('')
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
      {primeiro && !form && <p className="alerta-box">Nenhum usuário cadastrado. Crie primeiro o <b>Administrador</b>; os demais exigirão o PIN dele.</p>}
      {msg && <p className={msg.tipo}>{msg.texto}</p>}

      {form && (
        <form className="cartao" onSubmit={salvar}>
          <h3>{editId ? 'Editar usuário' : primeiro ? 'Primeiro usuário (Administrador)' : 'Novo usuário'}</h3>
          <div className="grade">
            <label>Nome*<input value={form.nome} onChange={e => set('nome', e.target.value)} required /></label>
            <label>Login*<input value={form.login} onChange={e => set('login', e.target.value)} required minLength={3} /></label>
            <label>Perfil
              <select value={form.perfilId ?? ''} onChange={e => set('perfilId', Number(e.target.value))} disabled={primeiro}>
                {perfis.map(p => <option key={p.id} value={p.id}>{p.nome} · desconto até {p.limiteDescontoPct}%{p.autorizaDesconto === 'S' ? ' · autoriza' : ''}</option>)}
              </select>
            </label>
            <label>{editId ? 'Novo PIN (deixe vazio p/ manter)' : 'PIN* (4 a 8 dígitos)'}
              <input type="password" inputMode="numeric" autoComplete="new-password" value={form.pin}
                onChange={e => set('pin', e.target.value.replace(/\D/g, ''))} required={!editId} minLength={4} maxLength={8} />
            </label>
          </div>
          {editId && <label className="check"><input type="checkbox" checked={form.ativo} onChange={e => set('ativo', e.target.checked)} /> Ativo</label>}
          {!primeiro && (
            <div className="grade autorizacao">
              <label>Administrador que autoriza
                <select value={adminId ?? ''} onChange={e => setAdminId(Number(e.target.value) || null)} required>
                  <option value="">Selecione…</option>
                  {admins.map(a => <option key={a.id} value={a.id}>{a.nome}</option>)}
                </select>
              </label>
              <label>PIN do administrador
                <input type="password" inputMode="numeric" autoComplete="off" value={adminPin} onChange={e => setAdminPin(e.target.value.replace(/\D/g, ''))} required />
              </label>
            </div>
          )}
          <div className="acoes">
            <button type="submit">Salvar</button>
            <button type="button" className="sec" onClick={() => setForm(null)}>Cancelar</button>
          </div>
        </form>
      )}

      <table>
        <thead><tr><th>Nome</th><th>Login</th><th>Perfil</th><th className="num">Desconto até</th><th>Autoriza</th><th></th></tr></thead>
        <tbody>
          {lista.map(u => (
            <tr key={u.id} className={u.ativo === 'S' ? '' : 'inativo'}>
              <td>{u.nome}</td><td>{u.login}</td><td>{u.perfil}</td><td className="num">{u.limiteDescontoPct}%</td>
              <td>{u.autorizaDesconto === 'S' ? 'Sim' : 'Não'}</td>
              <td><button className="sec" onClick={() => editar(u)}>Editar</button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
