import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api } from '../api/cliente'
import type { LoginResponse } from '../api/tipos'
import { TipoAutenticacion } from '../api/tipos'
import { mensajeError, useAvisos } from '../components/ui'

export function Login() {
  const { iniciar } = useAuth()
  const nav = useNavigate()
  const [usuario, setUsuario] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string>()
  const [enviando, setEnviando] = useState(false)
  const [dominio, setDominio] = useState<string>()

  useEffect(() => {
    api.get<{ directorioHabilitado: boolean; dominio: string }>('/api/auth/config')
      .then((c) => c.directorioHabilitado && setDominio(c.dominio)).catch(() => {})
  }, [])

  const enviar = async (e: FormEvent) => {
    e.preventDefault()
    setError(undefined)
    setEnviando(true)
    try {
      const u = await iniciar(usuario, password)
      nav(u.debeCambiarPassword ? '/cuenta' : '/', { replace: true })
    } catch (err) {
      setError(mensajeError(err))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className="login">
      <div className="login-panel">
        <div className="login-marca">
          <img src="/favicon.svg" alt="" width={44} height={44} />
          <div>
            <h1>DocuCentral</h1>
            <p>Repositorio central de documentos</p>
          </div>
        </div>
        <form onSubmit={enviar}>
          <label className="campo">
            <span>Usuario</span>
            <input value={usuario} onChange={(e) => setUsuario(e.target.value)} autoComplete="username" autoFocus required />
            {dominio && <small>Puede usar su usuario de red ({dominio}).</small>}
          </label>
          <label className="campo">
            <span>Contraseña</span>
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
          </label>
          {error && <div className="alerta alerta-error">{error}</div>}
          <button className="btn btn-pri btn-bloque" disabled={enviando}>{enviando ? 'Verificando…' : 'Iniciar sesión'}</button>
        </form>
      </div>
      <div className="login-arte" aria-hidden>
        <div className="pila">
          <span /><span /><span />
        </div>
        <p>Todos los documentos de una persona o expediente, en un solo lugar.</p>
      </div>
    </div>
  )
}

/** Mi cuenta: cambio de contraseña (obligatorio en el primer ingreso o tras un restablecimiento). */
export function Cuenta() {
  const { usuario, aplicarRespuesta } = useAuth()
  const avisar = useAvisos()
  const nav = useNavigate()
  const [actual, setActual] = useState('')
  const [nueva, setNueva] = useState('')
  const [confirmar, setConfirmar] = useState('')
  const [error, setError] = useState<string>()
  const [enviando, setEnviando] = useState(false)
  if (!usuario) return null
  const esDominio = usuario.tipoAutenticacion === TipoAutenticacion.Directorio

  const enviar = async (e: FormEvent) => {
    e.preventDefault()
    setError(undefined)
    if (nueva !== confirmar) { setError('Las contraseñas no coinciden.'); return }
    setEnviando(true)
    try {
      const r = await api.post<LoginResponse>('/api/auth/cambiar-password', { passwordActual: actual, passwordNueva: nueva })
      aplicarRespuesta(r)
      avisar('Contraseña actualizada.')
      setActual(''); setNueva(''); setConfirmar('')
      if (usuario.debeCambiarPassword) nav('/', { replace: true })
    } catch (err) {
      setError(mensajeError(err))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className={usuario.debeCambiarPassword ? 'login' : ''}>
      <div className={usuario.debeCambiarPassword ? 'login-panel' : 'tarjeta angosta'}>
        <h1 className="titulo-pagina">Mi cuenta</h1>
        <dl className="datos">
          <dt>Usuario</dt><dd className="mono">{usuario.nombreUsuario}</dd>
          <dt>Nombre</dt><dd>{usuario.nombreCompleto}</dd>
          <dt>Área</dt><dd>{usuario.area ?? '—'}</dd>
          <dt>Autenticación</dt><dd>{esDominio ? 'Active Directory' : 'Local'}</dd>
        </dl>
        {esDominio ? (
          <p className="texto-suave">Su contraseña se administra en Active Directory.</p>
        ) : (
          <form onSubmit={enviar}>
            <h2 className="subtitulo">Cambiar contraseña</h2>
            {usuario.debeCambiarPassword && (
              <div className="alerta alerta-info">Por seguridad, debe definir una contraseña nueva antes de continuar.</div>
            )}
            <label className="campo"><span>Contraseña actual</span>
              <input type="password" value={actual} onChange={(e) => setActual(e.target.value)} autoComplete="current-password" required /></label>
            <label className="campo"><span>Contraseña nueva</span>
              <input type="password" value={nueva} onChange={(e) => setNueva(e.target.value)} autoComplete="new-password" required />
              <small>Mínimo 8 caracteres, con mayúsculas, minúsculas y números.</small></label>
            <label className="campo"><span>Confirmar contraseña nueva</span>
              <input type="password" value={confirmar} onChange={(e) => setConfirmar(e.target.value)} autoComplete="new-password" required /></label>
            {error && <div className="alerta alerta-error">{error}</div>}
            <button className="btn btn-pri" disabled={enviando}>{enviando ? 'Guardando…' : 'Guardar contraseña'}</button>
          </form>
        )}
      </div>
    </div>
  )
}
