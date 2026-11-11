import { useEffect, useMemo, useState } from 'react'
import { api, EVENTO_SESSAO_EXPIRADA, P, type Eu } from './api'
import { SessaoContext, criarSessao } from './sessao'
import Login, { TrocarSenha } from './pages/Login'
import Comandas from './pages/Comandas'
import Garcom from './pages/Garcom'
import Producao from './pages/Producao'
import Produtos from './pages/Produtos'
import Clientes from './pages/Clientes'
import Usuarios from './pages/Usuarios'
import Fiscal from './pages/Fiscal'
import Checkin from './pages/Checkin'
import Estoque from './pages/Estoque'
import Relatorios from './pages/Relatorios'
import Implantacao from './pages/Implantacao'
import './App.css'

/** Abas e a permissão necessária para ver cada uma. */
const ABAS = {
  checkin: { nome: 'Check-in', perm: P.checkin },
  comandas: { nome: 'Comandas', perm: P.comandaAbrir },
  garcom: { nome: 'Garçom', perm: P.comandaLancar },
  producao: { nome: 'Produção', perm: P.producaoVer },
  fiscal: { nome: 'Fiscal', perm: P.fiscalVer },
  estoque: { nome: 'Estoque', perm: P.estoqueVer },
  relatorios: { nome: 'Relatórios', perm: P.relatorios },
  clientes: { nome: 'Clientes', perm: P.clientesVer },
  produtos: { nome: 'Produtos', perm: P.produtosEditar },
  usuarios: { nome: 'Usuários', perm: P.usuarios },
  implantacao: { nome: 'Implantação', perm: P.implantacao },
} as const
type Aba = keyof typeof ABAS

const hashAba = () => window.location.hash.replace('#', '')

export default function App() {
  const [eu, setEu] = useState<Eu | null | undefined>(undefined)   // undefined = verificando
  const [aviso, setAviso] = useState<string>()
  const [trocandoSenha, setTrocandoSenha] = useState(false)
  const [hash, setHash] = useState(hashAba)
  const [banco, setBanco] = useState('verificando…')

  useEffect(() => {
    api.eu().then(setEu).catch(() => setEu(null))
    api.health().then(h => setBanco(`Oracle ${h.banco}`)).catch(() => setBanco('sem conexão'))
    const onHash = () => setHash(hashAba())
    const onExpirou = () => { setEu(null); setAviso('Sua sessão terminou. Entre novamente.') }
    window.addEventListener('hashchange', onHash)
    window.addEventListener(EVENTO_SESSAO_EXPIRADA, onExpirou)
    return () => { window.removeEventListener('hashchange', onHash); window.removeEventListener(EVENTO_SESSAO_EXPIRADA, onExpirou) }
  }, [])

  const sessao = useMemo(() => (eu ? criarSessao(eu) : null), [eu])
  const abas = useMemo(() => (Object.keys(ABAS) as Aba[]).filter(a => sessao?.pode(ABAS[a].perm)), [sessao])

  if (eu === undefined) return <div className="login-fundo"><p>Carregando…</p></div>
  if (!eu || !sessao) return <Login aviso={aviso} onEntrou={u => { setAviso(undefined); setEu(u) }} />

  const sair = async () => { try { await api.sair() } catch { /* já saiu */ } setEu(null); setAviso(undefined) }

  if (eu.precisaTrocarSenha || trocandoSenha)
    return <TrocarSenha obrigatoria={eu.precisaTrocarSenha} onTrocou={u => { setTrocandoSenha(false); setEu(u) }}
      onCancelar={eu.precisaTrocarSenha ? sair : () => setTrocandoSenha(false)} />

  const aba: Aba | undefined = abas.includes(hash as Aba) ? (hash as Aba)
    : abas.includes('comandas') ? 'comandas' : abas.includes('garcom') ? 'garcom' : abas[0]
  const ir = (a: Aba) => { window.location.hash = a; setHash(a) }

  return (
    <SessaoContext.Provider value={sessao}>
      <div className="app">
        <header className="topo">
          <strong>JetPOA · Bistrô</strong>
          <nav>
            {abas.map(a => (
              <button key={a} className={aba === a ? 'ativo' : ''} onClick={() => ir(a)}>{ABAS[a].nome}</button>
            ))}
          </nav>
          <span className="usuario-logado" title={`${eu.login} · ${banco}`}>
            {eu.nome} <small>({eu.perfil})</small>
            <button onClick={() => setTrocandoSenha(true)}>Senha</button>
            <button onClick={sair}>Sair</button>
          </span>
        </header>
        <main>
          {!aba && <p className="vazio">Seu perfil não tem acesso a nenhuma tela. Fale com o gerente.</p>}
          {aba === 'checkin' && <Checkin />}
          {aba === 'estoque' && <Estoque />}
          {aba === 'relatorios' && <Relatorios />}
          {aba === 'comandas' && <Comandas />}
          {aba === 'garcom' && <Garcom />}
          {aba === 'producao' && <Producao />}
          {aba === 'fiscal' && <Fiscal />}
          {aba === 'clientes' && <Clientes />}
          {aba === 'produtos' && <Produtos />}
          {aba === 'usuarios' && <Usuarios />}
          {aba === 'implantacao' && <Implantacao />}
        </main>
      </div>
    </SessaoContext.Provider>
  )
}
