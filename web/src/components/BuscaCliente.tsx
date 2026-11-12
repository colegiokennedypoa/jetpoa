import { useEffect, useState } from 'react'
import { api, type Cliente } from '../api'
import { cpfValido, erroCpf, mascaraCpf, soDigitos } from '../documento'

/**
 * Seleção obrigatória de cliente: busca no cadastro (nome, CPF ou celular) ou cadastro rápido.
 * Regra do JetPOA: comanda só para cliente cadastrado; CPF nunca duplica.
 */
export default function BuscaCliente({ cliente, onCliente, autoFocus }: {
  cliente: Cliente | null; onCliente: (c: Cliente | null) => void; autoFocus?: boolean
}) {
  const [busca, setBusca] = useState('')
  const [sugestoes, setSugestoes] = useState<Cliente[]>([])
  const [novo, setNovo] = useState(false)
  const [n, setN] = useState({ nome: '', cpf: '', celular: '', nasc: '', aceita: false })
  const [cpfDono, setCpfDono] = useState<Cliente | null>(null)
  const [erro, setErro] = useState<string>()
  const [salvando, setSalvando] = useState(false)
  const [cpfTocado, setCpfTocado] = useState(false)
  const cpfErro = erroCpf(n.cpf, cpfTocado)

  // cliente escolhido (ou limpo pela tela após o check-in): a busca começa do zero
  useEffect(() => { setBusca(''); setSugestoes([]); setNovo(false); setErro(undefined) }, [cliente])

  useEffect(() => {
    if (cliente || novo || busca.trim().length < 3) { setSugestoes([]); return }
    const t = setTimeout(() => api.clientes(busca).then(r => setSugestoes(r.filter(c => c.ativo === 'S').slice(0, 8))).catch(() => {}), 250)
    return () => clearTimeout(t)
  }, [busca, cliente, novo])

  // CPF digitado no cadastro rápido: avisa na hora se já existe
  useEffect(() => {
    const cpf = soDigitos(n.cpf)
    if (!novo || !cpfValido(cpf)) { setCpfDono(null); return }
    let vivo = true
    api.clientePorCpf(cpf).then(c => { if (vivo) setCpfDono(c) })
    return () => { vivo = false }
  }, [n.cpf, novo])

  const abrirNovo = () => {
    const d = soDigitos(busca)
    setN({ nome: d.length >= 8 ? '' : busca.trim(), cpf: d.length === 11 ? mascaraCpf(d) : '', celular: d.length >= 10 && d.length !== 11 ? d : '', nasc: '', aceita: false })
    setNovo(true); setErro(undefined); setCpfTocado(d.length === 11)
  }

  const cadastrar = async () => {
    setErro(undefined)
    if (n.nome.trim().length < 2) { setErro('Informe o nome.'); return }
    if (!soDigitos(n.cpf) && !soDigitos(n.celular)) { setErro('Informe CPF ou celular.'); return }
    const e1 = erroCpf(n.cpf)
    if (e1) { setCpfTocado(true); setErro(e1); return }
    if (cpfDono) return
    setSalvando(true)
    try {
      const r = await api.criarCliente({
        cpf: soDigitos(n.cpf) || null, nome: n.nome.trim(), celular: soDigitos(n.celular) || null, email: null,
        dataNascimento: n.nasc || null, sexo: null, categoriaId: null, aceitaComunicacao: n.aceita, observacao: null, ativo: true,
      })
      onCliente(await api.cliente(r.id))
      setNovo(false); setBusca('')
    } catch (e) { setErro((e as Error).message) } finally { setSalvando(false) }
  }

  if (cliente) {
    return (
      <span className="cliente-sel">
        <b>{cliente.nome}</b>{cliente.categoria ? ` · ${cliente.categoria}` : ''}{cliente.cpf ? ` · CPF ***${cliente.cpf.slice(-4)}` : ''}
        <button type="button" className="sec" onClick={() => { onCliente(null); setBusca('') }}>trocar</button>
      </span>
    )
  }

  if (novo) {
    return (
      <div className="cadastro-rapido">
        <div className="grade">
          <label>Nome*<input value={n.nome} onChange={e => setN({ ...n, nome: e.target.value })} autoFocus /></label>
          <label>CPF<input value={n.cpf} onChange={e => setN({ ...n, cpf: mascaraCpf(e.target.value) })}
            onBlur={() => setCpfTocado(true)} inputMode="numeric" placeholder="000.000.000-00" maxLength={14}
            className={cpfErro ? 'invalido' : ''} aria-invalid={!!cpfErro} />
            {cpfErro && <small className="erro-campo">{cpfErro}</small>}</label>
          <label>Celular (DDD)<input value={n.celular} onChange={e => setN({ ...n, celular: e.target.value })} inputMode="tel" /></label>
          <label>Nascimento<input type="date" value={n.nasc} max={new Date().toISOString().slice(0, 10)} onChange={e => setN({ ...n, nasc: e.target.value })} /></label>
        </div>
        <label className="check"><input type="checkbox" checked={n.aceita} onChange={e => setN({ ...n, aceita: e.target.checked })} /> Aceita receber comunicações (LGPD)</label>
        {cpfDono && (
          <p className="alerta-box">CPF já cadastrado para <b>{cpfDono.nome}</b>.{' '}
            <button type="button" onClick={() => { onCliente(cpfDono); setNovo(false) }}>Usar este cliente</button></p>
        )}
        {erro && <p className="erro">{erro}</p>}
        <div className="acoes">
          <button type="button" onClick={cadastrar} disabled={salvando || !!cpfDono || !!erroCpf(n.cpf, false)}>{salvando ? 'Salvando…' : 'Cadastrar e usar'}</button>
          <button type="button" className="sec" onClick={() => setNovo(false)}>Voltar à busca</button>
        </div>
      </div>
    )
  }

  return (
    <div>
      <input value={busca} onChange={e => setBusca(e.target.value)} placeholder="Buscar cliente por nome, CPF ou celular…" autoFocus={autoFocus} />
      {sugestoes.length > 0 && (
        <span className="sugestoes">
          {sugestoes.map(s => (
            <button type="button" key={s.id} className="sec" onClick={() => onCliente(s)}>
              {s.nome}{s.celular ? ` · ${s.celular}` : ''}{s.cpf ? ` · CPF ***${s.cpf.slice(-4)}` : ''} · {s.categoria}
            </button>
          ))}
        </span>
      )}
      {busca.trim().length >= 3 && sugestoes.length === 0 && <small className="obs">Nenhum cliente encontrado.</small>}
      <div className="acoes" style={{ marginTop: '0.4rem' }}>
        <button type="button" className="sec" onClick={abrirNovo}>+ Cadastrar novo cliente</button>
      </div>
    </div>
  )
}
