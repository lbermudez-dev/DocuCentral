import { useRef, useState, type DragEvent } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/cliente'
import type { Metadatos } from '../api/tipos'
import { MetadatosForm, aMetadatosApi, metadatosVacios, useCatalogos, validarMetadatos } from '../components/MetadatosForm'
import { mensajeError, useAvisos } from '../components/ui'
import { fmtTamano } from '../utils'

interface Item {
  archivo: File
  titulo: string
  estado: 'pendiente' | 'subiendo' | 'ok' | 'error'
  progreso?: number
  mensaje?: string
  id?: string
  duplicados?: { id: string; titulo: string }[]
}

const sinExtension = (n: string) => n.replace(/\.[^.]+$/, '')

/** Carga web: uno o varios archivos que comparten los mismos datos (tipo, área, titular...).
 *  Cada archivo se guarda como un documento independiente con su propio título. */
export function Cargar() {
  const { tipos, areas } = useCatalogos()
  const avisar = useAvisos()
  const [m, setM] = useState<Metadatos>(metadatosVacios())
  const [items, setItems] = useState<Item[]>([])
  const [arrastrando, setArrastrando] = useState(false)
  const [error, setError] = useState<string>()
  const [subiendo, setSubiendo] = useState(false)
  const input = useRef<HTMLInputElement>(null)

  const agregar = (lista: FileList | null) => {
    if (!lista) return
    const nuevos = Array.from(lista).map<Item>((f) => ({ archivo: f, titulo: sinExtension(f.name), estado: 'pendiente' }))
    setItems((it) => [...it.filter((x) => x.estado !== 'ok'), ...nuevos])
    // Con un solo archivo, el título del formulario toma el nombre del archivo si estaba vacío
    if (nuevos.length === 1 && !m.titulo) setM((x) => ({ ...x, titulo: nuevos[0].titulo }))
  }
  const soltar = (e: DragEvent) => { e.preventDefault(); setArrastrando(false); agregar(e.dataTransfer.files) }
  const actualizar = (i: number, cambios: Partial<Item>) => setItems((it) => it.map((x, j) => (j === i ? { ...x, ...cambios } : x)))

  const pendientes = items.filter((x) => x.estado === 'pendiente' || x.estado === 'error')
  const varios = items.length > 1

  const subir = async () => {
    setError(undefined)
    const err = validarMetadatos(varios ? { ...m, titulo: m.titulo || 'x' } : m, tipos.find((t) => t.id === m.tipoDocumentoId))
    if (err) { setError(err); return }
    if (pendientes.length === 0) { setError('Agregue al menos un archivo.'); return }
    setSubiendo(true)
    let ok = 0
    for (let i = 0; i < items.length; i++) {
      const it = items[i]
      if (it.estado === 'ok') continue
      const datos = aMetadatosApi({ ...m, titulo: varios ? it.titulo : m.titulo })
      const fd = new FormData()
      fd.append('archivo', it.archivo)
      fd.append('metadatos', JSON.stringify(datos))
      actualizar(i, { estado: 'subiendo', progreso: 0, mensaje: undefined })
      try {
        const r = await api.subir<{ id: string; duplicados: { id: string; titulo: string }[] }>('/api/documentos', fd, (p) => actualizar(i, { progreso: p }))
        actualizar(i, { estado: 'ok', id: r.id, duplicados: r.duplicados })
        ok++
      } catch (e) {
        actualizar(i, { estado: 'error', mensaje: mensajeError(e) })
      }
    }
    setSubiendo(false)
    if (ok > 0) avisar(`${ok} documento${ok === 1 ? '' : 's'} cargado${ok === 1 ? '' : 's'}.`)
  }

  const nuevaCarga = () => { setItems([]); setM((x) => ({ ...metadatosVacios(), tipoDocumentoId: x.tipoDocumentoId, areaId: x.areaId })) }
  const terminado = items.length > 0 && items.every((x) => x.estado === 'ok')

  return (
    <>
      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">Cargar documentos</h1>
          <p className="texto-suave">Los archivos se guardan en el repositorio central con los datos que indique aquí.</p>
        </div>
      </div>

      <div className="dividido cargar">
        <section className="tarjeta">
          <h2 className="subtitulo">1. Archivos</h2>
          <div className={`zona-carga ${arrastrando ? 'activa' : ''}`}
            onDragOver={(e) => { e.preventDefault(); setArrastrando(true) }}
            onDragLeave={() => setArrastrando(false)} onDrop={soltar}
            onClick={() => input.current?.click()} role="button" tabIndex={0}
            onKeyDown={(e) => e.key === 'Enter' && input.current?.click()}>
            <strong>Arrastre los archivos aquí</strong>
            <span>o haga clic para seleccionarlos</span>
            <input ref={input} type="file" multiple hidden onChange={(e) => { agregar(e.target.files); e.target.value = '' }} />
          </div>

          {items.length > 0 && (
            <ul className="lista-archivos">
              {items.map((it, i) => (
                <li key={i} className={`archivo-${it.estado}`}>
                  <div className="archivo-fila">
                    {varios ? (
                      <input className="archivo-titulo" value={it.titulo} disabled={it.estado === 'ok' || subiendo}
                        onChange={(e) => actualizar(i, { titulo: e.target.value })} aria-label="Título del documento" />
                    ) : <strong className="archivo-titulo">{it.archivo.name}</strong>}
                    <span className="peq texto-suave nowrap">{fmtTamano(it.archivo.size)}</span>
                    {it.estado === 'ok' && it.id && <Link className="btn btn-sec btn-sm" to={`/documentos/${it.id}`}>Abrir</Link>}
                    {(it.estado === 'pendiente' || it.estado === 'error') && !subiendo &&
                      <button className="btn-icono" onClick={() => setItems((x) => x.filter((_, j) => j !== i))} aria-label="Quitar">×</button>}
                  </div>
                  {varios && <div className="peq texto-suave">{it.archivo.name}</div>}
                  {it.estado === 'subiendo' && <div className="progreso"><div style={{ width: `${it.progreso ?? 0}%` }} /></div>}
                  {it.estado === 'ok' && <div className="peq texto-ok">Guardado</div>}
                  {it.estado === 'error' && <div className="peq texto-error">{it.mensaje}</div>}
                  {it.duplicados && it.duplicados.length > 0 && (
                    <div className="peq texto-alerta">
                      Este mismo archivo ya existía en: {it.duplicados.map((d, k) => <span key={d.id}>{k > 0 && ', '}<Link to={`/documentos/${d.id}`}>{d.titulo}</Link></span>)}
                    </div>
                  )}
                </li>
              ))}
            </ul>
          )}
          {varios && <p className="peq texto-suave">Con varios archivos, cada uno se guarda como un documento distinto con el título indicado en la lista y los datos del formulario.</p>}
        </section>

        <section className="tarjeta">
          <h2 className="subtitulo">2. Datos del documento</h2>
          <MetadatosForm valor={m} alCambiar={setM} tipos={tipos} areas={areas} ocultarTitulo={varios} />
          {error && <div className="alerta alerta-error">{error}</div>}
          <div className="acciones fin">
            {terminado
              ? <button className="btn btn-pri" onClick={nuevaCarga}>Cargar otros documentos</button>
              : <button className="btn btn-pri" onClick={subir} disabled={subiendo || pendientes.length === 0}>
                  {subiendo ? 'Guardando…' : `Guardar ${pendientes.length > 1 ? `${pendientes.length} documentos` : 'documento'}`}
                </button>}
          </div>
        </section>
      </div>
    </>
  )
}
