import { useEffect, useState, type FormEvent } from 'react'
import { api, brl, type ComandaResumo, type Garcom as TGarcom } from '../api'
import Lancamento from '../components/Lancamento'

const CHAVE = 'jetpoa.garcom'

function lerGarcom(): number | null {
  try { const v = localStorage.getItem(CHAVE); return v ? Number(v) : null } catch { return null }
}

/** Tela do garçom (celular): escolhe o garçom uma vez, digita a comanda e lança. */
export default function Garcom() {
  const [garcons, setGarcons] = useState<TGarcom[]>([])
  const [garcomId, setGarcomId] = useState<number | null>(lerGarcom)
  const [numero, setNumero] = useState('')
  const [comanda, setComanda] = useState<ComandaResumo | null>(null)
  const [msg, setMsg] = useState<{ tipo: 'ok' | 'erro'; texto: string }>()

  useEffect(() => { api.garcons().then(g => setGarcons(g.filter(x => x.ativo === 'S'))).catch(() => {}) }, [])

  const escolherGarcom = (id: number | null) => {
    setGarcomId(id)
    try { if (id) localStorage.setItem(CHAVE, String(id)); else localStorage.removeItem(CHAVE) } catch { /* sem storage */ }
  }

  const buscar = async (e: FormEvent) => {
    e.preventDefault()
    setMsg(undefined)
    try { setComanda(await api.comandaPorNumero(Number(numero))) }
    catch (err) { setComanda(null); setMsg({ tipo: 'erro', texto: (err as Error).message }) }
  }

  const nome = garcons.find(g => g.id === garcomId)?.nome

  if (!garcomId) {
    return (
      <section className="garcom">
        <h3>Quem é você?</h3>
        <div className="garcom-lista">
          {garcons.map(g => <button key={g.id} className="sec" onClick={() => escolherGarcom(g.id)}>{g.nome}</button>)}
        </div>
      </section>
    )
  }

  return (
    <section className="garcom">
      <div className="filtros">
        <span>Garçom: <b>{nome ?? '…'}</b></span>
        <button className="sec" onClick={() => { escolherGarcom(null); setComanda(null) }}>trocar</button>
      </div>

      {!comanda ? (
        <form className="cartao" onSubmit={buscar}>
          <label>Número da comanda
            <input className="numero-grande" value={numero} onChange={e => setNumero(e.target.value.replace(/\D/g, ''))} inputMode="numeric" autoFocus required />
          </label>
          <div className="acoes"><button type="submit">Abrir pedido</button></div>
        </form>
      ) : (
        <>
          <div className="cabecalho-comanda">
            <span className="num">{comanda.numero}</span>
            <div><h3>{comanda.identificacao ?? '—'}</h3><small>{comanda.entradaTipo === 'R' ? 'Rio' : 'Terra'}{comanda.area ? ` · ${comanda.area}` : ''}</small></div>
            <span className="total">{brl(comanda.total)}</span>
            <button className="sec" onClick={() => { setComanda(null); setNumero('') }}>Outra comanda</button>
          </div>
          <Lancamento comandaId={comanda.id} origem="GARCOM" garcomFixo={garcomId}
            onLancado={m => { setMsg({ tipo: 'ok', texto: `Comanda ${comanda.numero}: ${m}` }); setComanda(null); setNumero('') }} />
        </>
      )}
      {msg && <p className={msg.tipo}>{msg.texto}</p>}
    </section>
  )
}
