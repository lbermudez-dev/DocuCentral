import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api/cliente'
import { NombreTipoDato, TipoDatoCampo, type TipoDocumento } from '../../api/tipos'
import { Cargando, Insignia, Modal, mensajeError, useAvisos } from '../../components/ui'

interface CampoEdit {
  id?: number
  clave: string
  etiqueta: string
  tipoDato: TipoDatoCampo
  obligatorio: boolean
  opciones?: string
  orden: number
  activo: boolean
}
interface TipoEdit { id?: number; nombre: string; descripcion?: string; activo: boolean; campos: CampoEdit[] }

/** Convierte una etiqueta en una clave: "Número de contrato" → numero_contrato */
const claveDe = (t: string) =>
  t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '')
    .replace(/^(\d)/, 'c_$1').slice(0, 60)

export function AdminTipos() {
  const avisar = useAvisos()
  const [tipos, setTipos] = useState<TipoDocumento[]>()
  const [edit, setEdit] = useState<TipoEdit>()

  const cargar = useCallback(() => api.get<TipoDocumento[]>('/api/tipos-documento').then(setTipos).catch((e) => avisar(mensajeError(e), 'error')), [avisar])
  useEffect(() => { cargar() }, [cargar])

  return (
    <>
      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">Tipos de documento</h1>
          <p className="texto-suave">Defina qué tipos de documento se guardan y qué datos se capturan de cada uno para poder buscarlos.</p>
        </div>
        <button className="btn btn-pri" onClick={() => setEdit({ nombre: '', activo: true, campos: [] })}>Nuevo tipo</button>
      </div>
      {!tipos ? <Cargando /> : (
        <div className="tabla-envoltura">
          <table className="tabla">
            <thead><tr><th>Tipo</th><th>Campos de búsqueda</th><th>Estado</th><th></th></tr></thead>
            <tbody>
              {tipos.map((t) => (
                <tr key={t.id}>
                  <td><strong>{t.nombre}</strong>{t.descripcion && <div className="peq texto-suave">{t.descripcion}</div>}</td>
                  <td>
                    <div className="etiquetas">
                      {t.campos.filter((c) => c.activo).map((c) => <span key={c.id}>{c.etiqueta}{c.obligatorio ? ' *' : ''}</span>)}
                      {t.campos.filter((c) => c.activo).length === 0 && <span className="texto-suave">Solo datos generales</span>}
                    </div>
                  </td>
                  <td>{t.activo ? <Insignia tono="ok">Activo</Insignia> : <Insignia>Inactivo</Insignia>}</td>
                  <td className="der">
                    <button className="btn btn-sec btn-sm" onClick={() => setEdit({ ...t, campos: t.campos.map((c) => ({ ...c, opciones: c.opciones ?? '' })) })}>Editar</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {edit && <EditorTipo inicial={edit} alCerrar={() => setEdit(undefined)} alGuardar={() => { setEdit(undefined); cargar(); avisar('Tipo de documento guardado.') }} />}
    </>
  )
}

function EditorTipo({ inicial, alCerrar, alGuardar }: { inicial: TipoEdit; alCerrar: () => void; alGuardar: () => void }) {
  const [t, setT] = useState<TipoEdit>(inicial)
  const [error, setError] = useState<string>()
  const [guardando, setGuardando] = useState(false)
  const setCampo = (i: number, c: Partial<CampoEdit>) => setT((x) => ({ ...x, campos: x.campos.map((y, j) => (j === i ? { ...y, ...c } : y)) }))
  const mover = (i: number, d: -1 | 1) => setT((x) => {
    const c = [...x.campos]
    const j = i + d
    if (j < 0 || j >= c.length) return x
    ;[c[i], c[j]] = [c[j], c[i]]
    return { ...x, campos: c }
  })

  const guardar = async () => {
    setError(undefined)
    setGuardando(true)
    const cuerpo = { ...t, campos: t.campos.map((c, i) => ({ ...c, orden: i + 1, opciones: c.tipoDato === TipoDatoCampo.Lista ? c.opciones : null })) }
    try {
      if (t.id) await api.put(`/api/tipos-documento/${t.id}`, cuerpo)
      else await api.post('/api/tipos-documento', cuerpo)
      alGuardar()
    } catch (e) { setError(mensajeError(e)) } finally { setGuardando(false) }
  }

  return (
    <Modal titulo={t.id ? `Editar: ${inicial.nombre}` : 'Nuevo tipo de documento'} abierto alCerrar={alCerrar} ancho={860} pie={
      <><button className="btn btn-sec" onClick={alCerrar}>Cancelar</button>
        <button className="btn btn-pri" onClick={guardar} disabled={guardando}>{guardando ? 'Guardando…' : 'Guardar'}</button></>
    }>
      <div className="form-grid">
        <label className="campo"><span>Nombre <i className="req">*</i></span>
          <input value={t.nombre} onChange={(e) => setT({ ...t, nombre: e.target.value })} maxLength={150} /></label>
        <label className="campo"><span>Estado</span>
          <select value={t.activo ? '1' : '0'} onChange={(e) => setT({ ...t, activo: e.target.value === '1' })}>
            <option value="1">Activo</option><option value="0">Inactivo (no se puede usar en cargas nuevas)</option>
          </select></label>
        <label className="campo col-2"><span>Descripción</span>
          <input value={t.descripcion ?? ''} onChange={(e) => setT({ ...t, descripcion: e.target.value })} maxLength={500} /></label>
      </div>

      <h3 className="subtitulo">Campos propios de este tipo</h3>
      <p className="peq texto-suave">Todos los documentos ya tienen título, identificador, titular, fecha, área y etiquetas. Agregue aquí solo los datos particulares de este tipo.</p>
      {t.campos.length > 0 && (
        <div className="tabla-envoltura">
          <table className="tabla tabla-edicion">
            <thead><tr><th>Etiqueta</th><th>Clave</th><th>Tipo de dato</th><th>Opciones</th><th>Oblig.</th><th>Activo</th><th></th></tr></thead>
            <tbody>
              {t.campos.map((c, i) => (
                <tr key={i} className={c.activo ? '' : 'inactivo'}>
                  <td><input value={c.etiqueta} onChange={(e) => setCampo(i, { etiqueta: e.target.value, ...(c.id ? {} : { clave: claveDe(e.target.value) }) })} placeholder="Ej. Número de contrato" /></td>
                  <td><input className="mono" value={c.clave} disabled={!!c.id} onChange={(e) => setCampo(i, { clave: e.target.value })} title={c.id ? 'La clave no cambia una vez creada' : ''} /></td>
                  <td>
                    <select value={c.tipoDato} onChange={(e) => setCampo(i, { tipoDato: Number(e.target.value) })}>
                      {Object.entries(NombreTipoDato).map(([v, n]) => <option key={v} value={v}>{n}</option>)}
                    </select>
                  </td>
                  <td>{c.tipoDato === TipoDatoCampo.Lista
                    ? <input value={c.opciones ?? ''} onChange={(e) => setCampo(i, { opciones: e.target.value })} placeholder="Opción 1|Opción 2" />
                    : <span className="texto-suave">—</span>}</td>
                  <td className="centro"><input type="checkbox" checked={c.obligatorio} onChange={(e) => setCampo(i, { obligatorio: e.target.checked })} /></td>
                  <td className="centro"><input type="checkbox" checked={c.activo} onChange={(e) => setCampo(i, { activo: e.target.checked })} /></td>
                  <td className="nowrap">
                    <button className="btn-icono" onClick={() => mover(i, -1)} aria-label="Subir">↑</button>
                    <button className="btn-icono" onClick={() => mover(i, 1)} aria-label="Bajar">↓</button>
                    <button className="btn-icono" onClick={() => setT({ ...t, campos: t.campos.filter((_, j) => j !== i) })} aria-label="Quitar" title="Si el campo ya tiene datos, se desactiva en lugar de borrarse">×</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <button className="btn btn-sec btn-sm" onClick={() => setT({ ...t, campos: [...t.campos, { clave: '', etiqueta: '', tipoDato: TipoDatoCampo.Texto, obligatorio: false, opciones: '', orden: t.campos.length + 1, activo: true }] })}>
        + Agregar campo
      </button>
      {error && <div className="alerta alerta-error">{error}</div>}
    </Modal>
  )
}
