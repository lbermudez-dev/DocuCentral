import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/cliente'
import type { DocumentoResumen, EstadoIngesta, Metadatos, Pagina } from '../api/tipos'
import { MetadatosForm, aMetadatosApi, metadatosVacios, useCatalogos, validarMetadatos } from '../components/MetadatosForm'
import { Visor } from '../components/Visor'
import { Cargando, ChipExt, ConfirmarConMotivo, Vacio, mensajeError, useAvisos } from '../components/ui'
import { fmtFechaHora, fmtTamano, mimeDeExt } from '../utils'

/** Documentos que llegaron por la carpeta de entrada y esperan que alguien capture sus datos. */
export function Bandeja() {
  const { tipos, areas } = useCatalogos()
  const avisar = useAvisos()
  const [lista, setLista] = useState<DocumentoResumen[]>()
  const [estado, setEstado] = useState<EstadoIngesta>()
  const [sel, setSel] = useState<DocumentoResumen>()
  const [m, setM] = useState<Metadatos>(metadatosVacios())
  const [mantener, setMantener] = useState(true)
  const [error, setError] = useState<string>()
  const [guardando, setGuardando] = useState(false)
  const [descartar, setDescartar] = useState(false)

  const cargar = useCallback(async () => {
    try {
      const [p, e] = await Promise.all([
        api.get<Pagina<DocumentoResumen>>('/api/bandeja?tamano=200'),
        api.get<EstadoIngesta>('/api/bandeja/estado'),
      ])
      setLista(p.items)
      setEstado(e)
      return p.items
    } catch (e) {
      avisar(mensajeError(e), 'error')
      return []
    }
  }, [avisar])

  useEffect(() => { cargar().then((l) => l[0] && seleccionar(l[0], false)) }, [cargar]) // eslint-disable-line react-hooks/exhaustive-deps

  const seleccionar = (d: DocumentoResumen, conservar = mantener) => {
    setSel(d)
    setError(undefined)
    setM((prev) => conservar
      ? { ...prev, titulo: d.titulo, descripcion: '', valores: {}, areaId: prev.areaId || d.areaId || 0 }
      : { ...metadatosVacios(), titulo: d.titulo, areaId: d.areaId ?? 0 })
  }

  const siguiente = (lista2: DocumentoResumen[], actualId: string) => {
    const resto = lista2.filter((x) => x.id !== actualId)
    if (resto[0]) seleccionar(resto[0])
    else setSel(undefined)
  }

  const indexar = async () => {
    if (!sel) return
    const err = validarMetadatos(m, tipos.find((t) => t.id === m.tipoDocumentoId))
    if (err) { setError(err); return }
    setGuardando(true)
    try {
      await api.post(`/api/bandeja/${sel.id}/indexar`, aMetadatosApi(m))
      avisar(`"${m.titulo}" publicado en el repositorio.`)
      const l = await cargar()
      siguiente(l, sel.id)
    } catch (e) {
      setError(mensajeError(e))
    } finally {
      setGuardando(false)
    }
  }

  const procesarAhora = async () => {
    await api.post('/api/bandeja/procesar-ahora')
    avisar('Revisando la carpeta de entrada…', 'info')
    setTimeout(cargar, 4000)
  }

  return (
    <>
      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">Bandeja de entrada</h1>
          <p className="texto-suave">Archivos importados automáticamente que necesitan sus datos para quedar disponibles en las búsquedas.</p>
        </div>
        <button className="btn btn-sec" onClick={procesarAhora}>Revisar carpeta ahora</button>
      </div>

      {estado && (
        <div className="estado-ingesta">
          <span className={`punto ${estado.activa ? (estado.ultimoError ? 'alerta' : 'ok') : 'apagado'}`} />
          <span>{estado.activa ? 'Carpeta vigilada activa' : 'Carpeta vigilada desactivada'}</span>
          <code className="mono">{estado.rutaEntrada}</code>
          {estado.ultimaEjecucion && <span className="texto-suave">Última revisión {fmtFechaHora(estado.ultimaEjecucion)} · {estado.importadosUltimaEjecucion} importados{estado.erroresUltimaEjecucion > 0 && `, ${estado.erroresUltimaEjecucion} con error`}</span>}
          {estado.ultimoError && <span className="texto-error">{estado.ultimoError}</span>}
        </div>
      )}

      {!lista ? <Cargando /> : lista.length === 0 ? (
        <Vacio titulo="La bandeja está vacía">
          Los archivos que se copien a la carpeta de entrada aparecerán aquí. Si los coloca en una subcarpeta con el código de un área (por ejemplo <code>RRHH</code>), llegan asignados a esa área.
        </Vacio>
      ) : (
        <div className="bandeja">
          <ul className="bandeja-lista">
            {lista.map((d) => (
              <li key={d.id}>
                <button className={sel?.id === d.id ? 'sel' : ''} onClick={() => seleccionar(d)}>
                  <ChipExt ext={d.extension} />
                  <span className="exp-doc-txt">
                    <strong>{d.titulo}</strong>
                    <small>{d.area ?? 'Sin área'} · {fmtTamano(d.tamanoBytes)} · {fmtFechaHora(d.creadoEn)}</small>
                  </span>
                </button>
              </li>
            ))}
          </ul>

          {sel ? (
            <>
              <div className="bandeja-visor"><Visor documentoId={sel.id} tipoMime={mimeDeExt(sel.extension)} alto="calc(100vh - 260px)" /></div>
              <div className="bandeja-form tarjeta">
                <MetadatosForm valor={m} alCambiar={setM} tipos={tipos} areas={areas} compacto />
                <label className="casilla">
                  <input type="checkbox" checked={mantener} onChange={(e) => setMantener(e.target.checked)} />
                  Mantener tipo, área y titular para el siguiente documento
                </label>
                {error && <div className="alerta alerta-error">{error}</div>}
                <div className="acciones fin">
                  <button className="btn btn-texto peligro" onClick={() => setDescartar(true)}>Descartar</button>
                  <button className="btn btn-pri" onClick={indexar} disabled={guardando}>{guardando ? 'Guardando…' : 'Indexar y publicar'}</button>
                </div>
              </div>
            </>
          ) : <Vacio titulo="Seleccione un documento de la lista" />}
        </div>
      )}

      <ConfirmarConMotivo abierto={descartar} titulo="Descartar documento" accion="Descartar"
        texto="Se quitará de la bandeja (por ejemplo, un escaneo erróneo). El archivo se conserva y queda registrado en la auditoría."
        alCerrar={() => setDescartar(false)}
        alConfirmar={async (motivo) => {
          if (!sel) return
          try {
            await api.del(`/api/bandeja/${sel.id}?motivo=${encodeURIComponent(motivo)}`)
            setDescartar(false)
            const l = await cargar()
            siguiente(l, sel.id)
          } catch (e) { avisar(mensajeError(e), 'error') }
        }} />
    </>
  )
}
