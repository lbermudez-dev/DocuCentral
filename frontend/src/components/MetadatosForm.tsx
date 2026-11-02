import { useEffect, useState } from 'react'
import { api } from '../api/cliente'
import { TipoDatoCampo, type Area, type Campo, type Metadatos, type TipoDocumento } from '../api/tipos'

/** Tipos de documento activos y áreas accesibles para el usuario. */
export function useCatalogos() {
  const [tipos, setTipos] = useState<TipoDocumento[]>([])
  const [areas, setAreas] = useState<Area[]>([])
  useEffect(() => {
    api.get<TipoDocumento[]>('/api/tipos-documento?soloActivos=true').then(setTipos).catch(() => {})
    api.get<Area[]>('/api/areas?soloAccesibles=true').then(setAreas).catch(() => {})
  }, [])
  return { tipos, areas }
}

export const metadatosVacios = (): Metadatos => ({
  tipoDocumentoId: 0, areaId: 0, titulo: '', descripcion: '', identificador: '', nombreTitular: '',
  etiquetas: '', fechaDocumento: '', valores: {},
})

/** Valida en el cliente lo mismo que el servidor, para avisar antes de enviar. */
export function validarMetadatos(m: Metadatos, tipo?: TipoDocumento): string | null {
  if (!m.tipoDocumentoId) return 'Seleccione el tipo de documento.'
  if (!m.areaId) return 'Seleccione el área.'
  if (!m.titulo.trim()) return 'El título es obligatorio.'
  for (const c of tipo?.campos.filter((x) => x.activo) ?? [])
    if (c.obligatorio && !(m.valores[c.clave] ?? '').toString().trim()) return `'${c.etiqueta}' es obligatorio.`
  return null
}

export function aMetadatosApi(m: Metadatos): Metadatos {
  return { ...m, fechaDocumento: m.fechaDocumento ? m.fechaDocumento : null }
}

