import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api/cliente'
import type { Busqueda, DocumentoResumen, ExpedienteResumen, Pagina } from '../api/tipos'
import { useCatalogos } from '../components/MetadatosForm'
import { Cargando, ChipExt, Paginador, Vacio, mensajeError, useAvisos } from '../components/ui'
import { fmtFecha, fmtFechaSimple, fmtTamano } from '../utils'

const TAM = 25

export function Buscar() {
  const [params, setParams] = useSearchParams()
  const modo = params.get('modo') === 'expedientes' ? 'expedientes' : 'documentos'

  return (
    <>
      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">Buscar</h1>
          <p className="texto-suave">Encuentre documentos sueltos o el expediente completo de una persona.</p>
        </div>
        <div className="segmentado" role="tablist">
          <button role="tab" aria-selected={modo === 'documentos'} className={modo === 'documentos' ? 'activo' : ''}
            onClick={() => setParams({})}>Documentos</button>
          <button role="tab" aria-selected={modo === 'expedientes'} className={modo === 'expedientes' ? 'activo' : ''}
            onClick={() => setParams({ modo: 'expedientes' })}>Expedientes</button>
        </div>
      </div>
      {modo === 'documentos' ? <BuscarDocumentos /> : <BuscarExpedientes />}
    </>
  )
}

