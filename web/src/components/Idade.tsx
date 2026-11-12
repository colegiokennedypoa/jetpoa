import type { Cliente, ComandaResumo, IdadeAbertura } from '../api'

/** Idade em anos completos hoje (data ISO, só a parte yyyy-mm-dd é usada). */
export function idadeAnos(iso: string | null | undefined): number | null {
  if (!iso) return null
  const [a, m, d] = iso.slice(0, 10).split('-').map(Number)
  const hoje = new Date()
  let idade = hoje.getFullYear() - a
  if (hoje.getMonth() + 1 < m || (hoje.getMonth() + 1 === m && hoje.getDate() < d)) idade--
  return idade
}

export const SEM_IDADE: IdadeAbertura = { dataNascimento: null, menorIdade: false }

/**
 * Na abertura da comanda (check-in / caixa): mostra a idade do cliente e, se o cadastro
 * não tem data de nascimento, deixa informar a data ou só marcar que é menor.
 */
export function IdadeCliente({ cliente, valor, onChange }: {
  cliente: Cliente; valor: IdadeAbertura; onChange: (v: IdadeAbertura) => void
}) {
  const cadastrada = idadeAnos(cliente.dataNascimento)
  if (cadastrada != null) {
    return cadastrada < 18
      ? <p className="aviso-menor">MENOR DE IDADE · {cadastrada} anos</p>
      : <small className="obs">Idade: {cadastrada} anos</small>
  }
  const informada = idadeAnos(valor.dataNascimento)
  return (
    <div className="idade-abertura">
      <label>Nascimento <small>(não cadastrado)</small>
        <input type="date" value={valor.dataNascimento ?? ''} max={new Date().toISOString().slice(0, 10)}
          onChange={e => onChange({ ...valor, dataNascimento: e.target.value || null })} />
      </label>
      {informada == null
        ? <label className="check"><input type="checkbox" checked={valor.menorIdade}
            onChange={e => onChange({ ...valor, menorIdade: e.target.checked })} /> É menor de 18 anos</label>
        : informada < 18
          ? <p className="aviso-menor">MENOR DE IDADE · {informada} anos</p>
          : <small className="obs">Idade: {informada} anos</small>}
    </div>
  )
}

/** Faixa de alerta na hora do pedido. */
export function AvisoMenor({ comanda }: { comanda: ComandaResumo }) {
  if (comanda.menorIdade !== 'S') return null
  const idade = idadeAnos(comanda.clienteNascimento)
  return (
    <p className="aviso-menor" role="alert">
      ⚠ MENOR DE IDADE{idade != null ? ` (${idade} anos)` : ''}: não servir bebida alcoólica
    </p>
  )
}
