import type { ReactNode } from 'react'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { useAuth } from './auth/AuthContext'
import { P } from './api/tipos'
import { Layout } from './components/Layout'
import { Cargando, Vacio } from './components/ui'
import { Login, Cuenta } from './pages/Login'
import { Buscar } from './pages/Buscar'
import { Expediente } from './pages/Expediente'
import { DocumentoDetalle } from './pages/DocumentoDetalle'
import { Cargar } from './pages/Cargar'
import { Bandeja } from './pages/Bandeja'
import { AdminTipos } from './pages/admin/Tipos'
import { AdminAreas, AdminUsuarios } from './pages/admin/AreasUsuarios'
import { AdminAuditoria, AdminConfiguracion, AdminRoles } from './pages/admin/Sistema'

/** Exige sesión; si el usuario debe cambiar la contraseña lo lleva a "Mi cuenta". */
function Protegido({ children }: { children: ReactNode }) {
  const { usuario, cargando } = useAuth()
  const loc = useLocation()
  if (cargando) return <div className="centrado"><Cargando /></div>
  if (!usuario) return <Navigate to="/login" replace state={{ desde: loc.pathname }} />
  if (usuario.debeCambiarPassword && loc.pathname !== '/cuenta') return <Navigate to="/cuenta" replace />
  return <>{children}</>
}

/** Exige al menos uno de los permisos. */
function Requiere({ p, children }: { p: string[]; children: ReactNode }) {
  const { tiene } = useAuth()
  if (!tiene(...p)) return <Vacio titulo="Sin acceso">Su rol no tiene permiso para esta sección. Consulte con el administrador.</Vacio>
  return <>{children}</>
}

/** Página inicial según permisos: no todos los roles pueden buscar. */
function Inicio() {
  const { tiene } = useAuth()
  if (tiene(P.DocumentosVer)) return <Buscar />
  if (tiene(P.BandejaIndexar)) return <Navigate to="/bandeja" replace />
  if (tiene(P.DocumentosCargar)) return <Navigate to="/cargar" replace />
  return <Vacio titulo="Bienvenido">Su usuario aún no tiene permisos asignados. Consulte con el administrador.</Vacio>
}

export default function App() {
  const { usuario, cargando } = useAuth()
  return (
    <Routes>
      <Route path="/login" element={!cargando && usuario ? <Navigate to="/" replace /> : <Login />} />
      <Route path="/cuenta" element={
        <Protegido>{usuario?.debeCambiarPassword ? <Cuenta /> : <Layout />}</Protegido>
      }>
        <Route index element={<Cuenta />} />
      </Route>
      <Route element={<Protegido><Layout /></Protegido>}>
        <Route index element={<Inicio />} />
        <Route path="expediente/:identificador" element={<Requiere p={[P.DocumentosVer]}><Expediente /></Requiere>} />
        <Route path="documentos/:id" element={<Requiere p={[P.DocumentosVer]}><DocumentoDetalle /></Requiere>} />
        <Route path="cargar" element={<Requiere p={[P.DocumentosCargar]}><Cargar /></Requiere>} />
        <Route path="bandeja" element={<Requiere p={[P.BandejaIndexar]}><Bandeja /></Requiere>} />
        <Route path="admin/tipos" element={<Requiere p={[P.TiposAdministrar]}><AdminTipos /></Requiere>} />
        <Route path="admin/areas" element={<Requiere p={[P.AreasAdministrar]}><AdminAreas /></Requiere>} />
        <Route path="admin/usuarios" element={<Requiere p={[P.UsuariosAdministrar]}><AdminUsuarios /></Requiere>} />
        <Route path="admin/roles" element={<Requiere p={[P.RolesAdministrar]}><AdminRoles /></Requiere>} />
        <Route path="admin/configuracion" element={<Requiere p={[P.ConfiguracionAdministrar]}><AdminConfiguracion /></Requiere>} />
        <Route path="admin/auditoria" element={<Requiere p={[P.AuditoriaVer]}><AdminAuditoria /></Requiere>} />
        <Route path="*" element={<Vacio titulo="Página no encontrada" />} />
      </Route>
    </Routes>
  )
}
