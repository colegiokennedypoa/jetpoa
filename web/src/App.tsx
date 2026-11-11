import { useEffect, useState } from 'react'
import { api } from './api'
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

const ABAS = {
  checkin: 'Check-in',
  comandas: 'Comandas',
  garcom: 'Garçom',
  producao: 'Produção',
  fiscal: 'Fiscal',
  estoque: 'Estoque',
  relatorios: 'Relatórios',
  clientes: 'Clientes',
  produtos: 'Produtos',
  usuarios: 'Usuários',
  implantacao: 'Implantação',
} as const
type Aba = keyof typeof ABAS

function abaInicial(): Aba {
  const h = window.location.hash.replace('#', '')
  return h in ABAS ? (h as Aba) : 'comandas'
}

export default function App() {
  const [aba, setAba] = useState<Aba>(abaInicial)
  const [banco, setBanco] = useState<string>('verificando…')

  useEffect(() => {
    api.health().then(h => setBanco(`Oracle ${h.banco}`)).catch(() => setBanco('sem conexão'))
    const onHash = () => setAba(abaInicial())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  const ir = (a: Aba) => { setAba(a); window.location.hash = a }

  return (
    <div className="app">
      <header className="topo">
        <strong>JetPOA · Bistrô</strong>
        <nav>
          {(Object.keys(ABAS) as Aba[]).map(a => (
            <button key={a} className={aba === a ? 'ativo' : ''} onClick={() => ir(a)}>{ABAS[a]}</button>
          ))}
        </nav>
        <span className="status">{banco}</span>
      </header>
      <main>
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
  )
}
