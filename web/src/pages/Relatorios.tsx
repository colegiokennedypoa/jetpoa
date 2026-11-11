import { useEffect, useState } from 'react'
import { api, brl, type LinhaRel, type RelatorioVendas } from '../api'

const iso = (d: Date) => d.toISOString().slice(0, 10)
const pct = (v: number, t: number) => (t > 0 ? `${((v / t) * 100).toFixed(1)}%` : '—')

function csv(nome: string, linhas: LinhaRel[]) {
  const cab = 'item;grupo;ocorrencias;quantidade;valor;custo'
  const corpo = linhas.map(l => [l.chave, l.grupo ?? '', l.qtd, l.quantidade, l.valor, l.custo ?? ''].map(v => String(v).replace('.', ',')).join(';'))
  const blob = new Blob(['﻿' + [cab, ...corpo].join('\n')], { type: 'text/csv;charset=utf-8' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob); a.download = `${nome}.csv`; a.click()
  URL.revokeObjectURL(a.href)
}

export default function Relatorios() {
  const [de, setDe] = useState(iso(new Date()))
  const [ate, setAte] = useState(iso(new Date()))
  const [r, setR] = useState<RelatorioVendas>()
  const [erro, setErro] = useState<string>()

  const gerar = () => api.relatorioVendas(de, ate).then(x => { setR(x); setErro(undefined) }).catch(e => setErro(e.message))
  useEffect(() => { gerar() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const atalho = (dias: number) => {
    const fim = new Date(); const ini = new Date(); ini.setDate(fim.getDate() - dias + 1)
    setDe(iso(ini)); setAte(iso(fim))
  }

  const s = r?.resumo
  return (
    <section>
      <div className="filtros">
        <label>De <input type="date" value={de} onChange={e => setDe(e.target.value)} /></label>
        <label>Até <input type="date" value={ate} onChange={e => setAte(e.target.value)} /></label>
        <span className="segmentado mini">
          <button onClick={() => atalho(1)}>Hoje</button>
          <button onClick={() => atalho(7)}>7 dias</button>
          <button onClick={() => atalho(30)}>30 dias</button>
        </span>
        <button onClick={gerar}>Gerar</button>
      </div>
      {erro && <p className="erro">{erro}</p>}

      {s && (
        <>
          <div className="kpis">
            <Kpi titulo="Faturamento" valor={brl(s.faturamento)} sub={`${s.comandas} comandas`} />
            <Kpi titulo="Ticket médio" valor={brl(s.ticketMedio)} />
            <Kpi titulo="Descontos" valor={brl(s.descontos)} sub={pct(s.descontos, s.subtotal) + ' do bruto'} />
            <Kpi titulo="Taxa de serviço" valor={brl(s.taxaServico)} />
            <Kpi titulo="Custo (CMV)" valor={brl(s.custo)} sub={`margem ${pct(s.faturamento - s.custo, s.faturamento)}`} />
            <Kpi titulo="Itens cancelados" valor={String(s.itensCancelados)} sub={brl(s.valorCancelado)} />
            <Kpi titulo="NFC-e autorizadas" valor={String(s.nfceAutorizadas)} sub={`${brl(s.valorNfce)} · ${pct(s.valorNfce, s.faturamento)} do faturamento`} />
            <Kpi titulo="Consumo interno" valor={String(s.consumoInterno)} sub="comandas" />
          </div>

          <div className="rel-grade">
            <Tabela titulo="Por dia" linhas={r!.porDia} total={s.faturamento} nome="por-dia" fmtChave={k => new Date(k + 'T12:00').toLocaleDateString('pt-BR')} />
            <Tabela titulo="Por forma de pagamento" linhas={r!.porForma} total={s.faturamento} nome="por-forma" />
            <Tabela titulo="Por entrada" linhas={r!.porEntrada} total={s.faturamento} nome="por-entrada" />
            <Tabela titulo="Por horário de fechamento" linhas={r!.porHora} total={s.faturamento} nome="por-hora" />
            <Tabela titulo="Por categoria" linhas={r!.porCategoria} total={s.subtotal - s.descontos} nome="por-categoria" custo />
            <Tabela titulo="Por garçom" linhas={r!.porGarcom} total={s.subtotal - s.descontos} nome="por-garcom" quantidade />
            <Tabela titulo="Descontos por motivo" linhas={r!.descontosPorMotivo} total={s.descontos} nome="descontos-motivo" />
            <Tabela titulo="Descontos por operador (autorizador)" linhas={r!.descontosPorOperador} total={s.descontos} nome="descontos-operador" grupo />
          </div>
          <Tabela titulo="Produtos (top 100)" linhas={r!.porProduto} total={s.subtotal - s.descontos} nome="produtos" quantidade custo grupo />
        </>
      )}
    </section>
  )
}

function Kpi({ titulo, valor, sub }: { titulo: string; valor: string; sub?: string }) {
  return <div className="kpi"><small>{titulo}</small><b>{valor}</b>{sub && <small>{sub}</small>}</div>
}

function Tabela({ titulo, linhas, total, nome, quantidade, custo, grupo, fmtChave }: {
  titulo: string; linhas: LinhaRel[]; total: number; nome: string
  quantidade?: boolean; custo?: boolean; grupo?: boolean; fmtChave?: (k: string) => string
}) {
  return (
    <div className="rel-bloco">
      <div className="filtros"><h4 style={{ margin: 0 }}>{titulo}</h4>
        <button className="sec" disabled={linhas.length === 0} onClick={() => csv(nome, linhas)}>CSV</button></div>
      <table>
        <thead><tr><th>Item</th>{grupo && <th></th>}{quantidade && <th className="num">Qtd</th>}<th className="num">Valor</th><th className="num">%</th>{custo && <th className="num">Custo</th>}</tr></thead>
        <tbody>
          {linhas.map(l => (
            <tr key={l.chave + (l.grupo ?? '')}>
              <td>{fmtChave ? fmtChave(l.chave) : l.chave}</td>{grupo && <td className="obs">{l.grupo ?? ''}</td>}
              {quantidade && <td className="num">{l.quantidade}</td>}
              <td className="num">{brl(l.valor)}</td><td className="num">{pct(l.valor, total)}</td>
              {custo && <td className="num">{l.custo != null ? brl(l.custo) : ''}</td>}
            </tr>
          ))}
          {linhas.length === 0 && <tr><td colSpan={6} className="vazio">Sem dados.</td></tr>}
        </tbody>
      </table>
    </div>
  )
}
