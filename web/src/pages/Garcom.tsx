import { useState, type FormEvent } from 'react'
import { api, brl, type ComandaResumo } from '../api'
import { useSessao } from '../sessao'
import Lancamento from '../components/Lancamento'

/**
 * Tela do garçom (celular): cada garçom entra com o próprio login, então o garçom é sempre
 * quem está logado (não dá para trocar). Digita a comanda e lança.
 */
export default function Garcom() {
  const { eu } = useSessao()
  const [numero, setNumero] = useState('')
  const [comanda, setComanda] = useState<ComandaResumo | null>(null)
  const [msg, setMsg] = useState<{ tipo: 'ok' | 'erro'; texto: string }>()

  const buscar = async (e: FormEvent) => {
    e.preventDefault()
    setMsg(undefined)
    try { setComanda(await api.comandaPorNumero(Number(numero))) }
    catch (err) { setComanda(null); setMsg({ tipo: 'erro', texto: (err as Error).message }) }
  }

  if (!eu.garcomId) {
    return (
      <section className="garcom">
        <p className="erro">Seu login não está vinculado a um garçom. Peça ao gerente para vincular na tela Usuários.</p>
      </section>
    )
  }

  return (
    <section className="garcom">
      <div className="filtros">
        <span>Garçom: <b>{eu.garcomNome}</b></span>
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
          <Lancamento comandaId={comanda.id} origem="GARCOM" garcomFixo={eu.garcomId}
            onLancado={m => { setMsg({ tipo: 'ok', texto: `Comanda ${comanda.numero}: ${m}` }); setComanda(null); setNumero('') }} />
        </>
      )}
      {msg && <p className={msg.tipo}>{msg.texto}</p>}
    </section>
  )
}
