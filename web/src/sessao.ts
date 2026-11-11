import { createContext, useContext } from 'react'
import type { Eu } from './api'

export type Sessao = { eu: Eu; pode: (perm: string) => boolean }

export const SessaoContext = createContext<Sessao | null>(null)

/** Usuário logado e suas permissões (só usado dentro do App, depois do login). */
export function useSessao(): Sessao {
  const s = useContext(SessaoContext)
  if (!s) throw new Error('useSessao fora do login')
  return s
}

export const criarSessao = (eu: Eu): Sessao => ({ eu, pode: p => eu.permissoes.includes(p) })
