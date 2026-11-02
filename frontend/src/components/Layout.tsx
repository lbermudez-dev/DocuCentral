import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { P } from '../api/tipos'
import { api } from '../api/cliente'

const Icono = ({ d }: { d: string }) => (
  <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden><path d={d} /></svg>
)
const I = {
  buscar: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM21 21l-4.3-4.3',
  cargar: 'M12 16V4M7 9l5-5 5 5M5 20h14',
  bandeja: 'M4 13h4l2 3h4l2-3h4M4 13l2.5-8h11L20 13v6a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1z',
  tipos: 'M4 6h16M4 12h10M4 18h7',
  areas: 'M3 21V8l9-5 9 5v13M9 21v-6h6v6',
  usuarios: 'M16 19v-1a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v1M9.5 10a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM21 19v-1a4 4 0 0 0-3-3.9M16 3.1a3.5 3.5 0 0 1 0 6.8',
  roles: 'M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z',
  config: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z',
  auditoria: 'M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2M9 5a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2M9 5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2M9 13l2 2 4-4',
  salir: 'M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9',
}

export function Layout() {
  const { usuario, tiene, cerrar } = useAuth()
  const [menuAbierto, setMenuAbierto] = useState(false)
  const [pendientes, setPendientes] = useState(0)
  const loc = useLocation()

  useEffect(() => setMenuAbierto(false), [loc.pathname])
  useEffect(() => {
    if (!tiene(P.BandejaIndexar)) return
    const cargar = () => api.get<{ pendientesIndexar: number }>('/api/bandeja/estado').then((e) => setPendientes(e.pendientesIndexar)).catch(() => {})
    cargar()
    const t = setInterval(cargar, 60000)
    return () => clearInterval(t)
  }, [tiene, loc.pathname])

  const admin = [
    tiene(P.TiposAdministrar) && { a: '/admin/tipos', t: 'Tipos de documento', i: I.tipos },
    tiene(P.AreasAdministrar) && { a: '/admin/areas', t: 'Áreas', i: I.areas },
    tiene(P.UsuariosAdministrar) && { a: '/admin/usuarios', t: 'Usuarios', i: I.usuarios },
    tiene(P.RolesAdministrar) && { a: '/admin/roles', t: 'Roles y permisos', i: I.roles },
    tiene(P.ConfiguracionAdministrar) && { a: '/admin/configuracion', t: 'Configuración', i: I.config },
    tiene(P.AuditoriaVer) && { a: '/admin/auditoria', t: 'Auditoría', i: I.auditoria },
  ].filter(Boolean) as { a: string; t: string; i: string }[]

  return (
    <div className="app">
      <aside className={`lateral ${menuAbierto ? 'abierto' : ''}`}>
        <div className="marca">
          <img src="/favicon.svg" alt="" width={30} height={30} />
          <div><strong>DocuCentral</strong><small>Gestión documental</small></div>
        </div>
        <nav>
          {tiene(P.DocumentosVer) && <NavLink to="/" end><Icono d={I.buscar} />Buscar</NavLink>}
          {tiene(P.DocumentosCargar) && <NavLink to="/cargar"><Icono d={I.cargar} />Cargar documentos</NavLink>}
          {tiene(P.BandejaIndexar) && (
            <NavLink to="/bandeja"><Icono d={I.bandeja} />Bandeja de entrada
              {pendientes > 0 && <span className="contador">{pendientes > 99 ? '99+' : pendientes}</span>}
            </NavLink>
          )}
          {admin.length > 0 && <div className="nav-grupo">Administración</div>}
          {admin.map((x) => <NavLink key={x.a} to={x.a}><Icono d={x.i} />{x.t}</NavLink>)}
        </nav>
        <div className="usuario-pie">
          <div className="avatar">{usuario?.nombreCompleto.split(' ').map((p) => p[0]).slice(0, 2).join('')}</div>
          <div className="usuario-datos">
            <strong title={usuario?.nombreCompleto}>{usuario?.nombreCompleto}</strong>
            <small>{usuario?.area ?? usuario?.nombreUsuario}</small>
          </div>
          <NavLink to="/cuenta" className="btn-icono claro" title="Mi cuenta" aria-label="Mi cuenta"><Icono d={I.config} /></NavLink>
          <button className="btn-icono claro" onClick={cerrar} title="Cerrar sesión" aria-label="Cerrar sesión"><Icono d={I.salir} /></button>
        </div>
      </aside>
      {menuAbierto && <div className="lateral-fondo" onClick={() => setMenuAbierto(false)} />}
      <div className="principal">
        <header className="barra-movil">
          <button className="btn-icono" onClick={() => setMenuAbierto(true)} aria-label="Abrir menú">☰</button>
          <strong>DocuCentral</strong>
        </header>
        <main className="contenido"><Outlet /></main>
      </div>
    </div>
  )
}
