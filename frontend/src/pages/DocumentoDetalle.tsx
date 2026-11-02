import { Fragment, useCallback, useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api, guardarBlob } from '../api/cliente'
import { P, OrigenDocumento, type DocumentoDetalle as TDoc, type Metadatos, type RegistroAuditoria } from '../api/tipos'
import { useAuth } from '../auth/AuthContext'
import { MetadatosForm, aMetadatosApi, mostrarValor, useCatalogos, validarMetadatos } from '../components/MetadatosForm'
import { Visor } from '../components/Visor'
import { Cargando, ChipExt, ConfirmarConMotivo, Insignia, Modal, Vacio, mensajeError, useAvisos } from '../components/ui'
import { fmtFechaHora, fmtFechaSimple, fmtTamano } from '../utils'

export function DocumentoDetalle() {
  const { id = '' } = useParams()
  const { tiene } = useAuth()
  const avisar = useAvisos()
  const nav = useNavigate()
  const [doc, setDoc] = useState<TDoc>()
  const [error, setError] = useState<string>()
  const [versionVista, setVersionVista] = useState<number>()
  const [pestana, setPestana] = useState<'datos' | 'versiones' | 'historial'>('datos')
  const [editar, setEditar] = useState(false)
  const [nuevaVersion, setNuevaVersion] = useState(false)
  const [eliminar, setEliminar] = useState(false)

  const cargar = useCallback(() => {
    api.get<TDoc>(`/api/documentos/${id}`)
      .then((d) => { setDoc(d); setVersionVista(d.versionActual) })
      .catch((e) => setError(e.status === 404 ? 'El documento no existe o no tiene acceso a él.' : mensajeError(e)))
  }, [id])
  useEffect(cargar, [cargar])

  const descargar = async (version?: number) => {
    try {
      const { blob, nombre } = await api.blob(`/api/documentos/${id}/archivo?inline=false${version ? `&version=${version}` : ''}`)
      guardarBlob(blob, nombre ?? doc?.titulo ?? 'documento')
    } catch (e) { avisar(mensajeError(e), 'error') }
  }

  if (error) return <Vacio titulo="No disponible">{error}</Vacio>
  if (!doc) return <Cargando />
  const vActual = doc.versiones.find((v) => v.numeroVersion === versionVista) ?? doc.versiones[0]

  return (
    <>
      <nav className="migas">
        <Link to="/">Buscar</Link><span>/</span>
        {doc.identificador && <><Link to={`/expediente/${encodeURIComponent(doc.identificador)}`} className="mono">{doc.identificador}</Link><span>/</span></>}
        <span>{doc.titulo}</span>
      </nav>

      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">{doc.titulo}</h1>
          <div className="fila-insignias">
            <Insignia tono="acento">{doc.tipoDocumento}</Insignia>
            <Insignia>{doc.area}</Insignia>
            {doc.origen === OrigenDocumento.CarpetaVigilada && <Insignia>Carpeta de entrada</Insignia>}
            <Insignia>Versión {doc.versionActual}</Insignia>
          </div>
        </div>
        <div className="acciones">
          {tiene(P.DocumentosDescargar) && <button className="btn btn-pri" onClick={() => descargar(versionVista)}>Descargar</button>}
          {tiene(P.DocumentosEditar) && <button className="btn btn-sec" onClick={() => setEditar(true)}>Editar datos</button>}
          {tiene(P.DocumentosEditar) && <button className="btn btn-sec" onClick={() => setNuevaVersion(true)}>Nueva versión</button>}
          {tiene(P.DocumentosEliminar) && <button className="btn btn-texto peligro" onClick={() => setEliminar(true)}>Eliminar</button>}
        </div>
      </div>

      <div className="dividido doc">
        <div>
          {versionVista !== doc.versionActual && (
            <div className="alerta alerta-info">
              Está viendo la versión {versionVista}.{' '}
              <button className="btn-enlace" onClick={() => setVersionVista(doc.versionActual)}>Volver a la vigente</button>
            </div>
          )}
          {vActual && <Visor documentoId={doc.id} version={versionVista} tipoMime={vActual.tipoMime} />}
        </div>

        <aside className="panel-lateral">
          <div className="pestanas">
            <button className={pestana === 'datos' ? 'activo' : ''} onClick={() => setPestana('datos')}>Datos</button>
            <button className={pestana === 'versiones' ? 'activo' : ''} onClick={() => setPestana('versiones')}>Versiones ({doc.versiones.length})</button>
            <button className={pestana === 'historial' ? 'activo' : ''} onClick={() => setPestana('historial')}>Historial</button>
          </div>

          {pestana === 'datos' && (
            <dl className="datos">
              <dt>Identificador</dt>
              <dd>{doc.identificador ? <Link className="mono enlace" to={`/expediente/${encodeURIComponent(doc.identificador)}`}>{doc.identificador}</Link> : '—'}</dd>
              <dt>Titular</dt><dd>{doc.nombreTitular ?? '—'}</dd>
              <dt>Fecha del documento</dt><dd>{fmtFechaSimple(doc.fechaDocumento)}</dd>
              {doc.valores.map((v) => (<Fragment key={v.campoId}><dt>{v.etiqueta}</dt><dd>{mostrarValor(v.tipoDato, v.valor)}</dd></Fragment>))}
              <dt>Etiquetas</dt>
              <dd>{doc.etiquetas ? <div className="etiquetas">{doc.etiquetas.split(',').map((e) => e.trim()).filter(Boolean).map((e) => <span key={e}>{e}</span>)}</div> : '—'}</dd>
              <dt>Descripción</dt><dd className="pre">{doc.descripcion ?? '—'}</dd>
              <dt>Cargado por</dt><dd>{doc.creadoPor ?? 'Sistema'} · {fmtFechaHora(doc.creadoEn)}</dd>
              {doc.modificadoEn && <><dt>Última modificación</dt><dd>{fmtFechaHora(doc.modificadoEn)}</dd></>}
            </dl>
          )}

          {pestana === 'versiones' && (
            <ul className="versiones">
              {doc.versiones.map((v) => (
                <li key={v.id} className={v.numeroVersion === versionVista ? 'sel' : ''}>
                  <div className="ver-cab">
                    <ChipExt ext={v.extension} />
                    <strong>v{v.numeroVersion}</strong>
                    {v.numeroVersion === doc.versionActual && <Insignia tono="ok">Vigente</Insignia>}
                  </div>
                  <div className="peq">{v.nombreOriginal} · {fmtTamano(v.tamanoBytes)}</div>
                  <div className="peq texto-suave">{v.creadoPor ?? 'Sistema'} · {fmtFechaHora(v.creadoEn)}</div>
                  {v.comentario && <div className="peq">“{v.comentario}”</div>}
                  <div className="peq mono texto-suave" title="Huella SHA-256 del archivo">SHA-256 {v.hashSha256.slice(0, 16)}…</div>
                  <div className="ver-acciones">
                    <button className="btn btn-sec btn-sm" onClick={() => setVersionVista(v.numeroVersion)}>Ver</button>
                    {tiene(P.DocumentosDescargar) && <button className="btn btn-sec btn-sm" onClick={() => descargar(v.numeroVersion)}>Descargar</button>}
                  </div>
                </li>
              ))}
            </ul>
          )}

          {pestana === 'historial' && <Historial id={doc.id} />}
        </aside>
      </div>

      {editar && <EditarModal doc={doc} alCerrar={() => setEditar(false)} alGuardar={() => { setEditar(false); cargar(); avisar('Datos actualizados.') }} />}
      {nuevaVersion && <NuevaVersionModal id={doc.id} alCerrar={() => setNuevaVersion(false)} alGuardar={() => { setNuevaVersion(false); cargar(); avisar('Nueva versión guardada.') }} />}
      <ConfirmarConMotivo abierto={eliminar} titulo="Eliminar documento" accion="Eliminar"
        texto="El documento dejará de aparecer en las búsquedas. El archivo y su historial se conservan en el repositorio."
        alCerrar={() => setEliminar(false)}
        alConfirmar={async (motivo) => {
          try {
            await api.del(`/api/documentos/${doc.id}?motivo=${encodeURIComponent(motivo)}`)
            avisar('Documento eliminado.')
            nav(doc.identificador ? `/expediente/${encodeURIComponent(doc.identificador)}` : '/')
          } catch (e) { avisar(mensajeError(e), 'error') }
        }} />
    </>
  )
}

