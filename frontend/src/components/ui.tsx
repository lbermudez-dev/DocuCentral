import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'

// ── Avisos (toasts)
type TipoAviso = 'ok' | 'error' | 'info'
interface Aviso { id: number; tipo: TipoAviso; texto: string }
const AvisosCtx = createContext<(texto: string, tipo?: TipoAviso) => void>(() => {})

export function AvisosProvider({ children }: { children: ReactNode }) {
  const [avisos, setAvisos] = useState<Aviso[]>([])
  const avisar = useCallback((texto: string, tipo: TipoAviso = 'ok') => {
    const id = Date.now() + Math.random()
    setAvisos((a) => [...a, { id, tipo, texto }])
    setTimeout(() => setAvisos((a) => a.filter((x) => x.id !== id)), tipo === 'error' ? 7000 : 3500)
  }, [])
  return (
    <AvisosCtx.Provider value={avisar}>
      {children}
      <div className="avisos" role="status" aria-live="polite">
        {avisos.map((a) => (
          <div key={a.id} className={`aviso aviso-${a.tipo}`}>{a.texto}</div>
        ))}
      </div>
    </AvisosCtx.Provider>
  )
}
export const useAvisos = () => useContext(AvisosCtx)

/** Mensaje legible a partir de un error de la API. */
export const mensajeError = (e: unknown) => (e instanceof Error ? e.message : 'Ocurrió un error inesperado.')

// ── Modal
export function Modal({ titulo, abierto, alCerrar, children, ancho = 560, pie }: {
  titulo: string
  abierto: boolean
  alCerrar: () => void
  children: ReactNode
  ancho?: number
  pie?: ReactNode
}) {
  useEffect(() => {
    if (!abierto) return
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', esc)
    return () => window.removeEventListener('keydown', esc)
  }, [abierto, alCerrar])
  if (!abierto) return null
  return (
    <div className="modal-fondo" onMouseDown={(e) => e.target === e.currentTarget && alCerrar()}>
      <div className="modal" style={{ maxWidth: ancho }} role="dialog" aria-modal="true" aria-label={titulo}>
        <div className="modal-cabecera">
          <h2>{titulo}</h2>
          <button className="btn-icono" onClick={alCerrar} aria-label="Cerrar">×</button>
        </div>
        <div className="modal-cuerpo">{children}</div>
        {pie && <div className="modal-pie">{pie}</div>}
      </div>
    </div>
  )
}

export function Cargando({ texto = 'Cargando…' }: { texto?: string }) {
  return <div className="cargando"><span className="spinner" />{texto}</div>
}

export function Vacio({ titulo, children }: { titulo: string; children?: ReactNode }) {
  return (
    <div className="vacio">
      <div className="vacio-icono" aria-hidden>
        <svg viewBox="0 0 48 48" width="44" height="44"><path d="M12 6h16l10 10v24a2 2 0 0 1-2 2H12a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z" fill="none" stroke="currentColor" strokeWidth="2"/><path d="M28 6v10h10" fill="none" stroke="currentColor" strokeWidth="2"/></svg>
      </div>
      <strong>{titulo}</strong>
      {children && <p>{children}</p>}
    </div>
  )
}

export function Paginador({ pagina, total, tamano, alCambiar }: { pagina: number; total: number; tamano: number; alCambiar: (p: number) => void }) {
  const paginas = Math.max(1, Math.ceil(total / tamano))
  if (total <= tamano) return null
  return (
    <div className="paginador">
      <button className="btn btn-sec btn-sm" disabled={pagina <= 1} onClick={() => alCambiar(pagina - 1)}>Anterior</button>
      <span>Página {pagina} de {paginas}</span>
      <button className="btn btn-sec btn-sm" disabled={pagina >= paginas} onClick={() => alCambiar(pagina + 1)}>Siguiente</button>
    </div>
  )
}

export function Insignia({ children, tono = 'neutro' }: { children: ReactNode; tono?: 'neutro' | 'ok' | 'alerta' | 'error' | 'acento' }) {
  return <span className={`insignia insignia-${tono}`}>{children}</span>
}

export function ChipExt({ ext }: { ext?: string }) {
  const e = (ext ?? '').replace('.', '').toUpperCase() || 'DOC'
  const clase = e === 'PDF' ? 'pdf' : ['JPG', 'JPEG', 'PNG', 'TIF', 'TIFF', 'GIF', 'BMP'].includes(e) ? 'img' : ['DOC', 'DOCX'].includes(e) ? 'doc' : ['XLS', 'XLSX'].includes(e) ? 'xls' : 'otro'
  return <span className={`chip-ext chip-${clase}`}>{e}</span>
}

/** Confirmación con motivo obligatorio (eliminar, descartar). */
export function ConfirmarConMotivo({ abierto, titulo, texto, accion, alCerrar, alConfirmar }: {
  abierto: boolean
  titulo: string
  texto: string
  accion: string
  alCerrar: () => void
  alConfirmar: (motivo: string) => Promise<void>
}) {
  const [motivo, setMotivo] = useState('')
  const [enviando, setEnviando] = useState(false)
  useEffect(() => { if (abierto) setMotivo('') }, [abierto])
  return (
    <Modal titulo={titulo} abierto={abierto} alCerrar={alCerrar} ancho={460} pie={
      <>
        <button className="btn btn-sec" onClick={alCerrar}>Cancelar</button>
        <button className="btn btn-peligro" disabled={!motivo.trim() || enviando}
          onClick={async () => { setEnviando(true); try { await alConfirmar(motivo.trim()) } finally { setEnviando(false) } }}>
          {accion}
        </button>
      </>
    }>
      <p className="texto-suave">{texto}</p>
      <label className="campo">
        <span>Motivo</span>
        <textarea rows={3} value={motivo} onChange={(e) => setMotivo(e.target.value)} autoFocus />
      </label>
    </Modal>
  )
}
