import { useCallback, useEffect, useState } from 'react'
import { api, hora, type Envio, type Setor } from '../api'

const TIPO = { P: 'Pedido', C: 'Cancelamento', R: 'Reimpressão' } as const
const SIT: Record<string, [string, string]> = { P: ['Na fila', 'tag pend'], I: ['Impresso', 'tag ok'], E: ['Erro', 'tag erro'] }

export default function Producao() {
  const [setores, setSetores] = useState<Setor[]>([])
  const [envios, setEnvios] = useState<Envio[]>([])
  const [soErros, setSoErros] = useState(false)
  const [msg, setMsg] = useState<{ tipo: 'ok' | 'erro'; texto: string }>()

  const carregarEnvios = useCallback(() => api.envios(soErros).then(setEnvios).catch(e => setMsg({ tipo: 'erro', texto: e.message })), [soErros])
  useEffect(() => { api.setores().then(setSetores).catch(() => {}) }, [])
  useEffect(() => {
    carregarEnvios()
    const t = setInterval(carregarEnvios, 5000)
    return () => clearInterval(t)
  }, [carregarEnvios])

  const acao = async (f: () => Promise<unknown>, ok: string) => {
    try { await f(); setMsg({ tipo: 'ok', texto: ok }); carregarEnvios() }
    catch (e) { setMsg({ tipo: 'erro', texto: (e as Error).message }) }
  }

  return (
    <section>
      <h3>Impressoras por setor</h3>
      <div className="setores">
        {setores.map(s => <SetorCard key={s.id} setor={s} onMsg={setMsg} />)}
      </div>
      {msg && <p className={msg.tipo}>{msg.texto}</p>}

      <div className="filtros">
        <h3 style={{ margin: 0 }}>Envios para produção</h3>
        <label><input type="checkbox" checked={soErros} onChange={e => setSoErros(e.target.checked)} /> Só pendentes/erros</label>
        <span className="contador">{soErros ? 'todos não impressos' : 'últimas 12 horas'}</span>
      </div>
      <table>
        <thead><tr><th>#</th><th>Hora</th><th>Comanda</th><th>Setor</th><th>Tipo</th><th className="num">Itens</th><th>Situação</th><th>Detalhe</th><th></th></tr></thead>
        <tbody>
          {envios.map(e => (
            <tr key={e.id}>
              <td>{e.id}</td><td>{hora(e.criadoEm)}</td><td>{e.numero}</td><td>{e.setor}</td><td>{TIPO[e.tipo]}</td>
              <td className="num">{e.qtdItens}</td>
              <td><span className={SIT[e.situacao][1]}>{SIT[e.situacao][0]}</span>{e.tentativas > 1 && <small> ({e.tentativas}x)</small>}</td>
              <td className="detalhe-erro">{e.ultimoErro ?? (e.impressoEm ? `impresso ${hora(e.impressoEm)}` : '')}</td>
              <td className="acoes-linha">
                {e.situacao === 'E' && <button className="sec" onClick={() => acao(() => api.tentarNovamente(e.id), 'Reenviado para a fila.')}>Tentar de novo</button>}
                {e.tipo !== 'C' && <button className="sec" onClick={() => acao(() => api.reimprimir(e.id), 'Reimpressão na fila.')}>Reimprimir</button>}
              </td>
            </tr>
          ))}
          {envios.length === 0 && <tr><td colSpan={9} className="vazio">Nada por aqui.</td></tr>}
        </tbody>
      </table>
    </section>
  )
}

function SetorCard({ setor, onMsg }: { setor: Setor; onMsg: (m: { tipo: 'ok' | 'erro'; texto: string }) => void }) {
  const [host, setHost] = useState(setor.impressoraHost ?? '')
  const [porta, setPorta] = useState(String(setor.impressoraPorta ?? 9100))
  const [imprime, setImprime] = useState(setor.imprime === 'S')
  const [ocupado, setOcupado] = useState(false)

  const salvar = async () => {
    setOcupado(true)
    try {
      await api.salvarSetor(setor.id, { impressoraHost: host || null, impressoraPorta: Number(porta) || 9100, imprime, colunasImpressao: setor.colunasImpressao })
      onMsg({ tipo: 'ok', texto: `${setor.nome}: configuração salva.` })
    } catch (e) { onMsg({ tipo: 'erro', texto: (e as Error).message }) }
    finally { setOcupado(false) }
  }
  const testar = async () => {
    setOcupado(true)
    try { onMsg({ tipo: 'ok', texto: (await api.testarSetor(setor.id)).mensagem }) }
    catch (e) { onMsg({ tipo: 'erro', texto: `${setor.nome}: ${(e as Error).message}` }) }
    finally { setOcupado(false) }
  }

  return (
    <div className="cartao setor">
      <b>{setor.nome}</b>
      <label className="check"><input type="checkbox" checked={imprime} onChange={e => setImprime(e.target.checked)} /> Imprime</label>
      <div className="ip">
        <input placeholder="IP" value={host} onChange={e => setHost(e.target.value)} disabled={!imprime} />
        <input placeholder="Porta" value={porta} onChange={e => setPorta(e.target.value.replace(/\D/g, ''))} disabled={!imprime} />
      </div>
      <div className="acoes">
        <button onClick={salvar} disabled={ocupado}>Salvar</button>
        <button className="sec" onClick={testar} disabled={ocupado || !imprime}>Imprimir teste</button>
      </div>
    </div>
  )
}
