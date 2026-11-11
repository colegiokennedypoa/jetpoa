import { useEffect, useState, type FormEvent } from 'react'
import { api, type Eu, type Perfil } from '../api'

/** Tela de entrada. Sem nenhum usuário cadastrado, mostra o cadastro do primeiro Administrador. */
export default function Login({ onEntrou, aviso }: { onEntrou: (eu: Eu) => void; aviso?: string }) {
  const [primeiro, setPrimeiro] = useState<boolean | null>(null)
  const [login, setLogin] = useState('')
  const [senha, setSenha] = useState('')
  const [nome, setNome] = useState('')
  const [confirma, setConfirma] = useState('')
  const [erro, setErro] = useState<string | undefined>(aviso)
  const [enviando, setEnviando] = useState(false)

  useEffect(() => { api.primeiroAcesso().then(r => setPrimeiro(r.semUsuarios)).catch(() => setPrimeiro(false)) }, [])

  async function entrar(e: FormEvent) {
    e.preventDefault()
    setEnviando(true); setErro(undefined)
    try { onEntrou(await api.login(login, senha)) }
    catch (x) { setErro((x as Error).message); setSenha('') }
    finally { setEnviando(false) }
  }

  async function criarAdmin(e: FormEvent) {
    e.preventDefault()
    if (senha !== confirma) { setErro('As senhas não conferem.'); return }
    setEnviando(true); setErro(undefined)
    try {
      const perfis: Perfil[] = await api.perfis()
      const adm = perfis.find(p => p.nome === 'Administrador')
      if (!adm) throw new Error('Perfil Administrador não encontrado.')
      await api.salvarUsuario(null, { nome, login, perfilId: adm.id, senha, pin: null, ativo: true })
      onEntrou(await api.login(login, senha))
    } catch (x) { setErro((x as Error).message) }
    finally { setEnviando(false) }
  }

  return (
    <div className="login-fundo">
      <form className="login-caixa" onSubmit={primeiro ? criarAdmin : entrar}>
        <h1>JetPOA · Bistrô</h1>
        {primeiro && <p className="dica">Nenhum usuário cadastrado. Crie o primeiro Administrador.</p>}
        {primeiro && (
          <label>Nome<input value={nome} onChange={e => setNome(e.target.value)} required autoFocus /></label>
        )}
        <label>Usuário
          <input value={login} onChange={e => setLogin(e.target.value)} autoComplete="username" autoCapitalize="none" required autoFocus={!primeiro} />
        </label>
        <label>Senha
          <input type="password" value={senha} onChange={e => setSenha(e.target.value)} autoComplete={primeiro ? 'new-password' : 'current-password'} required minLength={primeiro ? 6 : undefined} />
        </label>
        {primeiro && (
          <label>Confirme a senha
            <input type="password" value={confirma} onChange={e => setConfirma(e.target.value)} autoComplete="new-password" required />
          </label>
        )}
        {erro && <div className="msg erro">{erro}</div>}
        <button className="enviar" disabled={enviando || primeiro === null}>{primeiro ? 'Criar e entrar' : 'Entrar'}</button>
      </form>
    </div>
  )
}

/** Troca de senha: obrigatória no 1º acesso (senha provisória) ou a pedido do usuário. */
export function TrocarSenha({ obrigatoria, onTrocou, onCancelar }: { obrigatoria: boolean; onTrocou: (eu: Eu) => void; onCancelar?: () => void }) {
  const [atual, setAtual] = useState('')
  const [nova, setNova] = useState('')
  const [confirma, setConfirma] = useState('')
  const [erro, setErro] = useState<string>()
  const [enviando, setEnviando] = useState(false)

  async function salvar(e: FormEvent) {
    e.preventDefault()
    if (nova !== confirma) { setErro('As senhas não conferem.'); return }
    setEnviando(true); setErro(undefined)
    try { onTrocou(await api.trocarSenha(atual, nova)) }
    catch (x) { setErro((x as Error).message) }
    finally { setEnviando(false) }
  }

  return (
    <div className="login-fundo">
      <form className="login-caixa" onSubmit={salvar}>
        <h1>Trocar senha</h1>
        {obrigatoria && <p className="dica">Sua senha é provisória. Escolha uma senha pessoal (mínimo 6 caracteres) para continuar.</p>}
        <label>Senha atual<input type="password" value={atual} onChange={e => setAtual(e.target.value)} autoComplete="current-password" required autoFocus /></label>
        <label>Nova senha<input type="password" value={nova} onChange={e => setNova(e.target.value)} autoComplete="new-password" required minLength={6} /></label>
        <label>Confirme a nova senha<input type="password" value={confirma} onChange={e => setConfirma(e.target.value)} autoComplete="new-password" required /></label>
        {erro && <div className="msg erro">{erro}</div>}
        <button className="enviar" disabled={enviando}>Salvar senha</button>
        {onCancelar && <button type="button" className="sec" onClick={onCancelar}>{obrigatoria ? 'Sair' : 'Cancelar'}</button>}
      </form>
    </div>
  )
}
