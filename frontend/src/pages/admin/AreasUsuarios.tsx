import { useCallback, useEffect, useState } from 'react'
import { api } from '../../api/cliente'
import { TipoAutenticacion, type Area, type Rol, type Usuario } from '../../api/tipos'
import { useAuth } from '../../auth/AuthContext'
import { Cargando, Insignia, Modal, mensajeError, useAvisos } from '../../components/ui'
import { fmtFechaHora } from '../../utils'

// ───────────────────────────── Áreas ─────────────────────────────

export function AdminAreas() {
  const avisar = useAvisos()
  const [areas, setAreas] = useState<Area[]>()
  const [edit, setEdit] = useState<Partial<Area>>()
  const [error, setError] = useState<string>()

  const cargar = useCallback(() => api.get<Area[]>('/api/areas').then(setAreas).catch((e) => avisar(mensajeError(e), 'error')), [avisar])
  useEffect(() => { cargar() }, [cargar])

  const guardar = async () => {
    if (!edit) return
    setError(undefined)
    const cuerpo = { codigo: edit.codigo ?? '', nombre: edit.nombre ?? '', activo: edit.activo ?? true }
    try {
      if (edit.id) await api.put(`/api/areas/${edit.id}`, cuerpo)
      else await api.post('/api/areas', cuerpo)
      setEdit(undefined)
      cargar()
      avisar('Área guardada.')
    } catch (e) { setError(mensajeError(e)) }
  }

  return (
    <>
      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">Áreas</h1>
          <p className="texto-suave">Cada documento pertenece a un área. Los usuarios ven los documentos de su área y de las áreas adicionales que se les asignen.</p>
        </div>
        <button className="btn btn-pri" onClick={() => { setError(undefined); setEdit({ activo: true }) }}>Nueva área</button>
      </div>
      {!areas ? <Cargando /> : (
        <div className="tabla-envoltura">
          <table className="tabla">
            <thead><tr><th>Código</th><th>Nombre</th><th>Estado</th><th></th></tr></thead>
            <tbody>
              {areas.map((a) => (
                <tr key={a.id}>
                  <td className="mono">{a.codigo}</td>
                  <td>{a.nombre}</td>
                  <td>{a.activo ? <Insignia tono="ok">Activa</Insignia> : <Insignia>Inactiva</Insignia>}</td>
                  <td className="der"><button className="btn btn-sec btn-sm" onClick={() => { setError(undefined); setEdit(a) }}>Editar</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <Modal titulo={edit?.id ? 'Editar área' : 'Nueva área'} abierto={!!edit} alCerrar={() => setEdit(undefined)} ancho={460} pie={
        <><button className="btn btn-sec" onClick={() => setEdit(undefined)}>Cancelar</button><button className="btn btn-pri" onClick={guardar}>Guardar</button></>
      }>
        <label className="campo"><span>Código</span>
          <input className="mono" value={edit?.codigo ?? ''} onChange={(e) => setEdit({ ...edit, codigo: e.target.value.toUpperCase() })} maxLength={30} />
          <small>También es el nombre de la subcarpeta de la carpeta de entrada para asignar el área automáticamente.</small></label>
        <label className="campo"><span>Nombre</span>
          <input value={edit?.nombre ?? ''} onChange={(e) => setEdit({ ...edit, nombre: e.target.value })} maxLength={150} /></label>
        <label className="casilla"><input type="checkbox" checked={edit?.activo ?? true} onChange={(e) => setEdit({ ...edit, activo: e.target.checked })} /> Activa</label>
        {error && <div className="alerta alerta-error">{error}</div>}
      </Modal>
    </>
  )
}

// ───────────────────────────── Usuarios ─────────────────────────────

interface UsuarioEdit {
  id?: number
  nombreUsuario: string
  nombreCompleto: string
  email?: string
  tipoAutenticacion: TipoAutenticacion
  activo: boolean
  areaId?: number | null
  rolIds: number[]
  areasAdicionalesIds: number[]
  password?: string
  tipoOriginal?: TipoAutenticacion
}

export function AdminUsuarios() {
  const avisar = useAvisos()
  const { usuario: yo } = useAuth()
  const [usuarios, setUsuarios] = useState<Usuario[]>()
  const [roles, setRoles] = useState<Rol[]>([])
  const [areas, setAreas] = useState<Area[]>([])
  const [q, setQ] = useState('')
  const [edit, setEdit] = useState<UsuarioEdit>()
  const [reset, setReset] = useState<Usuario>()
  const [nuevaPwd, setNuevaPwd] = useState('')
  const [error, setError] = useState<string>()

  const cargar = useCallback(() =>
    api.get<Usuario[]>(`/api/usuarios?q=${encodeURIComponent(q)}`).then(setUsuarios).catch((e) => avisar(mensajeError(e), 'error')), [q, avisar])
  useEffect(() => { cargar() }, [cargar])
  useEffect(() => {
    api.get<Rol[]>('/api/roles').then(setRoles).catch(() => {})
    api.get<Area[]>('/api/areas').then(setAreas).catch(() => {})
  }, [])

  const abrir = (u?: Usuario) => {
    setError(undefined)
    setEdit(u
      ? { ...u, email: u.email ?? '', tipoOriginal: u.tipoAutenticacion, password: '' }
      : { nombreUsuario: '', nombreCompleto: '', tipoAutenticacion: TipoAutenticacion.Local, activo: true, rolIds: [], areasAdicionalesIds: [], password: '' })
  }

  const guardar = async () => {
    if (!edit) return
    setError(undefined)
    const { id, tipoOriginal: _t, ...cuerpo } = edit
    try {
      if (id) await api.put(`/api/usuarios/${id}`, cuerpo)
      else await api.post('/api/usuarios', cuerpo)
      setEdit(undefined)
      cargar()
      avisar(id ? 'Usuario actualizado. Sus sesiones abiertas se cerraron para aplicar los cambios.' : 'Usuario creado. Deberá cambiar la contraseña en su primer ingreso.')
    } catch (e) { setError(mensajeError(e)) }
  }

  const alternar = (lista: number[], v: number) => (lista.includes(v) ? lista.filter((x) => x !== v) : [...lista, v])
  const pideContrasena = edit && edit.tipoAutenticacion === TipoAutenticacion.Local && (!edit.id || edit.tipoOriginal !== TipoAutenticacion.Local)

  return (
    <>
      <div className="cabecera-pagina">
        <div>
          <h1 className="titulo-pagina">Usuarios</h1>
          <p className="texto-suave">Usuarios locales o de Active Directory, con sus roles y áreas de acceso.</p>
        </div>
        <button className="btn btn-pri" onClick={() => abrir()}>Nuevo usuario</button>
      </div>
      <input className="buscador-input angosto" placeholder="Buscar usuario…" value={q} onChange={(e) => setQ(e.target.value)} />
      {!usuarios ? <Cargando /> : (
        <div className="tabla-envoltura">
          <table className="tabla">
            <thead><tr><th>Usuario</th><th>Área</th><th>Roles</th><th>Acceso</th><th>Último ingreso</th><th>Estado</th><th></th></tr></thead>
            <tbody>
              {usuarios.map((u) => (
                <tr key={u.id}>
                  <td><strong>{u.nombreCompleto}</strong><div className="peq mono texto-suave">{u.nombreUsuario}</div></td>
                  <td>{u.area ?? '—'}{u.areasAdicionalesIds.length > 0 && <div className="peq texto-suave">+{u.areasAdicionalesIds.length} área(s)</div>}</td>
                  <td><div className="etiquetas">{u.rolIds.map((r) => <span key={r}>{roles.find((x) => x.id === r)?.nombre ?? r}</span>)}</div></td>
                  <td>{u.tipoAutenticacion === TipoAutenticacion.Directorio ? 'Active Directory' : 'Local'}</td>
                  <td className="nowrap peq">{fmtFechaHora(u.ultimoAcceso)}</td>
                  <td>
                    {!u.activo ? <Insignia>Inactivo</Insignia> : u.bloqueado ? <Insignia tono="error">Bloqueado</Insignia> : <Insignia tono="ok">Activo</Insignia>}
                  </td>
                  <td className="der nowrap">
                    {u.bloqueado && <button className="btn btn-sec btn-sm" onClick={async () => { await api.post(`/api/usuarios/${u.id}/desbloquear`); cargar(); avisar('Usuario desbloqueado.') }}>Desbloquear</button>}
                    {u.tipoAutenticacion === TipoAutenticacion.Local && <button className="btn btn-sec btn-sm" onClick={() => { setNuevaPwd(''); setError(undefined); setReset(u) }}>Contraseña</button>}
                    <button className="btn btn-sec btn-sm" onClick={() => abrir(u)}>Editar</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Modal titulo={edit?.id ? 'Editar usuario' : 'Nuevo usuario'} abierto={!!edit} alCerrar={() => setEdit(undefined)} ancho={680} pie={
        <><button className="btn btn-sec" onClick={() => setEdit(undefined)}>Cancelar</button><button className="btn btn-pri" onClick={guardar}>Guardar</button></>
      }>
        {edit && (
          <div className="form-grid">
            <label className="campo"><span>Usuario</span>
              <input className="mono" value={edit.nombreUsuario} disabled={!!edit.id} onChange={(e) => setEdit({ ...edit, nombreUsuario: e.target.value })} maxLength={100} />
              {edit.tipoAutenticacion === TipoAutenticacion.Directorio && <small>Igual que su usuario de red (sAMAccountName).</small>}</label>
            <label className="campo"><span>Nombre completo</span>
              <input value={edit.nombreCompleto} onChange={(e) => setEdit({ ...edit, nombreCompleto: e.target.value })} maxLength={200} /></label>
            <label className="campo"><span>Correo</span>
              <input type="email" value={edit.email ?? ''} onChange={(e) => setEdit({ ...edit, email: e.target.value })} maxLength={200} /></label>
            <label className="campo"><span>Autenticación</span>
              <select value={edit.tipoAutenticacion} onChange={(e) => setEdit({ ...edit, tipoAutenticacion: Number(e.target.value) })}>
                <option value={TipoAutenticacion.Local}>Local (usuario y contraseña del sistema)</option>
                <option value={TipoAutenticacion.Directorio}>Active Directory</option>
              </select></label>
            {pideContrasena && (
              <label className="campo col-2"><span>Contraseña inicial</span>
                <input type="password" value={edit.password ?? ''} onChange={(e) => setEdit({ ...edit, password: e.target.value })} autoComplete="new-password" />
                <small>Mínimo 8 caracteres con mayúsculas, minúsculas y números. Se pedirá cambiarla al ingresar.</small></label>
            )}
            <label className="campo"><span>Área principal</span>
              <select value={edit.areaId ?? ''} onChange={(e) => setEdit({ ...edit, areaId: e.target.value ? Number(e.target.value) : null })}>
                <option value="">Sin área</option>
                {areas.filter((a) => a.activo).map((a) => <option key={a.id} value={a.id}>{a.nombre}</option>)}
              </select></label>
            <label className="casilla col-1 alinear-fin">
              <input type="checkbox" checked={edit.activo} disabled={edit.id === yo?.id} onChange={(e) => setEdit({ ...edit, activo: e.target.checked })} /> Usuario activo
            </label>
            <fieldset className="col-2 grupo-casillas">
              <legend>Roles</legend>
              {roles.map((r) => (
                <label key={r.id} className="casilla">
                  <input type="checkbox" checked={edit.rolIds.includes(r.id)} onChange={() => setEdit({ ...edit, rolIds: alternar(edit.rolIds, r.id) })} />
                  <span><strong>{r.nombre}</strong>{r.descripcion && <small className="texto-suave"> — {r.descripcion}</small>}</span>
                </label>
              ))}
            </fieldset>
            <fieldset className="col-2 grupo-casillas columnas">
              <legend>Áreas adicionales que puede consultar</legend>
              {areas.filter((a) => a.activo && a.id !== edit.areaId).map((a) => (
                <label key={a.id} className="casilla">
                  <input type="checkbox" checked={edit.areasAdicionalesIds.includes(a.id)} onChange={() => setEdit({ ...edit, areasAdicionalesIds: alternar(edit.areasAdicionalesIds, a.id) })} /> {a.nombre}
                </label>
              ))}
            </fieldset>
          </div>
        )}
        {error && <div className="alerta alerta-error">{error}</div>}
      </Modal>

      <Modal titulo={`Restablecer contraseña: ${reset?.nombreUsuario ?? ''}`} abierto={!!reset} alCerrar={() => setReset(undefined)} ancho={440} pie={
        <><button className="btn btn-sec" onClick={() => setReset(undefined)}>Cancelar</button>
          <button className="btn btn-pri" onClick={async () => {
            try { await api.post(`/api/usuarios/${reset!.id}/restablecer-password`, { passwordNueva: nuevaPwd }); setReset(undefined); avisar('Contraseña restablecida. Se pedirá cambiarla al ingresar.') }
            catch (e) { setError(mensajeError(e)) }
          }}>Restablecer</button></>
      }>
        <label className="campo"><span>Contraseña temporal</span>
          <input type="password" value={nuevaPwd} onChange={(e) => setNuevaPwd(e.target.value)} autoComplete="new-password" /></label>
        {error && <div className="alerta alerta-error">{error}</div>}
      </Modal>
    </>
  )
}