function Historial({ id }: { id: string }) {
  const [items, setItems] = useState<RegistroAuditoria[]>()
  useEffect(() => { api.get<RegistroAuditoria[]>(`/api/documentos/${id}/historial`).then(setItems).catch(() => setItems([])) }, [id])
  if (!items) return <Cargando />
  if (items.length === 0) return <p className="texto-suave">Sin movimientos registrados.</p>
  return (
    <ul className="linea-tiempo">
      {items.map((a) => (
        <li key={a.id}>
          <strong>{ETIQUETAS_ACCION[a.accion] ?? a.accion}</strong>
          <span className="peq texto-suave">{a.nombreUsuario} · {fmtFechaHora(a.fecha)}</span>
          {a.detalle && <span className="peq">{a.detalle}</span>}
        </li>
      ))}
    </ul>
  )
}

export const ETIQUETAS_ACCION: Record<string, string> = {
  VER: 'Consultado', DESCARGAR: 'Descargado', CARGAR: 'Cargado', IMPORTAR: 'Importado de la carpeta de entrada',
  INDEXAR: 'Indexado', EDITAR: 'Datos editados', NUEVA_VERSION: 'Nueva versión', ELIMINAR: 'Eliminado',
  LOGIN: 'Inicio de sesión', LOGIN_FALLIDO: 'Inicio de sesión fallido', USUARIO_BLOQUEADO: 'Usuario bloqueado',
  CAMBIO_PASSWORD: 'Cambio de contraseña', DESCARGAR_EXPEDIENTE: 'Expediente descargado', CREAR: 'Creado', MODIFICAR: 'Modificado',
}

