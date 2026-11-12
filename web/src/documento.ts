/** Máscaras e validação de CPF/CNPJ (mesma regra de Domain/Documento.cs no servidor). */

export const soDigitos = (s: string | null | undefined) => (s ?? '').replace(/\D/g, '')

/** 000.000.000-00 enquanto digita (aceita colar com ou sem pontuação). */
export function mascaraCpf(v: string | null | undefined): string {
  const d = soDigitos(v).slice(0, 11)
  if (d.length <= 3) return d
  if (d.length <= 6) return `${d.slice(0, 3)}.${d.slice(3)}`
  if (d.length <= 9) return `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6)}`
  return `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6, 9)}-${d.slice(9)}`
}

/** 00.000.000/0000-00 */
export function mascaraCnpj(v: string | null | undefined): string {
  const d = soDigitos(v).slice(0, 14)
  if (d.length <= 2) return d
  if (d.length <= 5) return `${d.slice(0, 2)}.${d.slice(2)}`
  if (d.length <= 8) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5)}`
  if (d.length <= 12) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8)}`
  return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`
}

/** CPF até 11 dígitos, CNPJ acima disso. */
export const mascaraCpfCnpj = (v: string | null | undefined) =>
  soDigitos(v).length > 11 ? mascaraCnpj(v) : mascaraCpf(v)

export function cpfValido(v: string | null | undefined): boolean {
  const d = soDigitos(v)
  if (d.length !== 11 || /^(\d)\1{10}$/.test(d)) return false
  const dv = (len: number) => {
    let soma = 0
    for (let i = 0; i < len; i++) soma += Number(d[i]) * (len + 1 - i)
    const r = (soma * 10) % 11
    return r === 10 ? 0 : r
  }
  return dv(9) === Number(d[9]) && dv(10) === Number(d[10])
}

export function cnpjValido(v: string | null | undefined): boolean {
  const d = soDigitos(v)
  if (d.length !== 14 || /^(\d)\1{13}$/.test(d)) return false
  const dv = (len: number) => {
    const pesos = len === 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]
    const soma = pesos.reduce((s, p, i) => s + Number(d[i]) * p, 0)
    const r = soma % 11
    return r < 2 ? 0 : 11 - r
  }
  return dv(12) === Number(d[12]) && dv(13) === Number(d[13])
}

/**
 * Mensagem de erro do campo CPF (vazio = sem erro, o CPF é opcional).
 * Só acusa "incompleto" depois que o campo perde o foco (parametro `completo`).
 */
export function erroCpf(v: string | null | undefined, completo = true): string | undefined {
  const d = soDigitos(v)
  if (!d) return undefined
  if (d.length < 11) return completo ? 'CPF incompleto.' : undefined
  return cpfValido(d) ? undefined : 'CPF inválido.'
}

export function erroCpfCnpj(v: string | null | undefined): string | undefined {
  const d = soDigitos(v)
  if (!d) return undefined
  if (d.length === 11) return cpfValido(d) ? undefined : 'CPF inválido.'
  if (d.length === 14) return cnpjValido(d) ? undefined : 'CNPJ inválido.'
  return 'Informe CPF (11 dígitos) ou CNPJ (14 dígitos).'
}
