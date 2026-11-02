import { Fragment, useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api/cliente'
import type { DefinicionPermiso, Pagina, RegistroAuditoria, Rol } from '../../api/tipos'
import { Cargando, Insignia, Modal, Paginador, mensajeError, useAvisos } from '../../components/ui'
import { fmtFechaHora } from '../../utils'
import { ETIQUETAS_ACCION } from '../DocumentoDetalle'

// ───────────────────────────── Roles y permisos ─────────────────────────────

export function AdminRoles() {
  const avisar = useAvisos()
  const [roles, setRoles] = useState<Rol[]>()
  const [catalogo, setCatalogo] = useState<DefinicionPermiso[]>([])
  const [edit, setEdit] = useState<{ id?: number; nombre: string; descripcion?: string; permisos: string[] }>()
  const [error, setError] = useState<string>()

  const cargar = useCallback(() => api.get<Rol[]>('/api/roles').then(setRoles).catch((e) => avisar(mensajeError(e), 'error')), [avisar])
  useEffect(() => { cargar(); api.get<DefinicionPermiso[]>('/api/roles/permisos').then(setCatalogo).catch(() => {}) }, [cargar])

  const grupos = [...new Set(catalogo.map((p) => p.grupo))]

  const guardar = async () => {
    if (!edit) return
    setError(undefined)
    try {
      if (edit.id) await api.put(`/api/roles/${edit.id}`, edit)
      else await api.post('/api/roles', edit)
      setEdit(undefined)
      cargar()
      avisar('Rol guardado. Los usuarios con este rol deberán iniciar sesión de nuevo.')
    } catch (e) { setError(mensajeError(e)) }
  }

  const eliminar = async (r: Rol) => {
    if (!confirm(`¿Eliminar el rol "${r.nombre}"?`)) return
    try { await api.del(`/api/roles/${r.id}`); cargar(); avisar('Rol eliminado.') } catch (e) { avisar(mensajeError(e), 'error') }
  }

  return (
    <>
      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">Roles y permisos</h1>
          <p className="texto-suave">Un rol agrupa acciones permitidas. El alcance por área se define en cada usuario.</p>
        </div>
        <button className="btn btn-pri" onClick={() => { setError(undefined); setEdit({ nombre: '', permisos: [] }) }}>Nuevo rol</button>
      </div>
      {!roles ? <Cargando /> : (
        <div className="tabla-envoltura">
          <table className="tabla matriz">
            <thead>
              <tr>
                <th>Permiso</th>
                {roles.map((r) => (
                  <th key={r.id} className="centro">
                    <div>{r.nombre}</div>
                    <div className="peq texto-suave">{r.usuarios} usuario{r.usuarios === 1 ? '' : 's'}</div>
                    {!r.esSistema && (
                      <div className="nowrap">
                        <button className="btn-enlace peq" onClick={() => { setError(undefined); setEdit({ id: r.id, nombre: r.nombre, descripcion: r.descripcion, permisos: r.permisos }) }}>Editar</button>
                        {r.usuarios === 0 && <> · <button className="btn-enlace peq peligro" onClick={() => eliminar(r)}>Eliminar</button></>}
                      </div>
                    )}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {grupos.map((g) => (
                <Fragment key={g}>
                  <tr className="fila-grupo"><td colSpan={roles.length + 1}>{g}</td></tr>
                  {catalogo.filter((p) => p.grupo === g).map((p) => (
                    <tr key={p.codigo}>
                      <td>{p.descripcion}<div className="peq mono texto-suave">{p.codigo}</div></td>
                      {roles.map((r) => <td key={r.id} className="centro">{r.permisos.includes(p.codigo) ? <span className="marca-si">✓</span> : <span className="texto-suave">·</span>}</td>)}
                    </tr>
                  ))}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Modal titulo={edit?.id ? 'Editar rol' : 'Nuevo rol'} abierto={!!edit} alCerrar={() => setEdit(undefined)} ancho={620} pie={
        <><button className="btn btn-sec" onClick={() => setEdit(undefined)}>Cancelar</button><button className="btn btn-pri" onClick={guardar}>Guardar</button></>
      }>
        {edit && (
          <>
            <div className="form-grid">
              <label className="campo"><span>Nombre</span><input value={edit.nombre} onChange={(e) => setEdit({ ...edit, nombre: e.target.value })} maxLength={100} /></label>
              <label className="campo"><span>Descripción</span><input value={edit.descripcion ?? ''} onChange={(e) => setEdit({ ...edit, descripcion: e.target.value })} maxLength={300} /></label>
            </div>
            {grupos.map((g) => (
              <fieldset key={g} className="grupo-casillas">
                <legend>{g}</legend>
                {catalogo.filter((p) => p.grupo === g).map((p) => (
                  <label key={p.codigo} className="casilla">
                    <input type="checkbox" checked={edit.permisos.includes(p.codigo)}
                      onChange={(e) => setEdit({ ...edit, permisos: e.target.checked ? [...edit.permisos, p.codigo] : edit.permisos.filter((x) => x !== p.codigo) })} />
                    {p.descripcion}
                  </label>
                ))}
              </fieldset>
            ))}
          </>
        )}
        {error && <div className="alerta alerta-error">{error}</div>}
      </Modal>
    </>
  )
}
// ───────────────────────────── Configuración ─────────────────────────────

const K = {
  repo: 'Almacenamiento.RutaRepositorio',
  entrada: 'Almacenamiento.RutaEntrada',
  procesados: 'Almacenamiento.RutaProcesados',
  errores: 'Almacenamiento.RutaErrores',
  ext: 'Almacenamiento.ExtensionesPermitidas',
  tam: 'Almacenamiento.TamanoMaximoMB',
  activa: 'Almacenamiento.CarpetaVigiladaActiva',
  intervalo: 'Almacenamiento.IntervaloSegundos',
}

export function AdminConfiguracion() {
  const avisar = useAvisos()
  const [v, setV] = useState<Record<string, string>>()
  const [inicial, setInicial] = useState<Record<string, string>>()
  const [error, setError] = useState<string>()
  const [guardando, setGuardando] = useState(false)

  useEffect(() => {
    api.get<Record<string, string | null>>('/api/configuracion').then((r) => {
      const limpio = Object.fromEntries(Object.entries(r).map(([k, x]) => [k, x ?? '']))
      setV(limpio); setInicial(limpio)
    }).catch((e) => setError(mensajeError(e)))
  }, [])

  if (!v) return error ? <div className="alerta alerta-error">{error}</div> : <Cargando />
  const set = (k: string, x: string) => setV({ ...v, [k]: x })
  const repoCambio = inicial && inicial[K.repo] !== v[K.repo]

  const probar = async (ruta: string) => {
    try {
      const r = await api.post<{ mensaje: string }>('/api/configuracion/probar-ruta', ruta)
      avisar(r.mensaje)
    } catch (e) { avisar(mensajeError(e), 'error') }
  }

  const guardar = async () => {
    setError(undefined)
    setGuardando(true)
    try { await api.put('/api/configuracion', v); setInicial(v); avisar('Configuración guardada.') }
    catch (e) { setError(mensajeError(e)) }
    finally { setGuardando(false) }
  }

  // Función de render (no componente) para no perder el foco del input al escribir
  const ruta = (k: string, titulo: string, ayuda: string, opcional = false) => (
    <label className="campo col-2"><span>{titulo}{opcional && <span className="texto-suave"> (opcional)</span>}</span>
      <div className="con-boton">
        <input className="mono" value={v[k] ?? ''} onChange={(e) => set(k, e.target.value)} />
        <button type="button" className="btn btn-sec" disabled={!v[k]} onClick={() => probar(v[k])}>Probar</button>
      </div>
      <small>{ayuda}</small></label>
  )

  return (
    <>
      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">Configuración</h1>
          <p className="texto-suave">Rutas de almacenamiento e importación. Admiten carpetas locales (D:\Documentos), de red (\\servidor\docs) o rutas Linux/Docker (/data/docs).</p>
        </div>
      </div>
      <div className="tarjeta">
        <h2 className="subtitulo">Repositorio</h2>
        <div className="form-grid">
          {ruta(K.repo, "Ruta del repositorio central", "Donde quedan guardados todos los archivos, organizados por año/mes/documento.")}
          {repoCambio && (
            <div className="alerta alerta-alerta col-2">
              Cambiar esta ruta no mueve los archivos existentes. Antes de guardar, copie el contenido de la ruta anterior a la nueva conservando la estructura de carpetas; de lo contrario los documentos actuales no se podrán abrir.
            </div>
          )}
          <label className="campo"><span>Extensiones permitidas</span>
            <input className="mono" value={v[K.ext]} onChange={(e) => set(K.ext, e.target.value)} /><small>Separadas por coma.</small></label>
          <label className="campo"><span>Tamaño máximo por archivo (MB)</span>
            <input type="number" min={1} value={v[K.tam]} onChange={(e) => set(K.tam, e.target.value)} /></label>
        </div>

        <h2 className="subtitulo">Carpeta vigilada (importación automática)</h2>
        <div className="form-grid">
          <label className="casilla col-2">
            <input type="checkbox" checked={v[K.activa] === 'true'} onChange={(e) => set(K.activa, e.target.checked ? 'true' : 'false')} />
            Importar automáticamente los archivos que lleguen a la carpeta de entrada
          </label>
          {ruta(K.entrada, "Carpeta de entrada", "El escáner o los usuarios copian aquí los archivos. Use subcarpetas con el código de un área para asignarla automáticamente.")}
          {ruta(K.procesados, "Carpeta de procesados", "Si se indica, se deja aquí una copia del original tras importarlo. Si está vacía, el original se elimina de la entrada (ya queda en el repositorio).", true)}
          {ruta(K.errores, "Carpeta de errores", "Archivos rechazados (extensión no permitida, demasiado grandes o duplicados), con un .txt que explica el motivo.")}
          <label className="campo"><span>Revisar cada (segundos)</span>
            <input type="number" min={5} value={v[K.intervalo]} onChange={(e) => set(K.intervalo, e.target.value)} /></label>
        </div>
        {error && <div className="alerta alerta-error">{error}</div>}
        <div className="acciones fin"><button className="btn btn-pri" onClick={guardar} disabled={guardando}>{guardando ? 'Verificando rutas…' : 'Guardar configuración'}</button></div>
      </div>
    </>
  )
}

// ───────────────────────────── Auditoría ─────────────────────────────

export function AdminAuditoria() {
  const avisar = useAvisos()
  const [f, setF] = useState({ usuario: '', accion: '', texto: '', desde: '', hasta: '' })
  const [aplicado, setAplicado] = useState({ ...f, pagina: 1 })
  const [res, setRes] = useState<Pagina<RegistroAuditoria>>()
  const [acciones, setAcciones] = useState<string[]>([])

  useEffect(() => { api.get<string[]>('/api/auditoria/acciones').then(setAcciones).catch(() => {}) }, [])
  useEffect(() => {
    const qs = new URLSearchParams(Object.entries(aplicado).filter(([, x]) => x !== '' && x != null).map(([k, x]) => [k, String(x)]))
    qs.set('tamano', '50')
    api.get<Pagina<RegistroAuditoria>>(`/api/auditoria?${qs}`).then(setRes).catch((e) => avisar(mensajeError(e), 'error'))
  }, [aplicado, avisar])

  const tono = (a: string) => (a.includes('FALLIDO') || a.includes('BLOQUEADO') || a === 'ELIMINAR' ? 'error' : a.startsWith('DESCARGAR') ? 'alerta' : 'neutro')

  return (
    <>
      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">Auditoría</h1>
          <p className="texto-suave">Registro de inicios de sesión, consultas, descargas y cambios.</p>
        </div>
      </div>
      <form className="filtros tarjeta" onSubmit={(e) => { e.preventDefault(); setAplicado({ ...f, pagina: 1 }) }}>
        <label className="campo"><span>Usuario</span><input value={f.usuario} onChange={(e) => setF({ ...f, usuario: e.target.value })} /></label>
        <label className="campo"><span>Acción</span>
          <select value={f.accion} onChange={(e) => setF({ ...f, accion: e.target.value })}>
            <option value="">Todas</option>
            {acciones.map((a) => <option key={a} value={a}>{ETIQUETAS_ACCION[a] ?? a}</option>)}
          </select></label>
        <label className="campo"><span>Texto / Id</span><input value={f.texto} onChange={(e) => setF({ ...f, texto: e.target.value })} /></label>
        <label className="campo"><span>Desde</span><input type="date" value={f.desde} onChange={(e) => setF({ ...f, desde: e.target.value })} /></label>
        <label className="campo"><span>Hasta</span><input type="date" value={f.hasta} onChange={(e) => setF({ ...f, hasta: e.target.value })} /></label>
        <div className="filtros-acciones"><button className="btn btn-pri">Filtrar</button></div>
      </form>
      {!res ? <Cargando /> : (
        <>
          <div className="resultados-cabecera"><span>{res.total.toLocaleString('es-NI')} registros</span></div>
          <div className="tabla-envoltura">
            <table className="tabla compacta">
              <thead><tr><th>Fecha</th><th>Usuario</th><th>Acción</th><th>Objeto</th><th>Detalle</th><th>IP</th></tr></thead>
              <tbody>
                {res.items.map((a) => (
                  <tr key={a.id}>
                    <td className="nowrap">{fmtFechaHora(a.fecha)}</td>
                    <td className="mono">{a.nombreUsuario}</td>
                    <td><Insignia tono={tono(a.accion) as any}>{ETIQUETAS_ACCION[a.accion] ?? a.accion}</Insignia></td>
                    <td className="peq">{a.entidad}{a.entidadId && <div className="mono texto-suave">{a.entidad === 'Documento' ? <Link to={`/documentos/${a.entidadId}`}>{a.entidadId.slice(0, 8)}…</Link> : a.entidadId}</div>}</td>
                    <td className="peq">{a.detalle}</td>
                    <td className="peq mono texto-suave">{a.direccionIp}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Paginador pagina={res.pagina} total={res.total} tamano={50} alCambiar={(p) => setAplicado((x) => ({ ...x, pagina: p }))} />
        </>
      )}
    </>
  )
}