function EditarModal({ doc, alCerrar, alGuardar }: { doc: TDoc; alCerrar: () => void; alGuardar: () => void }) {
  const { tipos, areas } = useCatalogos()
  const [m, setM] = useState<Metadatos>({
    tipoDocumentoId: doc.tipoDocumentoId ?? 0, areaId: doc.areaId ?? 0, titulo: doc.titulo,
    descripcion: doc.descripcion ?? '', identificador: doc.identificador ?? '', nombreTitular: doc.nombreTitular ?? '',
    etiquetas: doc.etiquetas ?? '', fechaDocumento: doc.fechaDocumento?.slice(0, 10) ?? '',
    valores: Object.fromEntries(doc.valores.map((v) => [v.clave, v.valor ?? ''])),
  })
  const [error, setError] = useState<string>()
  const [enviando, setEnviando] = useState(false)
  const guardar = async () => {
    const err = validarMetadatos(m, tipos.find((t) => t.id === m.tipoDocumentoId))
    if (err) { setError(err); return }
    setEnviando(true)
    try { await api.put(`/api/documentos/${doc.id}`, aMetadatosApi(m)); alGuardar() }
    catch (e) { setError(mensajeError(e)) }
    finally { setEnviando(false) }
  }
  return (
    <Modal titulo="Editar datos del documento" abierto alCerrar={alCerrar} ancho={760} pie={
      <><button className="btn btn-sec" onClick={alCerrar}>Cancelar</button>
        <button className="btn btn-pri" onClick={guardar} disabled={enviando}>{enviando ? 'Guardando…' : 'Guardar'}</button></>
    }>
      <MetadatosForm valor={m} alCambiar={setM} tipos={tipos} areas={areas} />
      {error && <div className="alerta alerta-error">{error}</div>}
    </Modal>
  )
}

function NuevaVersionModal({ id, alCerrar, alGuardar }: { id: string; alCerrar: () => void; alGuardar: () => void }) {
  const [archivo, setArchivo] = useState<File>()
  const [comentario, setComentario] = useState('')
  const [progreso, setProgreso] = useState<number>()
  const [error, setError] = useState<string>()
  const subir = async () => {
    if (!archivo) return
    const fd = new FormData()
    fd.append('archivo', archivo)
    fd.append('comentario', comentario)
    setError(undefined)
    try { await api.subir(`/api/documentos/${id}/versiones`, fd, setProgreso); alGuardar() }
    catch (e) { setError(mensajeError(e)); setProgreso(undefined) }
  }
  return (
    <Modal titulo="Subir nueva versión" abierto alCerrar={alCerrar} ancho={500} pie={
      <><button className="btn btn-sec" onClick={alCerrar}>Cancelar</button>
        <button className="btn btn-pri" onClick={subir} disabled={!archivo || progreso !== undefined}>
          {progreso !== undefined ? `Subiendo ${progreso}%` : 'Subir versión'}</button></>
    }>
      <p className="texto-suave">La versión actual se conserva en el historial y se podrá consultar en cualquier momento.</p>
      <label className="campo"><span>Archivo</span><input type="file" onChange={(e) => setArchivo(e.target.files?.[0])} /></label>
      <label className="campo"><span>Comentario</span>
        <input value={comentario} onChange={(e) => setComentario(e.target.value)} placeholder="Ej. Copia firmada, corrección de página 3…" maxLength={500} /></label>
      {progreso !== undefined && <div className="progreso"><div style={{ width: `${progreso}%` }} /></div>}
      {error && <div className="alerta alerta-error">{error}</div>}
    </Modal>
  )
}
