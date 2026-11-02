import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, guardarBlob } from '../api/cliente'
import { P, type DocumentoResumen, type Expediente as TExpediente } from '../api/tipos'
import { useAuth } from '../auth/AuthContext'
import { Visor } from '../components/Visor'
import { Cargando, ChipExt, Vacio, mensajeError, useAvisos } from '../components/ui'
import { fmtFecha, fmtFechaSimple, fmtTamano, mimeDeExt } from '../utils'

/** Todos los documentos de una persona o caso, agrupados por tipo, con visor lateral. */
export function Expediente() {
  const { identificador = '' } = useParams()
  const { tiene } = useAuth()
  const avisar = useAvisos()
  const [exp, setExp] = useState<TExpediente>()
  const [error, setError] = useState<string>()
  const [sel, setSel] = useState<DocumentoResumen>()
  const [descargando, setDescargando] = useState(false)

  useEffect(() => {
    setExp(undefined); setError(undefined); setSel(undefined)
    api.get<TExpediente>(`/api/expedientes/${encodeURIComponent(identificador)}`)
      .then((e) => { setExp(e); setSel(e.documentos[0]) })
      .catch((e) => setError(e.status === 404 ? 'No hay documentos con este identificador en las áreas a las que tiene acceso.' : mensajeError(e)))
  }, [identificador])

  const grupos = useMemo(() => {
    const m = new Map<string, DocumentoResumen[]>()
    exp?.documentos.forEach((d) => {
      const k = d.tipoDocumento ?? 'Sin tipo'
      m.set(k, [...(m.get(k) ?? []), d])
    })
    return [...m.entries()]
  }, [exp])

  const descargarZip = async () => {
    setDescargando(true)
    try {
      const { blob, nombre } = await api.blob(`/api/expedientes/${encodeURIComponent(identificador)}/zip`)
      guardarBlob(blob, nombre ?? `Expediente_${identificador}.zip`)
    } catch (e) {
      avisar(mensajeError(e), 'error')
    } finally {
      setDescargando(false)
    }
  }

  if (error) return <><Migas id={identificador} /><Vacio titulo="Expediente no encontrado">{error}</Vacio></>
  if (!exp) return <Cargando />

  return (
    <>
      <Migas id={identificador} />
      <div className="cabecera-pagina">
        <div>
          <div className="mono exp-id grande">{exp.identificador}</div>
          <h1 className="titulo-pagina">{exp.nombreTitular ?? 'Titular sin nombre'}</h1>
          <p className="texto-suave">{exp.documentos.length} documento{exp.documentos.length === 1 ? '' : 's'} en {grupos.length} tipo{grupos.length === 1 ? '' : 's'}</p>
        </div>
        {tiene(P.DocumentosDescargar) && (
          <button className="btn btn-pri" onClick={descargarZip} disabled={descargando}>
            {descargando ? 'Preparando ZIP…' : 'Descargar expediente (ZIP)'}
          </button>
        )}
      </div>

      <div className="dividido">
        <div className="exp-lista">
          {grupos.map(([tipo, docs]) => (
            <section key={tipo} className="exp-grupo">
              <h3>{tipo} <span className="texto-suave">({docs.length})</span></h3>
              {docs.map((d) => (
                <button key={d.id} className={`exp-doc ${sel?.id === d.id ? 'sel' : ''}`} onClick={() => setSel(d)}>
                  <ChipExt ext={d.extension} />
                  <span className="exp-doc-txt">
                    <strong>{d.titulo}</strong>
                    <small>{d.fechaDocumento ? fmtFechaSimple(d.fechaDocumento) : fmtFecha(d.creadoEn)} · {d.area} · {fmtTamano(d.tamanoBytes)}</small>
                  </span>
                </button>
              ))}
            </section>
          ))}
        </div>
        <div className="exp-visor">
          {sel ? (
            <>
              <div className="exp-visor-cab">
                <strong>{sel.titulo}</strong>
                <Link to={`/documentos/${sel.id}`} className="btn btn-sec btn-sm">Ver ficha completa</Link>
              </div>
              <Visor documentoId={sel.id} tipoMime={mimeDeExt(sel.extension)} alto="70vh" />
            </>
          ) : <Vacio titulo="Seleccione un documento" />}
        </div>
      </div>
    </>
  )
}

const Migas = ({ id }: { id: string }) => (
  <nav className="migas"><Link to="/?modo=expedientes">Expedientes</Link><span>/</span><span className="mono">{id}</span></nav>
)