export function MetadatosForm({ valor, alCambiar, tipos, areas, compacto, ocultarTitulo }: {
  valor: Metadatos
  alCambiar: (m: Metadatos) => void
  tipos: TipoDocumento[]
  areas: Area[]
  compacto?: boolean
  /** En cargas múltiples cada archivo lleva su propio título. */
  ocultarTitulo?: boolean
}) {
  const tipo = tipos.find((t) => t.id === valor.tipoDocumentoId)
  const set = <K extends keyof Metadatos>(k: K, v: Metadatos[K]) => alCambiar({ ...valor, [k]: v })
  const setValor = (clave: string, v: string) => alCambiar({ ...valor, valores: { ...valor.valores, [clave]: v } })

  // Si solo hay un área disponible, se preselecciona
  useEffect(() => {
    if (!valor.areaId && areas.length === 1) set('areaId', areas[0].id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [areas])

  return (
    <div className={compacto ? 'form-grid form-compacto' : 'form-grid'}>
      <label className="campo">
        <span>Tipo de documento <i className="req">*</i></span>
        <select value={valor.tipoDocumentoId || ''} onChange={(e) => set('tipoDocumentoId', Number(e.target.value))}>
          <option value="">Seleccione…</option>
          {tipos.map((t) => <option key={t.id} value={t.id}>{t.nombre}</option>)}
        </select>
      </label>
      <label className="campo">
        <span>Área <i className="req">*</i></span>
        <select value={valor.areaId || ''} onChange={(e) => set('areaId', Number(e.target.value))}>
          <option value="">Seleccione…</option>
          {areas.map((a) => <option key={a.id} value={a.id}>{a.nombre}</option>)}
        </select>
      </label>
      {!ocultarTitulo && (
        <label className="campo col-2">
          <span>Título <i className="req">*</i></span>
          <input value={valor.titulo} onChange={(e) => set('titulo', e.target.value)} maxLength={300} />
        </label>
      )}
      <label className="campo">
        <span>Identificador del titular</span>
        <input value={valor.identificador ?? ''} onChange={(e) => set('identificador', e.target.value)}
          placeholder="Cédula, N° de socio, expediente…" maxLength={60} className="mono" />
        <small>Agrupa todos los documentos de la misma persona o caso.</small>
      </label>
      <label className="campo">
        <span>Nombre del titular</span>
        <input value={valor.nombreTitular ?? ''} onChange={(e) => set('nombreTitular', e.target.value)} maxLength={250} />
      </label>
      <label className="campo">
        <span>Fecha del documento</span>
        <input type="date" value={(valor.fechaDocumento ?? '').slice(0, 10)} onChange={(e) => set('fechaDocumento', e.target.value)} />
      </label>
      <label className="campo">
        <span>Etiquetas</span>
        <input value={valor.etiquetas ?? ''} onChange={(e) => set('etiquetas', e.target.value)} placeholder="Separadas por coma" maxLength={500} />
      </label>

      {tipo && tipo.campos.filter((c) => c.activo).length > 0 && (
        <fieldset className="col-2 campos-tipo">
          <legend>Datos de {tipo.nombre}</legend>
          <div className="form-grid">
            {tipo.campos.filter((c) => c.activo).sort((a, b) => a.orden - b.orden).map((c) => (
              <CampoDinamico key={c.id} campo={c} valor={valor.valores[c.clave] ?? ''} alCambiar={(v) => setValor(c.clave, v)} />
            ))}
          </div>
        </fieldset>
      )}

      <label className="campo col-2">
        <span>Descripción / observaciones</span>
        <textarea rows={compacto ? 2 : 3} value={valor.descripcion ?? ''} onChange={(e) => set('descripcion', e.target.value)} maxLength={2000} />
      </label>
    </div>
  )
}

function CampoDinamico({ campo, valor, alCambiar }: { campo: Campo; valor: string; alCambiar: (v: string) => void }) {
  const etiqueta = <span>{campo.etiqueta} {campo.obligatorio && <i className="req">*</i>}</span>
  switch (campo.tipoDato) {
    case TipoDatoCampo.Numero:
      return <label className="campo">{etiqueta}<input type="number" step="any" value={valor} onChange={(e) => alCambiar(e.target.value)} /></label>
    case TipoDatoCampo.Fecha:
      return <label className="campo">{etiqueta}<input type="date" value={valor.slice(0, 10)} onChange={(e) => alCambiar(e.target.value)} /></label>
    case TipoDatoCampo.Lista:
      return (
        <label className="campo">{etiqueta}
          <select value={valor} onChange={(e) => alCambiar(e.target.value)}>
            <option value="">Seleccione…</option>
            {(campo.opciones ?? '').split('|').map((o) => o.trim()).filter(Boolean).map((o) => <option key={o}>{o}</option>)}
          </select>
        </label>
      )
    case TipoDatoCampo.SiNo:
      return (
        <label className="campo">{etiqueta}
          <select value={valor} onChange={(e) => alCambiar(e.target.value)}>
            <option value="">—</option>
            <option value="true">Sí</option>
            <option value="false">No</option>
          </select>
        </label>
      )
    default:
      return <label className="campo">{etiqueta}<input value={valor} onChange={(e) => alCambiar(e.target.value)} maxLength={1000} /></label>
  }
}

/** Muestra un valor de campo dinámico de forma legible. */
export function mostrarValor(tipo: TipoDatoCampo, v?: string) {
  if (v == null || v === '') return '—'
  if (tipo === TipoDatoCampo.SiNo) return v === 'true' ? 'Sí' : 'No'
  if (tipo === TipoDatoCampo.Fecha) { const [a, m, d] = v.slice(0, 10).split('-'); return `${d}/${m}/${a}` }
  if (tipo === TipoDatoCampo.Numero) return Number(v).toLocaleString('es-NI')
  return v
}