function BuscarDocumentos() {
  const { tipos, areas } = useCatalogos()
  const avisar = useAvisos()
  const [filtro, setFiltro] = useState<Busqueda>({ orden: 'recientes' })
  const [aplicado, setAplicado] = useState<Busqueda>({ orden: 'recientes', pagina: 1 })
  const [verFiltros, setVerFiltros] = useState(false)
  const [res, setRes] = useState<Pagina<DocumentoResumen>>()
  const [cargando, setCargando] = useState(false)

  const ejecutar = useCallback(async (b: Busqueda) => {
    setCargando(true)
    try {
      const limpio: Busqueda = { ...b, tamanoPagina: TAM }
      if (limpio.campos) limpio.campos = Object.fromEntries(Object.entries(limpio.campos).filter(([, v]) => v?.trim()))
      setRes(await api.post<Pagina<DocumentoResumen>>('/api/documentos/buscar', limpio))
    } catch (e) {
      avisar(mensajeError(e), 'error')
    } finally {
      setCargando(false)
    }
  }, [avisar])

  useEffect(() => { ejecutar(aplicado) }, [aplicado, ejecutar])

  const buscar = (e?: FormEvent) => {
    e?.preventDefault()
    setAplicado({ ...filtro, pagina: 1 })
  }
  const limpiar = () => {
    const base = { orden: 'recientes' }
    setFiltro(base)
    setAplicado({ ...base, pagina: 1 })
  }
  const set = <K extends keyof Busqueda>(k: K, v: Busqueda[K]) => setFiltro((f) => ({ ...f, [k]: v }))
  const tipoSel = tipos.find((t) => t.id === filtro.tipoDocumentoId)
  const nFiltros = ['identificador', 'nombreTitular', 'tipoDocumentoId', 'areaId', 'fechaDesde', 'fechaHasta']
    .filter((k) => (aplicado as any)[k]).length + Object.values(aplicado.campos ?? {}).filter(Boolean).length

  return (
    <>
      <form className="buscador" onSubmit={buscar}>
        <div className="buscador-fila">
          <input className="buscador-input" placeholder="Buscar por título, cédula, nombre, etiquetas o cualquier dato…"
            value={filtro.texto ?? ''} onChange={(e) => set('texto', e.target.value)} autoFocus />
          <button type="button" className={`btn btn-sec ${verFiltros ? 'presionado' : ''}`} onClick={() => setVerFiltros(!verFiltros)}>
            Filtros{nFiltros > 0 && <span className="contador oscuro">{nFiltros}</span>}
          </button>
          <button className="btn btn-pri">Buscar</button>
        </div>

        {verFiltros && (
          <div className="filtros">
            <label className="campo"><span>Identificador</span>
              <input className="mono" value={filtro.identificador ?? ''} onChange={(e) => set('identificador', e.target.value)} /></label>
            <label className="campo"><span>Nombre del titular</span>
              <input value={filtro.nombreTitular ?? ''} onChange={(e) => set('nombreTitular', e.target.value)} /></label>
            <label className="campo"><span>Tipo de documento</span>
              <select value={filtro.tipoDocumentoId ?? ''} onChange={(e) => setFiltro((f) => ({ ...f, tipoDocumentoId: e.target.value ? Number(e.target.value) : undefined, campos: undefined }))}>
                <option value="">Todos</option>
                {tipos.map((t) => <option key={t.id} value={t.id}>{t.nombre}</option>)}
              </select></label>
            <label className="campo"><span>Área</span>
              <select value={filtro.areaId ?? ''} onChange={(e) => set('areaId', e.target.value ? Number(e.target.value) : undefined)}>
                <option value="">Todas</option>
                {areas.map((a) => <option key={a.id} value={a.id}>{a.nombre}</option>)}
              </select></label>
            <label className="campo"><span>Desde</span>
              <input type="date" value={filtro.fechaDesde ?? ''} onChange={(e) => set('fechaDesde', e.target.value || undefined)} /></label>
            <label className="campo"><span>Hasta</span>
              <input type="date" value={filtro.fechaHasta ?? ''} onChange={(e) => set('fechaHasta', e.target.value || undefined)} /></label>
            {tipoSel?.campos.filter((c) => c.activo).map((c) => (
              <label key={c.id} className="campo"><span>{c.etiqueta}</span>
                <input value={filtro.campos?.[c.id] ?? ''} onChange={(e) => set('campos', { ...(filtro.campos ?? {}), [c.id]: e.target.value })} /></label>
            ))}
            <label className="campo"><span>Ordenar por</span>
              <select value={filtro.orden} onChange={(e) => set('orden', e.target.value)}>
                <option value="recientes">Más recientes</option>
                <option value="antiguos">Más antiguos</option>
                <option value="titulo">Título</option>
                <option value="identificador">Identificador</option>
              </select></label>
            <div className="filtros-acciones">
              <button type="button" className="btn btn-texto" onClick={limpiar}>Limpiar filtros</button>
            </div>
          </div>
        )}
      </form>

      <div className="resultados-cabecera">
        {res && <span>{res.total.toLocaleString('es-NI')} documento{res.total === 1 ? '' : 's'}</span>}
        {cargando && <Cargando texto="Buscando…" />}
      </div>

      {res && res.items.length === 0 && !cargando && (
        <Vacio titulo="No se encontraron documentos">Pruebe con menos palabras o quite algunos filtros.</Vacio>
      )}

      {res && res.items.length > 0 && (
        <div className="tabla-envoltura">
          <table className="tabla">
            <thead>
              <tr><th></th><th>Documento</th><th>Titular</th><th>Tipo</th><th>Área</th><th>Fecha</th><th className="der">Tamaño</th></tr>
            </thead>
            <tbody>
              {res.items.map((d) => (
                <tr key={d.id}>
                  <td className="col-ext"><ChipExt ext={d.extension} /></td>
                  <td>
                    <Link to={`/documentos/${d.id}`} className="enlace-fuerte">{d.titulo}</Link>
                    {d.versionActual > 1 && <span className="texto-suave peq"> · v{d.versionActual}</span>}
                  </td>
                  <td>
                    {d.identificador
                      ? <Link to={`/expediente/${encodeURIComponent(d.identificador)}`} className="mono enlace">{d.identificador}</Link>
                      : <span className="texto-suave">—</span>}
                    {d.nombreTitular && <div className="peq">{d.nombreTitular}</div>}
                  </td>
                  <td>{d.tipoDocumento}</td>
                  <td>{d.area}</td>
                  <td className="nowrap">{d.fechaDocumento ? fmtFechaSimple(d.fechaDocumento) : fmtFecha(d.creadoEn)}</td>
                  <td className="der nowrap texto-suave">{fmtTamano(d.tamanoBytes)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {res && <Paginador pagina={res.pagina} total={res.total} tamano={TAM} alCambiar={(p) => setAplicado((a) => ({ ...a, pagina: p }))} />}
    </>
  )
}

function BuscarExpedientes() {
  const avisar = useAvisos()
  const [q, setQ] = useState('')
  const [lista, setLista] = useState<ExpedienteResumen[]>()
  const [cargando, setCargando] = useState(false)

  const buscar = useCallback(async (texto: string) => {
    setCargando(true)
    try { setLista(await api.get<ExpedienteResumen[]>(`/api/expedientes?q=${encodeURIComponent(texto)}`)) }
    catch (e) { avisar(mensajeError(e), 'error') }
    finally { setCargando(false) }
  }, [avisar])

  useEffect(() => { buscar('') }, [buscar])

  return (
    <>
      <form className="buscador" onSubmit={(e) => { e.preventDefault(); buscar(q) }}>
        <div className="buscador-fila">
          <input className="buscador-input" placeholder="Cédula, número de socio, expediente o nombre del titular…"
            value={q} onChange={(e) => setQ(e.target.value)} autoFocus />
          <button className="btn btn-pri">Buscar</button>
        </div>
      </form>
      <div className="resultados-cabecera">
        {lista && <span>{lista.length === 50 ? 'Mostrando los 50 más recientes' : `${lista.length} expediente${lista.length === 1 ? '' : 's'}`}</span>}
        {cargando && <Cargando texto="Buscando…" />}
      </div>
      {lista && lista.length === 0 && !cargando && (
        <Vacio titulo="No hay expedientes que coincidan">Los expedientes se forman con el identificador del titular que se captura en cada documento.</Vacio>
      )}
      <div className="rejilla-expedientes">
        {lista?.map((x) => (
          <Link key={x.identificador} to={`/expediente/${encodeURIComponent(x.identificador)}`} className="tarjeta-expediente">
            <div className="exp-cab">
              <span className="mono exp-id">{x.identificador}</span>
              <span className="exp-cant">{x.cantidadDocumentos} doc.</span>
            </div>
            <strong>{x.nombreTitular ?? 'Titular sin nombre'}</strong>
            <div className="exp-tipos">{x.tipos.slice(0, 4).map((t) => <span key={t}>{t}</span>)}{x.tipos.length > 4 && <span>+{x.tipos.length - 4}</span>}</div>
            <small className="texto-suave">Último documento: {fmtFecha(x.ultimaModificacion)}</small>
          </Link>
        ))}
      </div>
    </>
  )
}
