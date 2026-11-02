// Cliente HTTP mínimo sobre fetch. El token se guarda en sessionStorage:
// se pierde al cerrar la pestaña, lo que reduce el riesgo en equipos compartidos.

const BASE = import.meta.env.VITE_API_URL ?? ''
const CLAVE_TOKEN = 'dc_token'

export class ErrorApi extends Error {
  constructor(public status: number, mensaje: string, public codigo?: string) {
    super(mensaje)
  }
}

export const sesion = {
  token: () => sessionStorage.getItem(CLAVE_TOKEN),
  guardar: (t: string) => sessionStorage.setItem(CLAVE_TOKEN, t),
  limpiar: () => sessionStorage.removeItem(CLAVE_TOKEN),
}

/** Se dispara cuando el servidor rechaza la sesión (token vencido o revocado). */
export const EVENTO_SESION_EXPIRADA = 'dc:sesion-expirada'

async function procesar(res: Response): Promise<Response> {
  if (res.ok) return res
  let mensaje = `Error ${res.status}`
  let codigo: string | undefined
  try {
    const cuerpo = await res.json()
    mensaje = cuerpo.mensaje ?? cuerpo.title ?? cuerpo.detail ?? mensaje
    codigo = cuerpo.codigo
    if (cuerpo.errors) mensaje = Object.values(cuerpo.errors).flat().join(' ')
  } catch { /* respuesta sin JSON */ }
  if (res.status === 401 && sesion.token()) {
    sesion.limpiar()
    window.dispatchEvent(new Event(EVENTO_SESION_EXPIRADA))
  }
  if (res.status === 403 && mensaje.startsWith('Error')) mensaje = 'No tiene permiso para realizar esta acción.'
  if (res.status === 429) mensaje = 'Demasiados intentos. Espere un minuto e intente de nuevo.'
  throw new ErrorApi(res.status, mensaje, codigo)
}

function cabeceras(extra?: HeadersInit): Headers {
  const h = new Headers(extra)
  const t = sesion.token()
  if (t) h.set('Authorization', `Bearer ${t}`)
  return h
}

export async function pedir<T>(ruta: string, opciones: RequestInit = {}): Promise<T> {
  const h = cabeceras(opciones.headers)
  if (opciones.body && !(opciones.body instanceof FormData)) h.set('Content-Type', 'application/json')
  const res = await procesar(await fetch(BASE + ruta, { ...opciones, headers: h }))
  if (res.status === 204 || res.status === 202) return undefined as T
  const tipo = res.headers.get('content-type') ?? ''
  return (tipo.includes('json') ? res.json() : res.text()) as Promise<T>
}

export const api = {
  get: <T>(ruta: string) => pedir<T>(ruta),
  post: <T>(ruta: string, cuerpo?: unknown) =>
    pedir<T>(ruta, { method: 'POST', body: cuerpo instanceof FormData ? cuerpo : JSON.stringify(cuerpo ?? {}) }),
  put: <T>(ruta: string, cuerpo: unknown) => pedir<T>(ruta, { method: 'PUT', body: JSON.stringify(cuerpo) }),
  del: <T>(ruta: string) => pedir<T>(ruta, { method: 'DELETE' }),

  /** Descarga un archivo autenticado como Blob (para el visor o para guardarlo). */
  async blob(ruta: string): Promise<{ blob: Blob; nombre?: string }> {
    const res = await procesar(await fetch(BASE + ruta, { headers: cabeceras() }))
    const disp = res.headers.get('content-disposition') ?? ''
    const m = /filename\*=UTF-8''([^;]+)/i.exec(disp) ?? /filename="?([^";]+)"?/i.exec(disp)
    return { blob: await res.blob(), nombre: m ? decodeURIComponent(m[1]) : undefined }
  },

  /** Subida con barra de progreso (fetch no informa progreso de envío). */
  subir<T>(ruta: string, datos: FormData, alProgresar?: (pct: number) => void): Promise<T> {
    return new Promise((resolver, rechazar) => {
      const xhr = new XMLHttpRequest()
      xhr.open('POST', BASE + ruta)
      const t = sesion.token()
      if (t) xhr.setRequestHeader('Authorization', `Bearer ${t}`)
      xhr.upload.onprogress = (e) => e.lengthComputable && alProgresar?.(Math.round((e.loaded / e.total) * 100))
      xhr.onload = () => {
        let cuerpo: any = undefined
        try { cuerpo = xhr.responseText ? JSON.parse(xhr.responseText) : undefined } catch { /* vacío */ }
        if (xhr.status >= 200 && xhr.status < 300) resolver(cuerpo as T)
        else rechazar(new ErrorApi(xhr.status, cuerpo?.mensaje ?? cuerpo?.title ?? `Error ${xhr.status}`))
      }
      xhr.onerror = () => rechazar(new ErrorApi(0, 'No se pudo conectar con el servidor.'))
      xhr.send(datos)
    })
  },
}

export function guardarBlob(blob: Blob, nombre: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = nombre
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 2000)
}
