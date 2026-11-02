import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, sesion, EVENTO_SESION_EXPIRADA } from '../api/cliente'
import type { LoginResponse, UsuarioSesion } from '../api/tipos'

interface Contexto {
  usuario: UsuarioSesion | null
  cargando: boolean
  iniciar: (usuario: string, password: string) => Promise<UsuarioSesion>
  cerrar: () => void
  aplicarRespuesta: (r: LoginResponse) => void
  tiene: (...permisos: string[]) => boolean
}

const AuthCtx = createContext<Contexto>(null!)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [usuario, setUsuario] = useState<UsuarioSesion | null>(null)
  const [cargando, setCargando] = useState(true)

  useEffect(() => {
    if (!sesion.token()) { setCargando(false); return }
    api.get<UsuarioSesion>('/api/auth/me')
      .then(setUsuario)
      .catch(() => sesion.limpiar())
      .finally(() => setCargando(false))
  }, [])

  useEffect(() => {
    const alExpirar = () => setUsuario(null)
    window.addEventListener(EVENTO_SESION_EXPIRADA, alExpirar)
    return () => window.removeEventListener(EVENTO_SESION_EXPIRADA, alExpirar)
  }, [])

  const aplicarRespuesta = useCallback((r: LoginResponse) => {
    sesion.guardar(r.token)
    setUsuario(r.usuario)
  }, [])

  const iniciar = useCallback(async (u: string, p: string) => {
    const r = await api.post<LoginResponse>('/api/auth/login', { usuario: u, password: p })
    aplicarRespuesta(r)
    return r.usuario
  }, [aplicarRespuesta])

  const cerrar = useCallback(() => {
    sesion.limpiar()
    setUsuario(null)
  }, [])

  /** true si el usuario tiene al menos uno de los permisos indicados. */
  const tiene = useCallback(
    (...permisos: string[]) => !!usuario && !usuario.debeCambiarPassword && permisos.some((p) => usuario.permisos.includes(p)),
    [usuario],
  )

  return (
    <AuthCtx.Provider value={{ usuario, cargando, iniciar, cerrar, aplicarRespuesta, tiene }}>
      {children}
    </AuthCtx.Provider>
  )
}

export const useAuth = () => useContext(AuthCtx)
