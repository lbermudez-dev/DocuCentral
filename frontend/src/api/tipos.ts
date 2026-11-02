export enum TipoAutenticacion { Local = 0, Directorio = 1 }
export enum TipoDatoCampo { Texto = 0, Numero = 1, Fecha = 2, Lista = 3, SiNo = 4 }
export enum EstadoDocumento { PendienteIndexar = 0, Activo = 1, Eliminado = 2 }
export enum OrigenDocumento { CargaWeb = 0, CarpetaVigilada = 1 }

export const NombreTipoDato: Record<TipoDatoCampo, string> = {
  [TipoDatoCampo.Texto]: 'Texto',
  [TipoDatoCampo.Numero]: 'Número',
  [TipoDatoCampo.Fecha]: 'Fecha',
  [TipoDatoCampo.Lista]: 'Lista',
  [TipoDatoCampo.SiNo]: 'Sí / No',
}

export const P = {
  DocumentosVer: 'documentos.ver',
  DocumentosCargar: 'documentos.cargar',
  DocumentosEditar: 'documentos.editar',
  DocumentosDescargar: 'documentos.descargar',
  DocumentosEliminar: 'documentos.eliminar',
  DocumentosTodasAreas: 'documentos.todas_areas',
  BandejaIndexar: 'bandeja.indexar',
  TiposAdministrar: 'tipos.administrar',
  AreasAdministrar: 'areas.administrar',
  UsuariosAdministrar: 'usuarios.administrar',
  RolesAdministrar: 'roles.administrar',
  ConfiguracionAdministrar: 'configuracion.administrar',
  AuditoriaVer: 'auditoria.ver',
} as const

export interface UsuarioSesion {
  id: number
  nombreUsuario: string
  nombreCompleto: string
  area?: string
  debeCambiarPassword: boolean
  tipoAutenticacion: TipoAutenticacion
  permisos: string[]
}

export interface LoginResponse { token: string; expira: string; usuario: UsuarioSesion }

export interface Area { id: number; codigo: string; nombre: string; activo: boolean }

export interface Campo {
  id: number
  clave: string
  etiqueta: string
  tipoDato: TipoDatoCampo
  obligatorio: boolean
  opciones?: string | null
  orden: number
  activo: boolean
}

export interface TipoDocumento { id: number; nombre: string; descripcion?: string; activo: boolean; campos: Campo[] }

export interface DocumentoResumen {
  id: string
  titulo: string
  identificador?: string
  nombreTitular?: string
  tipoDocumentoId?: number
  tipoDocumento?: string
  areaId?: number
  area?: string
  fechaDocumento?: string
  creadoEn: string
  versionActual: number
  extension?: string
  tamanoBytes: number
  estado: EstadoDocumento
  origen: OrigenDocumento
}

export interface ValorCampo { campoId: number; clave: string; etiqueta: string; tipoDato: TipoDatoCampo; valor?: string }

export interface Version {
  id: number
  numeroVersion: number
  nombreOriginal: string
  extension: string
  tipoMime: string
  tamanoBytes: number
  hashSha256: string
  comentario?: string
  creadoPor?: string
  creadoEn: string
}

export interface DocumentoDetalle {
  id: string
  titulo: string
  descripcion?: string
  identificador?: string
  nombreTitular?: string
  etiquetas?: string
  fechaDocumento?: string
  tipoDocumentoId?: number
  tipoDocumento?: string
  areaId?: number
  area?: string
  estado: EstadoDocumento
  origen: OrigenDocumento
  versionActual: number
  creadoPor?: string
  creadoEn: string
  modificadoEn?: string
  valores: ValorCampo[]
  versiones: Version[]
}

export interface Metadatos {
  tipoDocumentoId: number
  areaId: number
  titulo: string
  descripcion?: string
  identificador?: string
  nombreTitular?: string
  etiquetas?: string
  fechaDocumento?: string | null
  valores: Record<string, string>
}

export interface Pagina<T> { items: T[]; total: number; pagina: number; tamanoPagina: number }

export interface Busqueda {
  texto?: string
  identificador?: string
  nombreTitular?: string
  tipoDocumentoId?: number
  areaId?: number
  fechaDesde?: string
  fechaHasta?: string
  campos?: Record<number, string>
  orden?: string
  pagina?: number
  tamanoPagina?: number
}

export interface ExpedienteResumen {
  identificador: string
  nombreTitular?: string
  cantidadDocumentos: number
  ultimaModificacion: string
  tipos: string[]
}

export interface Expediente { identificador: string; nombreTitular?: string; documentos: DocumentoResumen[] }

export interface Usuario {
  id: number
  nombreUsuario: string
  nombreCompleto: string
  email?: string
  tipoAutenticacion: TipoAutenticacion
  activo: boolean
  bloqueado: boolean
  areaId?: number | null
  area?: string
  rolIds: number[]
  areasAdicionalesIds: number[]
  ultimoAcceso?: string
}

export interface Rol { id: number; nombre: string; descripcion?: string; esSistema: boolean; permisos: string[]; usuarios: number }
export interface DefinicionPermiso { codigo: string; descripcion: string; grupo: string }

export interface RegistroAuditoria {
  id: number
  fecha: string
  nombreUsuario: string
  accion: string
  entidad: string
  entidadId?: string
  detalle?: string
  direccionIp?: string
}

export interface EstadoIngesta {
  activa: boolean
  rutaEntrada: string
  ultimaEjecucion?: string
  importadosUltimaEjecucion: number
  erroresUltimaEjecucion: number
  ultimoError?: string
  pendientesIndexar: number
}
