import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { api, brl, hora, type ComandaSemNota, type FiscalStatus, type Nota, type Usuario } from '../api'

const SIT: Record<string, string> = {
  AUTORIZADA: 'tag ok', CANCELADA: 'tag', SUBSTITUIDA: 'tag', INUTILIZADA: 'tag',
  PENDENTE: 'tag pend', CONTINGENCIA: 'tag pend', VERIFICAR: 'tag pend', REJEITADA: 'tag erro',
}

type Msg = { tipo: 'ok' | 'erro'; texto: string }

export default function Fiscal() {
  const [status, setStatus] = useState<FiscalStatus>()
  const [sefaz, setSefaz] = useState<string>()
  const [notas, setNotas] = useState<Nota[]>([])
  const [semNota, setSemNota] = useState<ComandaSemNota[]>([])
  const [filtro, setFiltro] = useState('')
  const [msg, setMsg] = useState<Msg>()
  const [cancelando, setCancelando] = useState<Nota | null>(null)
  const [inutilizando, setInutilizando] = useState<{ inicial: number; final: number } | null>(null)

  const carregar = useCallback(() => {
    api.notas(filtro || undefined).then(setNotas).catch(e => setMsg({ tipo: 'erro', texto: e.message }))
    api.comandasSemNota().then(setSemNota).catch(() => {})
  }, [filtro])

  useEffect(() => { api.fiscalStatus().then(setStatus).catch(e => setMsg({ tipo: 'erro', texto: e.message })) }, [])
  useEffect(() => { carregar(); const t = setInterval(carregar, 15000); return () => clearInterval(t) }, [carregar])

  const acao = async (f: () => Promise<{ situacao: string; mensagem: string | null }>, ok: string) => {
    try {
      const r = await f()
      setMsg({ tipo: ['AUTORIZADA', 'CONTINGENCIA', 'CANCELADA', 'INUTILIZADA'].includes(r.situacao) ? 'ok' : 'erro', texto: `${ok}: ${r.situacao}${r.mensagem ? ' · ' + r.mensagem : ''}` })
    } catch (e) { setMsg({ tipo: 'erro', texto: (e as Error).message }) }
    carregar()
  }

  const verSefaz = async () => {
    setSefaz('consultando…')
    try { const s = await api.statusSefaz(); setSefaz(`${s.online ? 'Online' : 'Indisponível'} · ${s.cStat} ${s.xMotivo}`) }
    catch (e) { setSefaz((e as Error).message) }
  }

  const rejeitadas = notas.filter(n => n.situacao === 'REJEITADA')
  const diasCert = status?.certificado?.diasRestantes

  return (
    <section>
      <div className="setores">
        <div className="cartao setor">
          <b>NFC-e</b>
          {status && (status.configurada
            ? <span><span className="tag ok">Configurada</span> {status.ambienteNome} · série {status.serie}</span>
            : <span><span className="tag erro">Não configurada</span> {status.ambienteNome} · série {status.serie}</span>)}
          {status?.faltando?.map(f => <small key={f} className="erro">Falta: {f}</small>)}
          {status?.erroCertificado && <small className="erro">{status.erroCertificado}</small>}
          {status?.certificado && (
            <small className={diasCert! < 30 ? 'erro' : ''}>
              Certificado: {status.certificado.titular} · vence {new Date(status.certificado.validade).toLocaleDateString('pt-BR')} ({diasCert} dias)
            </small>
          )}
          <div className="acoes">
            <button className="sec" onClick={verSefaz} disabled={!status?.configurada}>Status da SEFAZ</button>
            <button className="sec" onClick={() => setInutilizando({ inicial: 0, final: 0 })} disabled={!status?.configurada}>Inutilizar números</button>
          </div>
          {sefaz && <small>{sefaz}</small>}
        </div>
        <div className="cartao setor">
          <b>Pendências</b>
          <span>{semNota.length} comanda(s) fechada(s) sem NFC-e</span>
          <span>{rejeitadas.length} nota(s) rejeitada(s)</span>
          <span>{notas.filter(n => n.situacao === 'CONTINGENCIA' || n.situacao === 'VERIFICAR').length} em contingência / verificação</span>
        </div>
      </div>
      {msg && <p className={msg.tipo}>{msg.texto}</p>}

      {cancelando && <CancelarNota nota={cancelando} onFim={m => { setCancelando(null); if (m) setMsg(m); carregar() }} />}
      {inutilizando && <Inutilizar serie={status?.serie ?? 2} onFim={m => { setInutilizando(null); if (m) setMsg(m); carregar() }} />}

      {semNota.length > 0 && (
        <>
          <h3>Comandas fechadas sem NFC-e (7 dias)</h3>
          <table>
            <thead><tr><th>Comanda</th><th>Cliente</th><th>Fechada</th><th className="num">Valor</th><th>Última tentativa</th><th></th></tr></thead>
            <tbody>
              {semNota.map(c => (
                <tr key={c.id}>
                  <td>{c.numero}</td><td>{c.identificacao ?? '—'}</td>
                  <td>{new Date(c.fechadaEm).toLocaleDateString('pt-BR')} {hora(c.fechadaEm)}</td>
                  <td className="num">{brl(c.valorTotal)}</td>
                  <td className="detalhe-erro">{c.ultimaSituacao ? `${c.ultimaSituacao} · ${c.ultimoMotivo ?? ''}` : 'nunca emitida'}</td>
                  <td><button className="sec" disabled={!status?.configurada} onClick={() => acao(() => api.emitirNota(c.id), `Comanda ${c.numero}`)}>Emitir NFC-e</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}

      <div className="filtros" style={{ marginTop: '1rem' }}>
        <h3 style={{ margin: 0 }}>Notas emitidas</h3>
        <select value={filtro} onChange={e => setFiltro(e.target.value)}>
          <option value="">Últimas 48 horas</option>
          {Object.keys(SIT).map(s => <option key={s} value={s}>{s}</option>)}
        </select>
        <span className="contador">{notas.length}</span>
      </div>
      <table>
        <thead><tr><th>Nº</th><th>Série</th><th>Comanda</th><th>Emissão</th><th className="num">Valor</th><th>Situação</th><th>Retorno SEFAZ</th><th></th></tr></thead>
        <tbody>
          {notas.map(n => (
            <tr key={n.id}>
              <td>{n.numero}</td><td>{n.serie}</td><td>{n.comandaNumero ?? '—'}</td>
              <td>{new Date(n.emitidaEm).toLocaleDateString('pt-BR')} {hora(n.emitidaEm)}</td>
              <td className="num">{brl(n.valorTotal)}</td>
              <td><span className={SIT[n.situacao] ?? 'tag'}>{n.situacao}</span>{n.tpEmis === '9' && <small> (offline)</small>}{n.ambiente === '2' && <small> homolog.</small>}</td>
              <td className="detalhe-erro">{n.protocolo ? `Prot. ${n.protocolo}` : ''} {n.xMotivo ?? ''}</td>
              <td className="acoes-linha">
                <a className="sec botao-link" href={`/api/fiscal/notas/${n.id}/danfe`} target="_blank" rel="noreferrer">DANFE</a>
                <a className="sec botao-link" href={`/api/fiscal/notas/${n.id}/xml`}>XML</a>
                {['AUTORIZADA', 'CONTINGENCIA'].includes(n.situacao) &&
                  <button className="sec" onClick={() => acao(async () => { await api.imprimirNota(n.id); return { situacao: 'IMPRESSO', mensagem: null } }, `NFC-e ${n.numero}`)}>Imprimir</button>}
                {n.situacao === 'REJEITADA' && <>
                  <button className="sec" onClick={() => acao(() => api.reenviarNota(n.id), `NFC-e ${n.numero}`)}>Reenviar</button>
                  <button className="sec" onClick={() => setInutilizando({ inicial: n.numero, final: n.numero })}>Inutilizar</button>
                </>}
                {n.situacao === 'AUTORIZADA' && <button className="sec" onClick={() => setCancelando(n)}>Cancelar</button>}
              </td>
            </tr>
          ))}
          {notas.length === 0 && <tr><td colSpan={8} className="vazio">Nenhuma nota.</td></tr>}
        </tbody>
      </table>
    </section>
  )
}

function Gerentes({ value, onChange }: { value: number | null; onChange: (v: number | null) => void }) {
  const [lista, setLista] = useState<Usuario[]>([])
  useEffect(() => { api.usuarios().then(u => setLista(u.filter(x => x.ativo === 'S' && x.autorizaDesconto === 'S'))).catch(() => {}) }, [])
  return (
    <select value={value ?? ''} onChange={e => onChange(Number(e.target.value) || null)} required>
      <option value="">Gerente…</option>
      {lista.map(u => <option key={u.id} value={u.id}>{u.nome}</option>)}
    </select>
  )
}

function CancelarNota({ nota, onFim }: { nota: Nota; onFim: (m?: Msg) => void }) {
  const [just, setJust] = useState('')
  const [usuarioId, setUsuarioId] = useState<number | null>(null)
  const [pin, setPin] = useState('')
  const [erro, setErro] = useState<string>()
  const enviar = async (e: FormEvent) => {
    e.preventDefault()
    try {
      const r = await api.cancelarNota(nota.id, just, usuarioId!, pin)
      if (r.situacao === 'CANCELADA') onFim({ tipo: 'ok', texto: `NFC-e ${nota.numero} cancelada.` })
      else setErro(r.mensagem ?? r.situacao)
    } catch (err) { setErro((err as Error).message) }
  }
  return (
    <form className="cartao" onSubmit={enviar}>
      <h3>Cancelar NFC-e {nota.numero}</h3>
      <p className="alerta-box">O cancelamento é enviado à SEFAZ e não pode ser desfeito. Há prazo legal após a autorização.</p>
      <div className="grade">
        <label className="largo">Justificativa (15 a 255 caracteres)*<input value={just} onChange={e => setJust(e.target.value)} minLength={15} maxLength={255} required /></label>
        <label>Autorizado por<Gerentes value={usuarioId} onChange={setUsuarioId} /></label>
        <label>PIN<input type="password" inputMode="numeric" autoComplete="off" value={pin} onChange={e => setPin(e.target.value.replace(/\D/g, ''))} required /></label>
      </div>
      {erro && <p className="erro">{erro}</p>}
      <div className="acoes"><button type="submit">Cancelar nota</button><button type="button" className="sec" onClick={() => onFim()}>Voltar</button></div>
    </form>
  )
}

function Inutilizar({ serie, onFim }: { serie: number; onFim: (m?: Msg) => void }) {
  const [ini, setIni] = useState('')
  const [fim, setFim] = useState('')
  const [just, setJust] = useState('')
  const [usuarioId, setUsuarioId] = useState<number | null>(null)
  const [pin, setPin] = useState('')
  const [erro, setErro] = useState<string>()
  const enviar = async (e: FormEvent) => {
    e.preventDefault()
    try {
      const r = await api.inutilizar({ serie, inicial: Number(ini), final: Number(fim || ini), justificativa: just, usuarioId: usuarioId!, pin })
      if (r.situacao === 'INUTILIZADA') onFim({ tipo: 'ok', texto: `Números ${ini}–${fim || ini} inutilizados.` })
      else setErro(r.mensagem ?? r.situacao)
    } catch (err) { setErro((err as Error).message) }
  }
  return (
    <form className="cartao" onSubmit={enviar}>
      <h3>Inutilizar numeração (série {serie})</h3>
      <div className="grade">
        <label>Número inicial*<input value={ini} onChange={e => setIni(e.target.value.replace(/\D/g, ''))} required /></label>
        <label>Número final<input value={fim} onChange={e => setFim(e.target.value.replace(/\D/g, ''))} placeholder="igual ao inicial" /></label>
        <label className="largo">Justificativa (15 a 255 caracteres)*<input value={just} onChange={e => setJust(e.target.value)} minLength={15} maxLength={255} required /></label>
        <label>Autorizado por<Gerentes value={usuarioId} onChange={setUsuarioId} /></label>
        <label>PIN<input type="password" inputMode="numeric" autoComplete="off" value={pin} onChange={e => setPin(e.target.value.replace(/\D/g, ''))} required /></label>
      </div>
      {erro && <p className="erro">{erro}</p>}
      <div className="acoes"><button type="submit">Inutilizar</button><button type="button" className="sec" onClick={() => onFim()}>Voltar</button></div>
    </form>
  )
}
