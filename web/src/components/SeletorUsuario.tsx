import { useEffect, useState } from 'react'
import { api, type Usuario } from '../api'

/** Seleção de usuário + PIN (identifica quem faz a operação). */
export default function SeletorUsuario({ usuarioId, pin, onUsuario, onPin, somenteGerentes = false }: {
  usuarioId: number | null; pin: string; onUsuario: (id: number | null) => void; onPin: (p: string) => void; somenteGerentes?: boolean
}) {
  const [lista, setLista] = useState<Usuario[]>([])
  useEffect(() => {
    api.usuarios().then(u => setLista(u.filter(x => x.ativo === 'S' && x.perfil !== 'Garçom' && (!somenteGerentes || x.autorizaDesconto === 'S')))).catch(() => {})
  }, [somenteGerentes])
  return (
    <>
      <label>Usuário
        <select value={usuarioId ?? ''} onChange={e => onUsuario(Number(e.target.value) || null)} required>
          <option value="">Selecione…</option>
          {lista.map(u => <option key={u.id} value={u.id}>{u.nome} ({u.perfil})</option>)}
        </select>
      </label>
      <label>PIN
        <input type="password" inputMode="numeric" autoComplete="off" value={pin} onChange={e => onPin(e.target.value.replace(/\D/g, ''))} required />
      </label>
    </>
  )
}
